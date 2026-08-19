// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.IntegrationTests.Orchestration;
using MailFathom.IntegrationTests.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Synchronization;

/// <summary>
/// Proves that a synchronization run recognizes MailFathom's own writes in the mailbox, and stops recognizing one once
/// it has been accounted for.
/// </summary>
/// <remarks>
/// <para>
/// Three queries whose whole value is what they exclude. Each of them narrows on a batch of UIDs, a mutation name held
/// as text, a stage, and — for the placement read — a nullable UID column that must match a value and never a null. A
/// substitute answers whichever list the arrangement handed it, so none of that translation is observable there, and
/// each way it can fail is silent in the same direction: a run stops recognizing its own work and reconciles it as a
/// change somebody else made, which is a local row erased or a relocation performed twice.
/// </para>
/// <para>
/// No mail server takes part. What the reads answer is a question about rows, and the sibling class that drives the same
/// records against a real server is what proves the stages they pass through.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedMutationReconciliationReadTests(MailFathomOrchestrationFixture orchestration)
{
    private const string FolderAlias = "mutation-reconciliation";

    private const string DestinationFolderName = "MutationReconciliationArchive";

    /// <summary>The UIDVALIDITY the destination folder reported when it answered the copy.</summary>
    private static readonly ImapUidValidity PlacementUidValidity = ImapUidValidity.Create(4_100_000_007);

    /// <summary>The UID the destination folder assigned the copy, which is the identity the next run meets.</summary>
    private static readonly ImapUid PlacementUid = ImapUid.Create(5101);

    /// <summary>A UID in the destination folder that no mutation of this suite's ever placed anything at.</summary>
    private static readonly ImapUid UnplacedUid = ImapUid.Create(5199);

    private static readonly RemoteFolderPath DestinationPath =
        RemoteFolderPath.Create(DestinationFolderName, hierarchyDelimiter: '.');

    /// <summary>The instant a run writes down that it recognized its own work, stated so the row is comparable.</summary>
    private static readonly DateTimeOffset ObservedAt = new(2026, 5, 4, 9, 0, 0, TimeSpan.Zero);

    /// <summary>An age bound every record this class writes is newer than, which is what a window whose reading predates them passes.</summary>
    private static readonly DateTimeOffset BeforeEveryRecord = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly MailboxMutationRequester Requester =
        MailboxMutationRequester.Rule("reconciliation-read", "1");

    /// <summary>
    /// The whole convergence loop for a relocation MailFathom performed: the run meeting the new occurrence finds the
    /// record that explains it, the run meeting the source folder's gap finds the same record, and once the placement is
    /// written down as observed the first read stops returning it. The read is run once more against a UID nothing was
    /// placed at, so the empty answer at the end is the record having been accounted for rather than the query matching
    /// nothing whatever it is asked.
    /// </summary>
    [Fact]
    public async Task ReadPlacementsAtAsync_ARelocationThisInstancePerformed_IsRecognizedUntilItIsObserved()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var occurrence = SyntheticEmail.OccurrenceIn(binding, uid: 5001);
        var storedEmailId = await StoreMetadataAsync(services, occurrence, "relocated", cancellationToken);

        var recordId = await CompletedRelocationAsync(
            services,
            MailboxMutationRequest.Relocate(storedEmailId, occurrence, Requester, DestinationPath),
            cancellationToken);

        // Act
        var recognizedAtDestination = await ReadPlacementsAsync(services, PlacementUid, cancellationToken);
        var recognizedAtSource = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IMailboxMutationReconciliationStore>()
                .ReadMutationsRemovingAsync(
                    SyntheticMailAccount.AccountId,
                    occurrence.FolderResolutionId,
                    occurrence.UidValidity,
                    [occurrence.Uid],
                    token),
            cancellationToken);

        Assert.Equal(
            PersistenceCommitResult.Committed,
            await services.CommitAsync(
                (scope, session, token) => scope.GetRequiredService<IMailboxMutationReconciliationStore>()
                    .RecordPlacementObservedAsync(session, recordId, ObservedAt, token),
                cancellationToken));

        var afterObservation = await ReadPlacementsAsync(services, PlacementUid, cancellationToken);
        var atAUidNothingWasPlacedAt = await ReadPlacementsAsync(services, UnplacedUid, cancellationToken);

        // Assert
        Assert.Equal(recordId, Assert.Single(recognizedAtDestination).Id);
        Assert.Equal(recordId, Assert.Single(recognizedAtSource).Id);
        Assert.Empty(afterObservation);
        Assert.Empty(atAUidNothingWasPlacedAt);
    }

    /// <summary>
    /// A run reading the flags of a window has to tell a <c>\Seen</c> this instance set from one a person set in their
    /// client, and the mutation's name is the whole of that distinction. The relocation seeded beside it is what makes
    /// the narrowing decidable: it shares the folder and the UIDVALIDITY, so a read that ignored the name would return
    /// it. Two more halves of what this query excludes are asserted here for the same reason — both are comparisons
    /// against columns only a database performs. A bound at the record's own stage change must return nothing, which is
    /// what keeps an occurrence's accumulated stores off every later window; and a store still at
    /// <see cref="MailboxMutationStage.Recorded" /> must return nothing either, because it explains no reading and
    /// carries the newest stage change in the table, so a read that admitted it would spend an occurrence's budget on
    /// rows that account for nothing.
    /// </summary>
    [Fact]
    public async Task ReadFlagChangesOnAsync_AWindowHoldingBothKindsOfMutation_ReturnsOnlyTheIssuedStoresAfterTheBound()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);

        var seenOccurrence = SyntheticEmail.OccurrenceIn(binding, uid: 5002);
        var relocatedOccurrence = SyntheticEmail.OccurrenceIn(binding, uid: 5003);
        var writtenDownOccurrence = SyntheticEmail.OccurrenceIn(binding, uid: 5004);
        var seenEmailId = await StoreMetadataAsync(services, seenOccurrence, "flagged", cancellationToken);
        var relocatedEmailId = await StoreMetadataAsync(services, relocatedOccurrence, "moved", cancellationToken);
        var writtenDownEmailId = await StoreMetadataAsync(services, writtenDownOccurrence, "pending", cancellationToken);

        var seenRecordId = await CompletedSeenStoreAsync(
            services,
            MailboxMutationRequest.SetSeen(seenEmailId, seenOccurrence, Requester, isSeen: true),
            cancellationToken);
        await OpenAsync(
            services,
            MailboxMutationRequest.Relocate(relocatedEmailId, relocatedOccurrence, Requester, DestinationPath),
            cancellationToken);

        // Opened and left where the tool leaves it, which is the state every change is in until the account's next run
        // issues it. It is written down last, so it also carries the newest stage change of the three.
        await OpenAsync(
            services,
            MailboxMutationRequest.SetSeen(writtenDownEmailId, writtenDownOccurrence, Requester, isSeen: true),
            cancellationToken);

        // Act
        var seenStateChanges = await ReadFlagChangesAsync(
            services,
            binding.Id,
            seenOccurrence.UidValidity,
            [seenOccurrence.Uid, relocatedOccurrence.Uid, writtenDownOccurrence.Uid],
            BeforeEveryRecord,
            cancellationToken);
        var afterTheStoreWasIssued = await ReadFlagChangesAsync(
            services,
            binding.Id,
            seenOccurrence.UidValidity,
            [seenOccurrence.Uid, relocatedOccurrence.Uid, writtenDownOccurrence.Uid],
            seenStateChanges[0].StageChangedAt,
            cancellationToken);

        // Assert
        Assert.Equal(seenRecordId, Assert.Single(seenStateChanges).Id);
        Assert.Empty(afterTheStoreWasIssued);
    }

    /// <summary>
    /// The per-value budget is spent inside each occurrence's each value, and only a database settles that: the limit
    /// rides a lateral join whose <c>ORDER BY</c> and <c>LIMIT</c> run per group, and a translation that spent it
    /// across the answer, or ranked the rows after they had crossed the boundary, is invisible in any arrangement where
    /// every group holds one row. So one occurrence is given more stores of one value than the budget and a second is
    /// given a single older one. A budget spent window-wide returns the six newest and drops the second occurrence's
    /// record — which would leave that occurrence's flag credited to the mailbox owner and the rule that wrote it
    /// re-firing on the mail it had just marked.
    /// </summary>
    [Fact]
    public async Task ReadFlagChangesOnAsync_AnOccurrenceCarryingMoreStoresOfOneValueThanTheBudget_LeavesAnotherOccurrencesStoreAlone()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);

        var crowdedOccurrence = SyntheticEmail.OccurrenceIn(binding, uid: 5011);
        var quietOccurrence = SyntheticEmail.OccurrenceIn(binding, uid: 5012);
        var crowdedEmailId = await StoreMetadataAsync(services, crowdedOccurrence, "triaged", cancellationToken);
        var quietEmailId = await StoreMetadataAsync(services, quietOccurrence, "read once", cancellationToken);

        // Written down and completed first, so it is the oldest stage change of the lot and therefore the row a
        // newest-first truncation across the whole answer drops.
        var quietRecordId = await CompletedSeenStoreAsync(
            services,
            MailboxMutationRequest.SetSeen(quietEmailId, quietOccurrence, Requester, isSeen: true),
            cancellationToken);
        var crowdedRecordIds = new List<MailboxMutationRecordId>();

        for (var call = 0; call <= IMailboxMutationReconciliationStore.MaximumFlagChangeRecordsPerValue; call++)
        {
            crowdedRecordIds.Add(await CompletedSeenStoreAsync(
                services,
                MailboxMutationRequest.SetSeen(
                    crowdedEmailId,
                    crowdedOccurrence,
                    MailboxMutationRequester.Command($"triage-{call}"),
                    isSeen: call % 2 == 0),
                cancellationToken));
        }

        // Act
        var flagChanges = await ReadFlagChangesAsync(
            services,
            binding.Id,
            crowdedOccurrence.UidValidity,
            [crowdedOccurrence.Uid, quietOccurrence.Uid],
            BeforeEveryRecord,
            cancellationToken);

        // Assert
        Assert.Equal(
            IMailboxMutationReconciliationStore.MaximumFlagChangeRecordsPerValue,
            flagChanges.Count(change => change.Request.Occurrence.Uid == crowdedOccurrence.Uid));
        Assert.Equal(
            quietRecordId,
            Assert.Single(flagChanges, change => change.Request.Occurrence.Uid == quietOccurrence.Uid).Id);

        // The five kept are the newest of the six, which is what makes the budget the recent history of the value
        // rather than an arbitrary five of it.
        Assert.DoesNotContain(crowdedRecordIds[0], flagChanges.Select(change => change.Id));
    }

    private static Task<IReadOnlyList<MailboxMutationRecord>> ReadFlagChangesAsync(
        OrchestratedMailFathomServices services,
        MailFolderResolutionId folderResolutionId,
        ImapUidValidity uidValidity,
        IReadOnlyCollection<ImapUid> uids,
        DateTimeOffset issuedAfter,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IMailboxMutationReconciliationStore>()
                .ReadFlagChangesOnAsync(
                    SyntheticMailAccount.AccountId,
                    folderResolutionId,
                    uidValidity,
                    uids,
                    issuedAfter,
                    token),
            cancellationToken);

    private static Task<IReadOnlyList<MailboxMutationRecord>> ReadPlacementsAsync(
        OrchestratedMailFathomServices services,
        ImapUid uid,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IMailboxMutationReconciliationStore>().ReadPlacementsAtAsync(
                SyntheticMailAccount.AccountId,
                DestinationPath,
                PlacementUidValidity,
                [uid],
                token),
            cancellationToken);

    /// <summary>Drives one relocation to the state a finished mutation leaves, through the production store.</summary>
    /// <remarks>
    /// Every stage is written by the store rather than onto the row, so the record the reads above meet is the one a
    /// performer produces. The placement is the identity a <c>COPYUID</c> response would have named, which is what the
    /// next synchronization run recognizes the new occurrence by.
    /// </remarks>
    private static async Task<MailboxMutationRecordId> CompletedRelocationAsync(
        OrchestratedMailFathomServices services,
        MailboxMutationRequest request,
        CancellationToken cancellationToken)
    {
        var recordId = await OpenAsync(services, request, cancellationToken);

        Assert.Equal(
            PersistenceCommitResult.Committed,
            await services.CommitAsync(
                async (scope, session, token) =>
                {
                    var store = scope.GetRequiredService<IMailboxMutationRecordStore>();

                    await store.RecordPlacementIssuedAsync(session, recordId, requiresSourceRemoval: true, token);
                    await store.AdvanceAsync(
                        session,
                        recordId,
                        MailboxMutationStage.PlacementConfirmed,
                        RemoteEmailPlacement.Reported(PlacementUidValidity, PlacementUid),
                        token);
                    await store.AdvanceAsync(session, recordId, MailboxMutationStage.Completed, placement: null, token);
                },
                cancellationToken));

        return recordId;
    }

    /// <summary>Drives one <c>\Seen</c> store to the state a finished flag mutation leaves, through the production store.</summary>
    /// <remarks>
    /// A flag store reaches <see cref="MailboxMutationStage.Completed" /> from
    /// <see cref="MailboxMutationStage.Recorded" /> directly, since nothing about it is placed anywhere, and the stage
    /// is written by the store rather than onto the row so the record a read meets is the one a performer produces.
    /// </remarks>
    private static async Task<MailboxMutationRecordId> CompletedSeenStoreAsync(
        OrchestratedMailFathomServices services,
        MailboxMutationRequest request,
        CancellationToken cancellationToken)
    {
        var recordId = await OpenAsync(services, request, cancellationToken);

        Assert.Equal(
            PersistenceCommitResult.Committed,
            await services.CommitAsync(
                (scope, session, token) => scope.GetRequiredService<IMailboxMutationRecordStore>()
                    .AdvanceAsync(session, recordId, MailboxMutationStage.Completed, placement: null, token),
                cancellationToken));

        return recordId;
    }

    private static Task<MailboxMutationRecordId> OpenAsync(
        OrchestratedMailFathomServices services,
        MailboxMutationRequest request,
        CancellationToken cancellationToken) => CommitForAsync(
            services,
            async (scope, session, token) => (await scope.GetRequiredService<IMailboxMutationRecordStore>()
                .OpenAsync(session, request, token)).Id,
            cancellationToken);

    private static Task<StoredEmailId> StoreMetadataAsync(
        OrchestratedMailFathomServices services,
        EmailOccurrenceId occurrence,
        string subject,
        CancellationToken cancellationToken) => CommitForAsync(
            services,
            (scope, session, token) => scope.GetRequiredService<IEmailMetadataRepository>().UpsertMetadataAsync(
                session,
                SyntheticEmail.RemoteMetadataOf(occurrence, subject),
                extractedMetadata: null,
                StoredEmailContentAvailability.ExceededSizeLimit,
                token),
            cancellationToken);

    /// <summary>Commits one write and hands back what it produced, asserting the commit where it happened.</summary>
    private static Task<TResult> CommitForAsync<TResult>(
        OrchestratedMailFathomServices services,
        Func<IServiceProvider, IPersistenceSession, CancellationToken, Task<TResult>> write,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) =>
            {
                await using var session = await scope.GetRequiredService<IPersistenceSessionFactory>()
                    .BeginSessionAsync(token);

                var produced = await write(scope, session, token);

                Assert.Equal(PersistenceCommitResult.Committed, await session.CommitAsync(token));

                return produced;
            },
            cancellationToken);
}
