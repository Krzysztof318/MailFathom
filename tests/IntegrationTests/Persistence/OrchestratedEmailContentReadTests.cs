// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.EmailContent;
using MailMcp.Application.Emails;
using MailMcp.Application.Persistence;
using MailMcp.Application.Synchronization;
using MailMcp.Domain.Emails;
using MailMcp.Infrastructure.Persistence;
using MailMcp.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailMcp.IntegrationTests.Persistence;

/// <summary>Proves the two database-side halves of reading one email's content against real PostgreSQL.</summary>
/// <remarks>
/// Neither is reachable from a unit test. The summary lookup is a projection the provider has to translate into a
/// primary-key query over columns that exist, and the repair request is raw SQL naming a table and quoted column names
/// that only a real schema can confirm — its idempotency is the primary key's rather than the code's, so only a second
/// real write establishes it.
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedEmailContentReadTests(MailMcpOrchestrationFixture orchestration)
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
        await using var services = await OrchestratedMailMcpServices.StartAsync(orchestration, cancellationToken);
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
        await using var services = await OrchestratedMailMcpServices.StartAsync(orchestration, cancellationToken);
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
        Assert.True(
            request.LastRequestedAt >= request.FirstRequestedAt,
            "The last sighting of a defect cannot predate the first one.");
    }

    private static Task<EmailContentRepairRequestEntity[]> ReadRequestsAsync(
        OrchestratedMailMcpServices services,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope
                .GetRequiredService<MailMcpDbContext>()
                .EmailContentRepairRequests
                .AsNoTracking()
                .Where(request => request.StoredEmailId == storedEmailId.Value)
                .ToArrayAsync(token),
            cancellationToken);

    private static async Task<StoredEmailId> StoreMetadataAsync(
        OrchestratedMailMcpServices services,
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
