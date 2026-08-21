// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Repair;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves the two database-side halves of reading one email's content against real PostgreSQL.</summary>
/// <remarks>
/// Neither is reachable from a unit test. The summary lookup is a projection the provider has to translate into a
/// primary-key query over columns that exist, and the repair request is raw SQL naming a table and quoted column names
/// that only a real schema can confirm — its idempotency is the primary key's rather than the code's, so only a second
/// real write establishes it.
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedEmailContentReadTests(MailFathomOrchestrationFixture orchestration)
{
    private const string FolderAlias = "content-read";

    private const uint LookedUpUid = 41;

    private const uint RepairedUid = 42;

    /// <summary>The lookup answers from the same projection a listing does, and reaches no stored payload doing it.</summary>
    [Fact]
    public async Task FindAsync_AStoredEmail_ReturnsItsSummaryWithoutTrackingAnything()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var occurrenceId = SyntheticEmail.OccurrenceIn(binding, LookedUpUid);
        var storedEmailId = await StoreMetadataAsync(services, occurrenceId, "Content lookup", cancellationToken);

        // Act
        var summary = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IStoredEmailSummaryReader>().FindAsync(storedEmailId, token),
            cancellationToken);

        var unknown = await services.InScopeAsync(
            (scope, token) => scope
                .GetRequiredService<IStoredEmailSummaryReader>()
                .FindAsync(StoredEmailId.Create(Guid.CreateVersion7()), token),
            cancellationToken);

        // Assert
        Assert.NotNull(summary);
        Assert.Equal(storedEmailId, summary.StoredEmailId);
        Assert.Equal("Content lookup", summary.Subject);
        Assert.Equal(occurrenceId.AccountId, summary.AccountId);
        Assert.Equal(FolderAlias.ToUpperInvariant(), summary.FolderAlias.Value);

        // An identifier nothing stored is an ordinary answer rather than a failure, which is what lets the use case
        // decide what absence means.
        Assert.Null(unknown);
    }

    /// <summary>A second read of the same damaged message leaves one request, with a count that has moved.</summary>
    [Fact]
    public async Task RecordAsync_TheSameEmailTwice_KeepsOneRowAndCountsBothReads()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var occurrenceId = SyntheticEmail.OccurrenceIn(binding, RepairedUid);
        var storedEmailId = await StoreMetadataAsync(services, occurrenceId, "Content repair", cancellationToken);

        // Act
        await services.InScopeAsync(
            async (scope, token) =>
            {
                await scope.GetRequiredService<IEmailContentRepairRequestStore>().RecordAsync(
                    new EmailContentRepairRequest(storedEmailId, EmailContentDefect.Missing),
                    token);

                return true;
            },
            cancellationToken);

        var afterFirstRead = Assert.Single(await ReadRequestsAsync(services, storedEmailId, cancellationToken));

        await services.InScopeAsync(
            async (scope, token) =>
            {
                await scope.GetRequiredService<IEmailContentRepairRequestStore>().RecordAsync(
                    new EmailContentRepairRequest(storedEmailId, EmailContentDefect.HashMismatch),
                    token);

                return true;
            },
            cancellationToken);

        // Assert
        var requests = await ReadRequestsAsync(services, storedEmailId, cancellationToken);

        var request = Assert.Single(requests);
        Assert.Equal(EmailContentDefect.HashMismatch, request.Defect);
        Assert.Equal(2, request.RequestCount);

        // The first sighting is what says how long the defect has been outstanding, so a later read must not move it.
        Assert.Equal(afterFirstRead.FirstRequestedAt, request.FirstRequestedAt);

        // Nothing is asserted about the ordering of the two timestamps against each other. Both come from the system
        // clock this composition registers, so any claim about their order would be a claim about that clock rather
        // than about the upsert — and two writes this close together can legitimately share a timestamp. What the
        // statement guarantees against a straggler is that the stored value never goes backwards, which is a property
        // of GREATEST rather than something a sequential pair of writes can exhibit.
    }

    private static Task<EmailContentRepairRequestEntity[]> ReadRequestsAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope
                .GetRequiredService<MailFathomDbContext>()
                .EmailContentRepairRequests
                .AsNoTracking()
                .Where(request => request.StoredEmailId == storedEmailId.Value)
                .ToArrayAsync(token),
            cancellationToken);

    private static async Task<StoredEmailId> StoreMetadataAsync(
        OrchestratedMailFathomServices services,
        EmailOccurrenceId occurrenceId,
        string subject,
        CancellationToken cancellationToken)
    {
        StoredEmailId? storedEmailId = null;

        var commitResult = await services.CommitAsync(
            async (scope, session, token) =>
            {
                storedEmailId = await scope.GetRequiredService<IEmailMetadataRepository>().UpsertMetadataAsync(
                    session,
                    SyntheticEmail.RemoteMetadataOf(occurrenceId, subject, sizeOctets: 2048),
                    extractedMetadata: null,
                    StoredEmailContentAvailability.Available,
                    token);
            },
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);

        return storedEmailId!.Value;
    }
}
