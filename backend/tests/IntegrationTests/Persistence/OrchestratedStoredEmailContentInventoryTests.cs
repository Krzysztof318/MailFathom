// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves the reads the storage ceiling depends on answer correctly against PostgreSQL.</summary>
/// <remarks>
/// <para>
/// Neither read is reachable from a unit test, and each fails in a way a substitute cannot reproduce. The queue read is
/// EF Core LINQ over a navigation-property filter and an enum stored as text, and its filter has to match the one the
/// partial index <c>ix_stored_emails_awaiting_content</c> was created with, spelling for spelling; the occupancy read is
/// raw SQL over a PostgreSQL catalog function. A translation error or a filter that drifted from the index would leave
/// every unit test green — they run against an in-memory fake — and only surface as a refill pass that silently finds
/// nothing on a deployment that has reached its ceiling.
/// </para>
/// <para>
/// One test covers both reads and the three exclusions that matter, rather than one test per rule, because they share
/// one arrangement and a failure in any of them is distinguishable from the assertion that reports it.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedStoredEmailContentInventoryTests(MailFathomOrchestrationFixture orchestration)
{
    private const string FolderAlias = "content-inventory";

    /// <summary>The folder the UID-space test owns, so the row it stores cannot reach the queue the test above reads.</summary>
    /// <remarks>
    /// The queue read is answered per folder rather than per UID, so a second test storing an awaiting occurrence in this
    /// class's own folder is reported by the first one whenever xUnit runs it first — as an extra UID in a window the
    /// assertion says holds one. Distinct UIDs are not enough for that, which is what a folder of its own supplies.
    /// </remarks>
    private const string UidSpaceFolderAlias = "content-inventory-uid-space";

    private const uint AwaitingUid = 41;

    private const uint AvailableUid = 42;

    private const uint TombstonedUid = 43;

    private const uint ForeignUidValidityUid = 44;

    [Fact]
    public async Task GetEmailsAwaitingContentAsync_ForOneFolder_ReturnsOnlyTheOccurrencesStillWaitingForRoom()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);

        var awaiting = SyntheticEmail.OccurrenceIn(binding, AwaitingUid);
        await RecordAsync(services, awaiting, StoredEmailContentAvailability.AwaitingStorageHeadroom, cancellationToken);

        // Content that is present must not be reported as waiting for room.
        var available = SyntheticEmail.OccurrenceIn(binding, AvailableUid);
        await RecordAsync(services, available, StoredEmailContentAvailability.Available, cancellationToken);

        // A message that has left its folder must not be fetched again, however it was recorded.
        var tombstoned = SyntheticEmail.OccurrenceIn(binding, TombstonedUid);
        await RecordAsync(services, tombstoned, StoredEmailContentAvailability.AwaitingStorageHeadroom, cancellationToken);
        await TombstoneAsync(services, tombstoned, cancellationToken);

        // Act
        var reported = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IStoredEmailContentInventory>().GetEmailsAwaitingContentAsync(
                SyntheticMailAccount.Account,
                binding.Id,
                awaiting.UidValidity,
                maxEmailCount: 50,
                token),
            cancellationToken);

        // Assert
        var reportedUids = reported.Select(candidate => candidate.Metadata.OccurrenceId.Uid.Value).ToArray();
        Assert.Equal([AwaitingUid], reportedUids);

        // The projection is what a refetch is committed under, so it has to carry back what the row was stored with.
        var onlyOne = Assert.Single(reported);
        Assert.Equal(awaiting, onlyOne.Metadata.OccurrenceId);
        Assert.Equal(SyntheticEmail.RemoteMetadataOf(awaiting, "awaiting-content", 4096).SizeOctets, onlyOne.Metadata.SizeOctets);

        // Nothing filed this occurrence, and the join has to say so rather than defaulting to whatever suppresses least.
        Assert.False(onlyOne.IsFiledCopy);
    }

    /// <summary>A folder the server recreated is a different UID space, so its stored occurrences are not fetchable.</summary>
    /// <remarks>
    /// Fetching by a UID recorded under a previous UIDVALIDITY would retrieve whatever message the current space happens
    /// to hold at that number, which is how a refill pass would attach one person's mail to another's row.
    /// </remarks>
    [Fact]
    public async Task GetEmailsAwaitingContentAsync_ForAnotherUidValidity_ReportsNothingFromTheOneStored()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, UidSpaceFolderAlias, cancellationToken);
        var stored = SyntheticEmail.OccurrenceIn(binding, ForeignUidValidityUid);
        await RecordAsync(services, stored, StoredEmailContentAvailability.AwaitingStorageHeadroom, cancellationToken);

        // Act
        var reported = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IStoredEmailContentInventory>().GetEmailsAwaitingContentAsync(
                SyntheticMailAccount.Account,
                binding.Id,
                ImapUidValidity.Create(stored.UidValidity.Value + 1),
                maxEmailCount: 50,
                token),
            cancellationToken);

        // Assert
        Assert.Empty(reported);
    }

    /// <summary>The occupancy read is what a ceiling is compared against, so it has to describe a store that holds mail.</summary>
    [Fact]
    public async Task GetStoredContentBytesAsync_WithContentStored_ReportsWhatTheContentTableOccupies()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var occurrenceId = SyntheticEmail.OccurrenceIn(binding, 45);
        var rawMime = SyntheticEmail.RawMimeOf("content-occupancy", 256 * 1024);

        var commitResult = await services.CommitAsync(
            async (scope, session, token) =>
            {
                var storedEmailId = await scope.GetRequiredService<IEmailMetadataRepository>().UpsertMetadataAsync(
                    session, SyntheticMailAccount.Owner,
                    SyntheticEmail.RemoteMetadataOf(occurrenceId, "content-occupancy", rawMime.Length),
                    extractedMetadata: null,
                    StoredEmailContentAvailability.Available,
                    token);

                await scope.GetRequiredService<IEmailContentStore>().SaveContentAsync(
                    session,
                    storedEmailId,
                    occurrenceId,
                    PlacedEmailContent.InDatabase(rawMime),
                    token);
            },
            cancellationToken);
        Assert.Equal(PersistenceCommitResult.Committed, commitResult);

        // Act
        var occupiedBytes = await services.InScopeAsync(
            (scope, token) => scope
                .GetRequiredService<IStoredEmailContentInventory>()
                .GetStoredContentBytesAsync(token),
            cancellationToken);

        // Assert
        // Physical occupancy rather than the sum of the payloads, so the assertion is that it accounts for at least the
        // bytes just written; the overhead above them is what the port documents and is not a number to pin.
        Assert.True(
            occupiedBytes >= rawMime.LongLength,
            $"The content store reported {occupiedBytes} bytes, which is below the {rawMime.LongLength} bytes just stored.");
    }

    /// <summary>Records one occurrence's metadata with the availability under test, and no content.</summary>
    private static async Task RecordAsync(
        OrchestratedMailFathomServices services,
        EmailOccurrenceId occurrenceId,
        StoredEmailContentAvailability availability,
        CancellationToken cancellationToken)
    {
        var commitResult = await services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IEmailMetadataRepository>().UpsertMetadataAsync(
                session, SyntheticMailAccount.Owner,
                SyntheticEmail.RemoteMetadataOf(occurrenceId, "awaiting-content", 4096),
                extractedMetadata: null,
                availability,
                token),
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);
    }

    /// <summary>Marks one stored occurrence as gone from its remote folder, which every mailbox read excludes.</summary>
    private static async Task TombstoneAsync(
        OrchestratedMailFathomServices services,
        EmailOccurrenceId occurrenceId,
        CancellationToken cancellationToken) =>
        await services.InScopeAsync(
            async (scope, token) =>
            {
                var dbContext = scope.GetRequiredService<MailFathomDbContext>();

                return await dbContext.StoredEmails
                    .Where(email => email.UidValidity == occurrenceId.UidValidity.Value
                        && email.Uid == occurrenceId.Uid.Value
                        && email.MailFolder.Alias == occurrenceId.FolderResolutionId.Alias.Value)
                    .ExecuteUpdateAsync(
                        email => email.SetProperty(
                            candidate => candidate.RemoteExpungeObservedAt,
                            SyntheticEmail.ReceivedAt),
                        token);
            },
            cancellationToken);
}
