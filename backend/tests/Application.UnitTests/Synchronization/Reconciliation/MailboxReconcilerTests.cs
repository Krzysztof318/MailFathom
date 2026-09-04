// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Application.Signals;
using MailFathom.Application.Synchronization;
using MailFathom.Application.Synchronization.Reconciliation;
using MailFathom.Application.Synchronization.Sessions;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Synchronization.Reconciliation;

public sealed class MailboxReconcilerTests
{
    private static readonly MailAccountIdentity Account =
        MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("primary"));

    private static readonly MailFolderResolution InboxFolder = MailFolderResolution.FirstBindingOf(
        MailFolderAlias.Create("inbox"),
        RemoteFolderPath.Create("INBOX", '/'));

    private static readonly ImapUidValidity SelectedUidValidity = ImapUidValidity.Create(7);

    private static readonly DateTimeOffset RunInstant = new(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The whole point of the pass: a UID the server stopped reporting leaves the mailbox a reader sees.</summary>
    [Fact]
    public async Task ReconcileAsync_ServerNoLongerHoldsAnOccurrence_TombstonesItWithoutRemovingTheRow()
    {
        // Arrange
        var store = new FakeReconciliationStore(StoredOccurrences(10, 11, 12));
        await using var mailboxSession = CreateSessionHolding(10, 12);
        var reconciler = CreateReconciler(store, RemotelyDeletedEmailDisposition.RetainTombstone);

        // Act
        var result = await reconciler.ReconcileAsync(
            mailboxSession,
            Account,
            InboxFolder,
            SelectedUidValidity,
            reconciledThroughModSeq: null,
            CancellationToken.None);

        // Assert
        Assert.Equal(2, result.ObservedEmailCount);
        Assert.Equal(1, result.RemotelyDeletedEmailCount);
        Assert.Equal([11U], store.TombstonedUids);
        Assert.Empty(store.RemovedUids);
        Assert.Equal(RunInstant, store.RowOf(11).RemoteExpungeObservedAt);
    }

    /// <summary>The second variant destroys the local copy instead of hiding it, and the account's setting is what chooses.</summary>
    [Fact]
    public async Task ReconcileAsync_AccountErasesLocalCopies_RemovesTheRowInsteadOfTombstoningIt()
    {
        // Arrange
        var store = new FakeReconciliationStore(StoredOccurrences(10, 11));
        await using var mailboxSession = CreateSessionHolding(10);
        var reconciler = CreateReconciler(store, RemotelyDeletedEmailDisposition.EraseLocalCopy);

        // Act
        var result = await reconciler.ReconcileAsync(
            mailboxSession,
            Account,
            InboxFolder,
            SelectedUidValidity,
            reconciledThroughModSeq: null,
            CancellationToken.None);

        // Assert
        Assert.Equal(1, result.RemotelyDeletedEmailCount);
        Assert.Equal([11U], store.RemovedUids);
        Assert.Empty(store.TombstonedUids);
    }

    /// <summary>An occurrence MailFathom moved or deleted itself left the folder because of that, not because somebody else deleted it.</summary>
    /// <remarks>
    /// The account erases local copies, which is the setting that makes the difference visible: without the record the
    /// row would be destroyed here, and for a relocation that would erase the local copy of mail that still exists in
    /// another folder.
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReconcileAsync_OccurrenceRemovedByAMutationMailFathomMade_AttributesItInsteadOfErasingTheLocalCopy(
        bool isRelocation)
    {
        // Arrange
        var store = new FakeReconciliationStore(StoredOccurrences(10, 11));
        var mutationStore = new InMemoryMailboxMutationReconciliationStore();
        var record = MutationRemoving(store.StoredEmailIdOf(11), uid: 11, isRelocation);
        mutationStore.Add(record);
        await using var mailboxSession = CreateSessionHolding(10);
        var reconciler = CreateReconciler(
            store,
            RemotelyDeletedEmailDisposition.EraseLocalCopy,
            mutationStore: mutationStore);

        // Act
        var result = await reconciler.ReconcileAsync(
            mailboxSession,
            Account,
            InboxFolder,
            SelectedUidValidity,
            reconciledThroughModSeq: null,
            CancellationToken.None);

        // Assert
        Assert.Equal(0, result.RemotelyDeletedEmailCount);
        Assert.Equal(1, result.OwnMutationCompletedEmailCount);
        Assert.Empty(store.RemovedUids);
        Assert.Empty(store.TombstonedUids);
        Assert.Equal(RunInstant, mutationStore.RecordOf(record.Id).SourceRemovalObservedAt);

        // The observation moves so the window reaches further into the folder on the next run rather than selecting
        // the same occurrence forever.
        Assert.Equal(RunInstant, store.RowOf(11).ObservedAt);
    }

    /// <summary>A delete the owner authored is disposed of by its own record rather than by the setting for somebody else's deletion.</summary>
    /// <remarks>
    /// The account erases what its server loses, which is the arrangement that makes the two settings distinguishable:
    /// every value below has to survive that, because reading the account's setting instead would destroy the local
    /// copy whichever disposition the delete was authored under.
    /// </remarks>
    [Theory]
    [InlineData(AuthoredDeleteEmailDisposition.RetainLocalCopy)]
    [InlineData(AuthoredDeleteEmailDisposition.RetainTombstone)]
    [InlineData(AuthoredDeleteEmailDisposition.EraseLocalCopy)]
    public async Task ReconcileAsync_OccurrenceRemovedByAnAuthoredDelete_CarriesTheDispositionItWasAuthoredUnder(
        AuthoredDeleteEmailDisposition authoredDisposition)
    {
        // Arrange
        var store = new FakeReconciliationStore(StoredOccurrences(11));
        var mutationStore = new InMemoryMailboxMutationReconciliationStore();
        mutationStore.Add(MutationRemoving(
            store.StoredEmailIdOf(11),
            uid: 11,
            isRelocation: false,
            localDisposition: authoredDisposition));
        await using var mailboxSession = CreateSessionHolding();
        var reconciler = CreateReconciler(
            store,
            RemotelyDeletedEmailDisposition.EraseLocalCopy,
            mutationStore: mutationStore);

        // Act
        var result = await reconciler.ReconcileAsync(
            mailboxSession,
            Account,
            InboxFolder,
            SelectedUidValidity,
            reconciledThroughModSeq: null,
            CancellationToken.None);

        // Assert
        Assert.Equal(1, result.OwnMutationCompletedEmailCount);
        Assert.Equal(0, result.RemotelyDeletedEmailCount);

        var attributed = Assert.Single(Assert.Single(store.AppliedOutcomes).RemovedByOwnMutation);
        Assert.Equal(authoredDisposition, attributed.LocalDisposition);

        // The account's own setting travels beside it and answers only for the disappearances nothing accounted for,
        // which this window has none of.
        Assert.Equal(RemotelyDeletedEmailDisposition.EraseLocalCopy, Assert.Single(store.AppliedOutcomes).Disposition);
        Assert.Empty(Assert.Single(store.AppliedOutcomes).Disappeared);
    }

    /// <summary>A relocation disposes of no local copy, so it reaches the store naming no disposition to apply.</summary>
    [Fact]
    public async Task ReconcileAsync_OccurrenceRemovedByARelocation_NamesNoLocalDisposition()
    {
        // Arrange
        var store = new FakeReconciliationStore(StoredOccurrences(11));
        var mutationStore = new InMemoryMailboxMutationReconciliationStore();
        mutationStore.Add(MutationRemoving(store.StoredEmailIdOf(11), uid: 11, isRelocation: true));
        await using var mailboxSession = CreateSessionHolding();
        var reconciler = CreateReconciler(
            store,
            RemotelyDeletedEmailDisposition.EraseLocalCopy,
            mutationStore: mutationStore);

        // Act
        await reconciler.ReconcileAsync(
            mailboxSession,
            Account,
            InboxFolder,
            SelectedUidValidity,
            reconciledThroughModSeq: null,
            CancellationToken.None);

        // Assert
        var attributed = Assert.Single(Assert.Single(store.AppliedOutcomes).RemovedByOwnMutation);
        Assert.Null(attributed.LocalDisposition);
    }

    /// <summary>
    /// A relocation into a folder nothing mirrors takes the message out of the mirrored mailbox for good, so the source
    /// disappearance carries the disposition the change was authored under exactly as a delete does.
    /// </summary>
    [Theory]
    [InlineData(AuthoredDeleteEmailDisposition.RetainLocalCopy)]
    [InlineData(AuthoredDeleteEmailDisposition.RetainTombstone)]
    [InlineData(AuthoredDeleteEmailDisposition.EraseLocalCopy)]
    public async Task ReconcileAsync_OccurrenceRemovedByARelocationNothingMirrors_CarriesTheDispositionItWasAuthoredUnder(
        AuthoredDeleteEmailDisposition authoredDisposition)
    {
        // Arrange
        var store = new FakeReconciliationStore(StoredOccurrences(11));
        var mutationStore = new InMemoryMailboxMutationReconciliationStore();
        mutationStore.Add(MutationRemoving(
            store.StoredEmailIdOf(11),
            uid: 11,
            isRelocation: true,
            relocationDisposition: authoredDisposition));
        await using var mailboxSession = CreateSessionHolding();
        var reconciler = CreateReconciler(
            store,
            RemotelyDeletedEmailDisposition.RetainTombstone,
            mutationStore: mutationStore);

        // Act
        var result = await reconciler.ReconcileAsync(
            mailboxSession,
            Account,
            InboxFolder,
            SelectedUidValidity,
            reconciledThroughModSeq: null,
            CancellationToken.None);

        // Assert
        Assert.Equal(1, result.OwnMutationCompletedEmailCount);

        var attributed = Assert.Single(Assert.Single(store.AppliedOutcomes).RemovedByOwnMutation);
        Assert.Equal(authoredDisposition, attributed.LocalDisposition);
    }

    /// <summary>One occurrence can carry several records, and the disappearance is credited to the one that has been outstanding longest.</summary>
    /// <remarks>
    /// The case is real rather than theoretical: a record matches at every stage past <c>Recorded</c>, abandoned
    /// included, so a completed relocation and a later mutation that gave up on the same source occurrence both
    /// qualify. Which one is credited changes nothing about the local row — the point is that the disappearance is
    /// attributed once, and to the record that describes the change that actually moved the message.
    /// </remarks>
    [Fact]
    public async Task ReconcileAsync_OccurrenceCarryingSeveralRecords_AttributesTheDisappearanceToTheOldestOnlyOnce()
    {
        // Arrange
        var store = new FakeReconciliationStore(StoredOccurrences(11));
        var mutationStore = new InMemoryMailboxMutationReconciliationStore();
        var storedEmailId = store.StoredEmailIdOf(11);
        var oldest = MutationRemoving(storedEmailId, uid: 11, isRelocation: true);
        var later = MutationRemoving(storedEmailId, uid: 11, isRelocation: false, RunInstant.AddMinutes(5))
            with
        {
            Stage = MailboxMutationStage.Abandoned,
        };
        mutationStore.Add(oldest);
        mutationStore.Add(later);
        await using var mailboxSession = CreateSessionHolding();
        var reconciler = CreateReconciler(
            store,
            RemotelyDeletedEmailDisposition.RetainTombstone,
            mutationStore: mutationStore);

        // Act
        var result = await reconciler.ReconcileAsync(
            mailboxSession,
            Account,
            InboxFolder,
            SelectedUidValidity,
            reconciledThroughModSeq: null,
            CancellationToken.None);

        // Assert
        Assert.Equal(1, result.OwnMutationCompletedEmailCount);
        Assert.Equal(0, result.RemotelyDeletedEmailCount);
        Assert.Equal(RunInstant, mutationStore.RecordOf(oldest.Id).SourceRemovalObservedAt);
        Assert.Null(mutationStore.RecordOf(later.Id).SourceRemovalObservedAt);
    }

    /// <summary>A disappearance seen before the destination folder is synchronized settles one half and leaves the other owing.</summary>
    [Fact]
    public async Task ReconcileAsync_RelocationSourceSeenBeforeItsPlacement_LeavesThePlacementStillAwaited()
    {
        // Arrange
        var store = new FakeReconciliationStore(StoredOccurrences(11));
        var mutationStore = new InMemoryMailboxMutationReconciliationStore();
        var record = MutationRemoving(store.StoredEmailIdOf(11), uid: 11, isRelocation: true);
        mutationStore.Add(record);
        await using var mailboxSession = CreateSessionHolding();
        var reconciler = CreateReconciler(
            store,
            RemotelyDeletedEmailDisposition.RetainTombstone,
            mutationStore: mutationStore);

        // Act
        await reconciler.ReconcileAsync(
            mailboxSession,
            Account,
            InboxFolder,
            SelectedUidValidity,
            reconciledThroughModSeq: null,
            CancellationToken.None);

        // Assert
        var observed = mutationStore.RecordOf(record.Id);
        Assert.Equal(RunInstant, observed.SourceRemovalObservedAt);
        Assert.Null(observed.PlacementObservedAt);
        Assert.False(observed.IsReconciled);
    }

    /// <summary>A message read on another client changes its flags, and the stored snapshot has to follow.</summary>
    [Fact]
    public async Task ReconcileAsync_ServerReportsChangedFlags_RefreshesTheStoredSnapshot()
    {
        // Arrange
        var store = new FakeReconciliationStore(StoredOccurrences(10));
        await using var mailboxSession = Substitute.For<IMailboxSession>();
        IReadOnlyList<RemoteEmailFlagObservation> observations =
        [
            new RemoteEmailFlagObservation(
                ImapUid.Create(10),
                new RemoteEmailFlagSnapshot(
                    RunInstant,
                    IsSeen: true,
                    IsAnswered: true,
                    IsFlagged: false,
                    IsDraft: false,
                    IsDeleted: true,
                    Keywords: RemoteEmailKeywords.None)),
        ];
        mailboxSession
            .ObserveWindowWithoutSettingSeenAsync(Arg.Any<IReadOnlyList<ImapUid>>(), Arg.Any<ulong?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(RemoteFolderWindowObservation.FromDescribedOccurrences(observations, folderHighestModSeq: null)));
        var reconciler = CreateReconciler(store, RemotelyDeletedEmailDisposition.RetainTombstone);

        // Act
        await reconciler.ReconcileAsync(
            mailboxSession,
            Account,
            InboxFolder,
            SelectedUidValidity,
            reconciledThroughModSeq: null,
            CancellationToken.None);

        // Assert
        var row = store.RowOf(10);
        Assert.True(row.Snapshot!.IsSeen);
        Assert.True(row.Snapshot.IsAnswered);

        // The server reporting \Deleted is a message still in the folder, not a disappearance, so the row keeps its
        // place in the mailbox and only the flag changes.
        Assert.True(row.Snapshot.IsDeleted);
        Assert.Null(row.RemoteExpungeObservedAt);
    }

    /// <summary>A rule that marks mail read must not re-evaluate everything it just marked, which is the cheapest mutation looping hardest.</summary>
    /// <remarks>
    /// The flag comes back as a changed modification sequence, indistinguishable in the protocol from a person reading
    /// the message in their own client. Only the record separates them, so the withheld change names the record that
    /// accounted for it and the stored snapshot still follows the server.
    /// </remarks>
    [Fact]
    public async Task ReconcileAsync_SeenFlagMailFathomSetItself_WithholdsTheChange()
    {
        // Arrange
        var store = new FakeReconciliationStore(ObservedOccurrence(10, isSeen: false));
        var mutationStore = new InMemoryMailboxMutationReconciliationStore();
        var record = MutationSettingSeen(store.StoredEmailIdOf(10), uid: 10, isSeen: true);
        mutationStore.Add(record);
        await using var mailboxSession = CreateSessionReportingSeenState(isSeen: true, 10);
        var reconciler = CreateReconciler(
            store,
            RemotelyDeletedEmailDisposition.RetainTombstone,
            mutationStore: mutationStore);

        // Act
        var result = await reconciler.ReconcileAsync(
            mailboxSession,
            Account,
            InboxFolder,
            SelectedUidValidity,
            reconciledThroughModSeq: null,
            CancellationToken.None);

        // Assert
        Assert.Equal(0, result.SeenStateChangedEmailCount);
        var suppressed = Assert.Single(result.SuppressedChanges);
        Assert.Equal(MailboxChangeKind.SeenStateChanged, suppressed.Kind);
        Assert.Equal(MailboxMutation.SetSeen, suppressed.Mutation);
        Assert.Equal(store.StoredEmailIdOf(10), suppressed.StoredEmailId);
        Assert.Equal(record.Id, suppressed.MutationRecordId);

        // The stored snapshot still follows the server, because what was withheld is the trigger and never the reading.
        Assert.True(store.RowOf(10).Snapshot!.IsSeen);
    }

    /// <summary>A star standing where MailFathom's own store put it is that store completing, not the owner starring the message.</summary>
    [Fact]
    public async Task ReconcileAsync_FlaggedStateMailFathomSetItself_WithholdsTheChange()
    {
        // Arrange
        var store = new FakeReconciliationStore(ObservedOccurrence(10, isSeen: true));
        var mutationStore = new InMemoryMailboxMutationReconciliationStore();
        var record = MutationSettingFlagged(store.StoredEmailIdOf(10), uid: 10, isFlagged: true);
        mutationStore.Add(record);
        await using var mailboxSession = CreateSessionReportingFlags(
            isSeen: true,
            isFlagged: true,
            RemoteEmailKeywords.None,
            10);
        var reconciler = CreateReconciler(
            store,
            RemotelyDeletedEmailDisposition.RetainTombstone,
            mutationStore: mutationStore);

        // Act
        var result = await reconciler.ReconcileAsync(
            mailboxSession,
            Account,
            InboxFolder,
            SelectedUidValidity,
            reconciledThroughModSeq: null,
            CancellationToken.None);

        // Assert
        Assert.Equal(0, result.FlaggedStateChangedEmailCount);
        var suppressed = Assert.Single(result.SuppressedChanges);
        Assert.Equal(MailboxChangeKind.FlaggedStateChanged, suppressed.Kind);
        Assert.Equal(MailboxMutation.SetFlagged, suppressed.Mutation);
        Assert.Equal(record.Id, suppressed.MutationRecordId);

        // The stored snapshot still follows the server, because what was withheld is the trigger and never the reading.
        Assert.True(store.RowOf(10).Snapshot!.IsFlagged);
    }

    /// <summary>Starring mail by hand is the mailbox owner's act, and it stays a change to react to.</summary>
    [Fact]
    public async Task ReconcileAsync_OwnerStarredMailThemselves_RaisesTheChange()
    {
        // Arrange
        var store = new FakeReconciliationStore(ObservedOccurrence(10, isSeen: true));
        await using var mailboxSession = CreateSessionReportingFlags(
            isSeen: true,
            isFlagged: true,
            RemoteEmailKeywords.None,
            10);
        var reconciler = CreateReconciler(store, RemotelyDeletedEmailDisposition.RetainTombstone);

        // Act
        var result = await reconciler.ReconcileAsync(
            mailboxSession,
            Account,
            InboxFolder,
            SelectedUidValidity,
            reconciledThroughModSeq: null,
            CancellationToken.None);

        // Assert
        Assert.Equal(1, result.FlaggedStateChangedEmailCount);
        Assert.Equal(0, result.SeenStateChangedEmailCount);
        Assert.Empty(result.SuppressedChanges);
    }

    /// <summary>Keywords standing as MailFathom's own addition asked for are that addition completing.</summary>
    /// <remarks>
    /// The occurrence already carried a label of the owner's, so the set the addition would have left is the earlier
    /// reading plus <c>$Todo</c> rather than whatever the server now reports. That is what makes this test able to fail:
    /// an attribution computing the expected set from the observed keywords would suppress this whatever the record
    /// asked for.
    /// </remarks>
    [Fact]
    public async Task ReconcileAsync_KeywordsMailFathomWroteItself_WithholdsTheChange()
    {
        // Arrange
        var store = new FakeReconciliationStore(ObservedOccurrence(10, isSeen: true, isFlagged: false, "$Invoice"));
        var mutationStore = new InMemoryMailboxMutationReconciliationStore();
        var record = MutationAddingKeywords(store.StoredEmailIdOf(10), uid: 10, "$Todo");
        mutationStore.Add(record);
        await using var mailboxSession = CreateSessionReportingFlags(
            isSeen: true,
            isFlagged: false,
            RemoteEmailKeywords.Create(["$Invoice", "$Todo"]),
            10);
        var reconciler = CreateReconciler(
            store,
            RemotelyDeletedEmailDisposition.RetainTombstone,
            mutationStore: mutationStore);

        // Act
        var result = await reconciler.ReconcileAsync(
            mailboxSession,
            Account,
            InboxFolder,
            SelectedUidValidity,
            reconciledThroughModSeq: null,
            CancellationToken.None);

        // Assert
        Assert.Equal(0, result.KeywordsChangedEmailCount);
        var suppressed = Assert.Single(result.SuppressedChanges);
        Assert.Equal(MailboxChangeKind.KeywordsChanged, suppressed.Kind);
        Assert.Equal(MailboxMutation.AddKeywords, suppressed.Mutation);
        Assert.Equal(record.Id, suppressed.MutationRecordId);

        // The stored keywords still follow the server, which is what keeps the column a mirror of the last observation.
        Assert.Equal(RemoteEmailKeywords.Create(["$Invoice", "$Todo"]), store.RowOf(10).Snapshot!.Keywords);
    }

    /// <summary>An addition accounts for the set it would have left and never for a set the owner also took a label off.</summary>
    /// <remarks>
    /// This is the direction the attribution has to fail in. The record asked for <c>$Todo</c> and the owner dropped
    /// <c>$Invoice</c> in the same interval, so what the server now reports is nobody's single act; crediting the record
    /// with it would withhold the owner's removal from rule evaluation as MailFathom's own doing, and nothing later
    /// would report that it had.
    /// </remarks>
    [Fact]
    public async Task ReconcileAsync_KeywordsMailFathomAddedBesideALabelTheOwnerRemoved_RaisesTheChange()
    {
        // Arrange
        var store = new FakeReconciliationStore(ObservedOccurrence(10, isSeen: true, isFlagged: false, "$Invoice"));
        var mutationStore = new InMemoryMailboxMutationReconciliationStore();
        mutationStore.Add(MutationAddingKeywords(store.StoredEmailIdOf(10), uid: 10, "$Todo"));
        await using var mailboxSession = CreateSessionReportingFlags(
            isSeen: true,
            isFlagged: false,
            RemoteEmailKeywords.Create(["$Todo"]),
            10);
        var reconciler = CreateReconciler(
            store,
            RemotelyDeletedEmailDisposition.RetainTombstone,
            mutationStore: mutationStore);

        // Act
        var result = await reconciler.ReconcileAsync(
            mailboxSession,
            Account,
            InboxFolder,
            SelectedUidValidity,
            reconciledThroughModSeq: null,
            CancellationToken.None);

        // Assert
        Assert.Equal(1, result.KeywordsChangedEmailCount);
        Assert.Empty(result.SuppressedChanges);
    }

    /// <summary>Labelling mail in a client is the mailbox owner's act, and it stays a change to react to.</summary>
    [Fact]
    public async Task ReconcileAsync_OwnerLabelledMailThemselves_RaisesTheChange()
    {
        // Arrange
        var store = new FakeReconciliationStore(ObservedOccurrence(10, isSeen: true, isFlagged: false, "$Invoice"));
        await using var mailboxSession = CreateSessionReportingFlags(
            isSeen: true,
            isFlagged: false,
            RemoteEmailKeywords.Create(["$Invoice", "$Waiting"]),
            10);
        var reconciler = CreateReconciler(store, RemotelyDeletedEmailDisposition.RetainTombstone);

        // Act
        var result = await reconciler.ReconcileAsync(
            mailboxSession,
            Account,
            InboxFolder,
            SelectedUidValidity,
            reconciledThroughModSeq: null,
            CancellationToken.None);

        // Assert
        Assert.Equal(1, result.KeywordsChangedEmailCount);
        Assert.Empty(result.SuppressedChanges);
    }

    /// <summary>One FLAGS response carries every value, so an occurrence whose star and labels both moved is one read rather than three.</summary>
    [Fact]
    public async Task ReconcileAsync_SeveralValuesMovedOnOneOccurrence_AsksTheRecordOnce()
    {
        // Arrange
        var store = new FakeReconciliationStore(ObservedOccurrence(10, isSeen: false));
        var mutationStore = new InMemoryMailboxMutationReconciliationStore();
        await using var mailboxSession = CreateSessionReportingFlags(
            isSeen: true,
            isFlagged: true,
            RemoteEmailKeywords.Create(["$Waiting"]),
            10);
        var reconciler = CreateReconciler(
            store,
            RemotelyDeletedEmailDisposition.RetainTombstone,
            mutationStore: mutationStore);
        var readingTheWindowStartedFrom = store.RowOf(10).ObservedAt;

        // Act
        var result = await reconciler.ReconcileAsync(
            mailboxSession,
            Account,
            InboxFolder,
            SelectedUidValidity,
            reconciledThroughModSeq: null,
            CancellationToken.None);

        // Assert
        Assert.Equal(1, mutationStore.FlagChangeReadCount);
        Assert.Equal(1, result.SeenStateChangedEmailCount);
        Assert.Equal(1, result.FlaggedStateChangedEmailCount);
        Assert.Equal(1, result.KeywordsChangedEmailCount);

        // The read is narrowed to the stores that could still account for a value, which is the reading the window
        // started from. A bound later than that would withhold the record that explains a change.
        Assert.Equal(readingTheWindowStartedFrom, mutationStore.LastFlagChangeReadIssuedAfter);
    }

    /// <summary>Two occurrences read at different times narrow the record search from the earlier reading, not the later one.</summary>
    /// <remarks>
    /// Every record that could account for a value was staged after the reading the value moved from, so a bound taken
    /// from the latest reading in the window would withhold the records explaining every occurrence read before it —
    /// and those changes would then be attributed to the mailbox owner and reacted to as their act. One occurrence
    /// cannot say which end of the window the bound came from, so this is the case that does.
    /// </remarks>
    [Fact]
    public async Task ReconcileAsync_ValuesMovedOnOccurrencesReadAtDifferentTimes_NarrowsFromTheEarlierReading()
    {
        // Arrange
        var readThreeHoursAgo = RunInstant.AddHours(-3);
        var readOneHourAgo = RunInstant.AddHours(-1);
        var store = new FakeReconciliationStore(
        [
            new StoredOccurrence(
                StoredEmailId.Create(Guid.CreateVersion7(RunInstant.AddSeconds(10))),
                10,
                PreviouslyObservedSeenState: false,
                PreviouslyObservedAt: readThreeHoursAgo),
            new StoredOccurrence(
                StoredEmailId.Create(Guid.CreateVersion7(RunInstant.AddSeconds(11))),
                11,
                PreviouslyObservedSeenState: false,
                PreviouslyObservedAt: readOneHourAgo),
        ]);
        var mutationStore = new InMemoryMailboxMutationReconciliationStore();
        await using var mailboxSession = CreateSessionReportingFlags(
            isSeen: true,
            isFlagged: false,
            RemoteEmailKeywords.None,
            10,
            11);
        var reconciler = CreateReconciler(
            store,
            RemotelyDeletedEmailDisposition.RetainTombstone,
            mutationStore: mutationStore);

        // Act
        var result = await reconciler.ReconcileAsync(
            mailboxSession,
            Account,
            InboxFolder,
            SelectedUidValidity,
            reconciledThroughModSeq: null,
            CancellationToken.None);

        // Assert
        Assert.Equal(2, result.SeenStateChangedEmailCount);
        Assert.Equal(1, mutationStore.FlagChangeReadCount);
        Assert.Equal(readThreeHoursAgo, mutationStore.LastFlagChangeReadIssuedAfter);
    }

    /// <summary>An occurrence carrying a pile of stores cannot crowd out the record that explains the message beside it.</summary>
    /// <remarks>
    /// The attribution read is capped, and where that cap is spent decides what a truncation costs. Spent across the
    /// window, a message an agent marked and unmarked repeatedly takes every slot and the single record explaining the
    /// message next to it is dropped — so that message's <c>\Seen</c> flag is credited to the mailbox owner and the
    /// rule that set it re-fires on the mail it just acted on. Spent within each occurrence, neither can reach the
    /// other's room. The pile is deliberately larger than the whole window's worth, and the record it would have
    /// displaced is the oldest of the lot, which is the one a newest-first truncation drops first.
    /// </remarks>
    [Fact]
    public async Task ReconcileAsync_OneOccurrenceCarryingAPileOfStoresBesideAnotherCarryingOne_WithholdsBothChanges()
    {
        // Arrange
        var readAnHourAgo = RunInstant.AddHours(-1);
        var store = new FakeReconciliationStore(
        [
            new StoredOccurrence(
                StoredEmailId.Create(Guid.CreateVersion7(RunInstant.AddSeconds(10))),
                10,
                PreviouslyObservedSeenState: false,
                PreviouslyObservedAt: readAnHourAgo),
            new StoredOccurrence(
                StoredEmailId.Create(Guid.CreateVersion7(RunInstant.AddSeconds(11))),
                11,
                PreviouslyObservedSeenState: false,
                PreviouslyObservedAt: readAnHourAgo),
        ]);
        var mutationStore = new InMemoryMailboxMutationReconciliationStore();
        var explainsTheSecond = MutationSettingSeen(
            store.StoredEmailIdOf(11),
            uid: 11,
            isSeen: true,
            stagedAt: RunInstant.AddMinutes(1));

        mutationStore.Add(explainsTheSecond);

        foreach (var minute in Enumerable.Range(2, 20))
        {
            mutationStore.Add(MutationSettingSeen(
                store.StoredEmailIdOf(10),
                uid: 10,
                isSeen: true,
                stagedAt: RunInstant.AddMinutes(minute)));
        }

        await using var mailboxSession = CreateSessionReportingSeenState(isSeen: true, 10, 11);
        var reconciler = CreateReconciler(
            store,
            RemotelyDeletedEmailDisposition.RetainTombstone,
            mutationStore: mutationStore);

        // Act
        var result = await reconciler.ReconcileAsync(
            mailboxSession,
            Account,
            InboxFolder,
            SelectedUidValidity,
            reconciledThroughModSeq: null,
            CancellationToken.None);

        // Assert
        Assert.Equal(0, result.SeenStateChangedEmailCount);
        Assert.Equal(2, result.SuppressedChanges.Count);
        Assert.Contains(
            result.SuppressedChanges,
            suppressed => suppressed.MutationRecordId == explainsTheSecond.Id
                && suppressed.StoredEmailId == store.StoredEmailIdOf(11));
    }

    /// <summary>A pile of stars on one message cannot crowd out that same message's only <c>\Seen</c> store.</summary>
    /// <remarks>
    /// One occurrence's values compete for the budget unless the ranking says otherwise, and the two values arrive in
    /// one <c>FLAGS</c> response, so the failure is invisible in the protocol: the star is explained, the read flag is
    /// not, and the rule that marked the message read re-fires on the mail it just acted on. The stars are staged after
    /// the <c>\Seen</c> store, which is what puts the store last in a newest-first truncation.
    /// </remarks>
    [Fact]
    public async Task ReconcileAsync_OneOccurrenceWhoseStarsOutnumberItsOnlySeenStore_WithholdsBothChanges()
    {
        // Arrange
        var store = new FakeReconciliationStore(ObservedOccurrence(10, isSeen: false));
        var mutationStore = new InMemoryMailboxMutationReconciliationStore();
        var explainsTheReadFlag = MutationSettingSeen(
            store.StoredEmailIdOf(10),
            uid: 10,
            isSeen: true,
            stagedAt: RunInstant.AddMinutes(1));

        mutationStore.Add(explainsTheReadFlag);

        foreach (var minute in Enumerable.Range(2, 20))
        {
            mutationStore.Add(MutationSettingFlagged(
                store.StoredEmailIdOf(10),
                uid: 10,
                isFlagged: true,
                stagedAt: RunInstant.AddMinutes(minute)));
        }

        await using var mailboxSession = CreateSessionReportingFlags(
            isSeen: true,
            isFlagged: true,
            RemoteEmailKeywords.None,
            10);
        var reconciler = CreateReconciler(
            store,
            RemotelyDeletedEmailDisposition.RetainTombstone,
            mutationStore: mutationStore);

        // Act
        var result = await reconciler.ReconcileAsync(
            mailboxSession,
            Account,
            InboxFolder,
            SelectedUidValidity,
            reconciledThroughModSeq: null,
            CancellationToken.None);

        // Assert
        Assert.Equal(0, result.SeenStateChangedEmailCount);
        Assert.Equal(0, result.FlaggedStateChangedEmailCount);
        Assert.Contains(
            result.SuppressedChanges,
            suppressed => suppressed.MutationRecordId == explainsTheReadFlag.Id
                && suppressed.Kind == MailboxChangeKind.SeenStateChanged);
    }

    /// <summary>Marking mail read by hand is the mailbox owner's act, and it stays a change to react to.</summary>
    [Fact]
    public async Task ReconcileAsync_OwnerMarkedMailReadThemselves_RaisesTheChange()
    {
        // Arrange
        var store = new FakeReconciliationStore(ObservedOccurrence(10, isSeen: false));
        var mutationStore = new InMemoryMailboxMutationReconciliationStore();
        await using var mailboxSession = CreateSessionReportingSeenState(isSeen: true, 10);
        var reconciler = CreateReconciler(
            store,
            RemotelyDeletedEmailDisposition.RetainTombstone,
            mutationStore: mutationStore);

        // Act
        var result = await reconciler.ReconcileAsync(
            mailboxSession,
            Account,
            InboxFolder,
            SelectedUidValidity,
            reconciledThroughModSeq: null,
            CancellationToken.None);

        // Assert
        Assert.Equal(1, result.SeenStateChangedEmailCount);
        Assert.Empty(result.SuppressedChanges);
    }

    /// <summary>The suppression is scoped to the one change the record describes, so the owner setting that flag again later is their act.</summary>
    /// <remarks>
    /// This is the case a record that answered forever would get wrong, and it is silent when it happens: a rule
    /// conditioned on read mail would simply never fire again for a message MailFathom had once marked read.
    /// </remarks>
    [Fact]
    public async Task ReconcileAsync_OwnerRestoresTheFlagMailFathomSetEarlier_RaisesItAsTheirOwnChange()
    {
        // Arrange
        var store = new FakeReconciliationStore(ObservedOccurrence(10, isSeen: false));
        var mutationStore = new InMemoryMailboxMutationReconciliationStore();
        mutationStore.Add(MutationSettingSeen(store.StoredEmailIdOf(10), uid: 10, isSeen: true));
        var reconciler = CreateReconciler(
            store,
            RemotelyDeletedEmailDisposition.RetainTombstone,
            mutationStore: mutationStore);

        // Act
        await using var afterOwnStore = CreateSessionReportingSeenState(isSeen: true, 10);
        var ownChange = await reconciler.ReconcileAsync(
            mailboxSession: afterOwnStore,
            Account,
            InboxFolder,
            SelectedUidValidity,
            reconciledThroughModSeq: null,
            CancellationToken.None);

        await using var afterOwnerCleared = CreateSessionReportingSeenState(isSeen: false, 10);
        var clearedByOwner = await reconciler.ReconcileAsync(
            mailboxSession: afterOwnerCleared,
            Account,
            InboxFolder,
            SelectedUidValidity,
            reconciledThroughModSeq: null,
            CancellationToken.None);

        await using var afterOwnerSetItAgain = CreateSessionReportingSeenState(isSeen: true, 10);
        var setAgainByOwner = await reconciler.ReconcileAsync(
            mailboxSession: afterOwnerSetItAgain,
            Account,
            InboxFolder,
            SelectedUidValidity,
            reconciledThroughModSeq: null,
            CancellationToken.None);

        // Assert
        Assert.Single(ownChange.SuppressedChanges);
        Assert.Equal(0, ownChange.SeenStateChangedEmailCount);
        Assert.Empty(clearedByOwner.SuppressedChanges);
        Assert.Equal(1, clearedByOwner.SeenStateChangedEmailCount);
        Assert.Empty(setAgainByOwner.SuppressedChanges);
        Assert.Equal(1, setAgainByOwner.SeenStateChangedEmailCount);
    }

    /// <summary>A record stops answering once the occurrence has been read, whether or not that reading found the flag where it put it.</summary>
    /// <remarks>
    /// This is the case that decides where the expiry is recorded. The owner reverts the flag before any window sees it,
    /// so the first window finds nothing changed and there is no matching change to mark a record spent by — and if the
    /// expiry lived on the record, the owner marking the message read weeks later would be silently withheld. Anchoring
    /// it to the occurrence's own last observation is what makes that window count.
    /// </remarks>
    [Fact]
    public async Task ReconcileAsync_OwnerRevertedTheFlagBeforeAnyWindowSawIt_StillRaisesTheirLaterChange()
    {
        // Arrange
        var store = new FakeReconciliationStore(ObservedOccurrence(10, isSeen: false));
        var mutationStore = new InMemoryMailboxMutationReconciliationStore();
        mutationStore.Add(MutationSettingSeen(store.StoredEmailIdOf(10), uid: 10, isSeen: true));
        var reconciler = CreateReconciler(
            store,
            RemotelyDeletedEmailDisposition.RetainTombstone,
            mutationStore: mutationStore);

        // Act
        await using var afterTheOwnerReverted = CreateSessionReportingSeenState(isSeen: false, 10);
        var sawNothingChanged = await reconciler.ReconcileAsync(
            mailboxSession: afterTheOwnerReverted,
            Account,
            InboxFolder,
            SelectedUidValidity,
            reconciledThroughModSeq: null,
            CancellationToken.None);

        await using var afterTheOwnerMarkedItRead = CreateSessionReportingSeenState(isSeen: true, 10);
        var ownerMarkedItRead = await reconciler.ReconcileAsync(
            mailboxSession: afterTheOwnerMarkedItRead,
            Account,
            InboxFolder,
            SelectedUidValidity,
            reconciledThroughModSeq: null,
            CancellationToken.None);

        // Assert
        Assert.Equal(0, sawNothingChanged.SeenStateChangedEmailCount);
        Assert.Empty(sawNothingChanged.SuppressedChanges);
        Assert.Equal(1, ownerMarkedItRead.SeenStateChangedEmailCount);
        Assert.Empty(ownerMarkedItRead.SuppressedChanges);
    }

    /// <summary>A record whose <c>STORE</c> never went out accounts for nothing, because the flag standing there is somebody else's doing.</summary>
    [Fact]
    public async Task ReconcileAsync_SeenStoreThatNeverReachedTheServer_RaisesTheChange()
    {
        // Arrange
        var store = new FakeReconciliationStore(ObservedOccurrence(10, isSeen: false));
        var mutationStore = new InMemoryMailboxMutationReconciliationStore();
        mutationStore.Add(MutationSettingSeen(
            store.StoredEmailIdOf(10),
            uid: 10,
            isSeen: true,
            MailboxMutationStage.Recorded));
        await using var mailboxSession = CreateSessionReportingSeenState(isSeen: true, 10);
        var reconciler = CreateReconciler(
            store,
            RemotelyDeletedEmailDisposition.RetainTombstone,
            mutationStore: mutationStore);

        // Act
        var result = await reconciler.ReconcileAsync(
            mailboxSession,
            Account,
            InboxFolder,
            SelectedUidValidity,
            reconciledThroughModSeq: null,
            CancellationToken.None);

        // Assert
        Assert.Equal(1, result.SeenStateChangedEmailCount);
        Assert.Empty(result.SuppressedChanges);
    }

    /// <summary>A record answers for the direction it asked for and never for the opposite one.</summary>
    [Fact]
    public async Task ReconcileAsync_FlagMovedOppositeToWhatWasAsked_RaisesTheChange()
    {
        // Arrange
        var store = new FakeReconciliationStore(ObservedOccurrence(10, isSeen: false));
        var mutationStore = new InMemoryMailboxMutationReconciliationStore();
        mutationStore.Add(MutationSettingSeen(store.StoredEmailIdOf(10), uid: 10, isSeen: false));
        await using var mailboxSession = CreateSessionReportingSeenState(isSeen: true, 10);
        var reconciler = CreateReconciler(
            store,
            RemotelyDeletedEmailDisposition.RetainTombstone,
            mutationStore: mutationStore);

        // Act
        var result = await reconciler.ReconcileAsync(
            mailboxSession,
            Account,
            InboxFolder,
            SelectedUidValidity,
            reconciledThroughModSeq: null,
            CancellationToken.None);

        // Assert
        Assert.Equal(1, result.SeenStateChangedEmailCount);
        Assert.Empty(result.SuppressedChanges);
    }

    /// <summary>The first reading of an occurrence's flags is an observation rather than a change, and a window where nothing moved asks nothing.</summary>
    /// <remarks>
    /// Both halves guard the same mistake from opposite sides. Treating a first reading as a change would raise a
    /// trigger for every message a backfill stores, and asking the database about a window in which nothing moved would
    /// put a query on every run of every folder for an answer that is always empty.
    /// </remarks>
    [Fact]
    public async Task ReconcileAsync_NoFlagHasMoved_ReportsNoChangeAndAsksNothing()
    {
        // Arrange
        var store = new FakeReconciliationStore(StoredOccurrences(10, 11));
        var mutationStore = new InMemoryMailboxMutationReconciliationStore();
        await using var mailboxSession = CreateSessionReportingSeenState(isSeen: true, 10, 11);
        var reconciler = CreateReconciler(
            store,
            RemotelyDeletedEmailDisposition.RetainTombstone,
            mutationStore: mutationStore);

        // Act
        var result = await reconciler.ReconcileAsync(
            mailboxSession,
            Account,
            InboxFolder,
            SelectedUidValidity,
            reconciledThroughModSeq: null,
            CancellationToken.None);

        // Assert
        Assert.Equal(0, result.SeenStateChangedEmailCount);
        Assert.Empty(result.SuppressedChanges);
        Assert.Equal(0, mutationStore.FlagChangeReadCount);
        Assert.True(store.RowOf(10).Snapshot!.IsSeen);
    }

    /// <summary>A large folder is reconciled across runs rather than scanned in one, and the run says so.</summary>
    [Fact]
    public async Task ReconcileAsync_MoreEmailsThanTheWindowHolds_BoundsTheWindowAndReportsRemainingWork()
    {
        // Arrange
        var storedUids = Enumerable.Range(10, 10).Select(uid => (uint)uid).ToArray();
        var store = new FakeReconciliationStore(StoredOccurrences(storedUids));
        await using var mailboxSession = CreateSessionHolding(storedUids);
        var reconciler = CreateReconciler(store, RemotelyDeletedEmailDisposition.RetainTombstone, maxReconciledEmailsPerRun: 4);

        // Act
        var result = await reconciler.ReconcileAsync(
            mailboxSession,
            Account,
            InboxFolder,
            SelectedUidValidity,
            reconciledThroughModSeq: null,
            CancellationToken.None);

        // Assert
        Assert.Equal(4, result.ObservedEmailCount);
        Assert.True(result.EmailsRemain);
        Assert.Equal([10U, 11U, 12U, 13U], store.AskedAboutUids);
    }

    /// <summary>Observing an email is what moves it to the back of the queue, which is how the window advances with no cursor.</summary>
    [Fact]
    public async Task ReconcileAsync_RunAgainAfterAWindow_ReachesUnobservedMailAndRevisitsTheOldestObservation()
    {
        // Arrange
        var storedUids = Enumerable.Range(10, 6).Select(uid => (uint)uid).ToArray();
        var store = new FakeReconciliationStore(StoredOccurrences(storedUids));
        await using var mailboxSession = CreateSessionHolding(storedUids);
        var timeProvider = new FakeTimeProvider(RunInstant);
        var reconciler = CreateReconciler(
            store,
            RemotelyDeletedEmailDisposition.RetainTombstone,
            maxReconciledEmailsPerRun: 3,
            timeProvider: timeProvider);
        await reconciler.ReconcileAsync(mailboxSession, Account, InboxFolder, SelectedUidValidity, reconciledThroughModSeq: null, CancellationToken.None);
        store.AskedAboutUids.Clear();
        timeProvider.Advance(TimeSpan.FromMinutes(5));

        // Act
        await reconciler.ReconcileAsync(mailboxSession, Account, InboxFolder, SelectedUidValidity, reconciledThroughModSeq: null, CancellationToken.None);

        // Assert
        // The window moves on to mail nobody has asked about, and spends its reserved part on the email observed
        // longest ago rather than filling itself with unobserved mail alone.
        Assert.Equal([13U, 14U, 10U], store.AskedAboutUids);
    }

    /// <summary>
    /// A run's forward pass can store more new mail than one window holds, so a window taken in observation order alone
    /// would go to newly arrived mail forever and never notice a deletion among the mail stored earlier.
    /// </summary>
    [Fact]
    public async Task ReconcileAsync_MoreNewMailThanOneWindowHolds_StillRevisitsPreviouslyObservedEmails()
    {
        // Arrange
        var storedUids = Enumerable.Range(10, 4).Select(uid => (uint)uid).ToArray();
        var store = new FakeReconciliationStore(StoredOccurrences(storedUids));
        var timeProvider = new FakeTimeProvider(RunInstant);
        var reconciler = CreateReconciler(
            store,
            RemotelyDeletedEmailDisposition.RetainTombstone,
            maxReconciledEmailsPerRun: 2,
            timeProvider: timeProvider);
        await using (var firstSession = CreateSessionHolding(storedUids))
        {
            await reconciler.ReconcileAsync(firstSession, Account, InboxFolder, SelectedUidValidity, reconciledThroughModSeq: null, CancellationToken.None);
        }

        // The mail observed by the first run then disappears from the server, while the forward pass keeps the window
        // full of occurrences nobody has asked about.
        store.AskedAboutUids.Clear();
        timeProvider.Advance(TimeSpan.FromMinutes(5));
        await using var mailboxSession = CreateSessionHolding(12, 13);

        // Act
        await reconciler.ReconcileAsync(mailboxSession, Account, InboxFolder, SelectedUidValidity, reconciledThroughModSeq: null, CancellationToken.None);

        // Assert
        Assert.Contains(10U, store.AskedAboutUids);
        Assert.Equal([10U], store.TombstonedUids);
    }

    /// <summary>A renumbered folder must cost nothing locally: every stored occurrence names a UID space the server abandoned.</summary>
    [Fact]
    public async Task ReconcileAsync_FolderReportsADifferentUidValidity_DeletesNothingAndReachesNoServer()
    {
        // Arrange
        var store = new FakeReconciliationStore(StoredOccurrences(10, 11, 12));
        await using var mailboxSession = CreateSessionHolding();
        var reconciler = CreateReconciler(store, RemotelyDeletedEmailDisposition.EraseLocalCopy);

        // Act
        var result = await reconciler.ReconcileAsync(
            mailboxSession,
            Account,
            InboxFolder,
            ImapUidValidity.Create(8),
            reconciledThroughModSeq: null,
            CancellationToken.None);

        // Assert
        Assert.Equal(0, result.RemotelyDeletedEmailCount);
        Assert.Empty(store.RemovedUids);
        Assert.Empty(store.TombstonedUids);
        await mailboxSession.DidNotReceive().ObserveWindowWithoutSettingSeenAsync(
            Arg.Any<IReadOnlyList<ImapUid>>(),
            Arg.Any<ulong?>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Re-running over a folder whose deletions are already recorded commits the same state, not a second one.</summary>
    [Fact]
    public async Task ReconcileAsync_RunTwiceOverTheSameDisappearance_KeepsTheFirstObservationTimestamp()
    {
        // Arrange
        var store = new FakeReconciliationStore(StoredOccurrences(10, 11));
        await using var mailboxSession = CreateSessionHolding(10);
        var timeProvider = new FakeTimeProvider(RunInstant);
        var reconciler = CreateReconciler(
            store,
            RemotelyDeletedEmailDisposition.RetainTombstone,
            timeProvider: timeProvider);
        await reconciler.ReconcileAsync(mailboxSession, Account, InboxFolder, SelectedUidValidity, reconciledThroughModSeq: null, CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromHours(1));

        // Act
        var result = await reconciler.ReconcileAsync(
            mailboxSession,
            Account,
            InboxFolder,
            SelectedUidValidity,
            reconciledThroughModSeq: null,
            CancellationToken.None);

        // Assert
        Assert.Equal(0, result.RemotelyDeletedEmailCount);
        Assert.Equal([11U], store.TombstonedUids);
        Assert.Equal(RunInstant, store.RowOf(11).RemoteExpungeObservedAt);
    }

    /// <summary>
    /// The pass inspects mail that is already stored, which is exactly where a careless fetch would mark a whole mailbox
    /// as read. The absence is meaningful because the same substitute would report a content fetch if one were issued.
    /// </summary>
    [Fact]
    public async Task ReconcileAsync_WindowOfStoredEmails_ReadsFlagsWithoutFetchingAnyContent()
    {
        // Arrange
        var store = new FakeReconciliationStore(StoredOccurrences(10, 11));
        await using var mailboxSession = CreateSessionHolding(10, 11);
        var reconciler = CreateReconciler(store, RemotelyDeletedEmailDisposition.RetainTombstone);

        // Act
        await reconciler.ReconcileAsync(
            mailboxSession,
            Account,
            InboxFolder,
            SelectedUidValidity,
            reconciledThroughModSeq: null,
            CancellationToken.None);

        // Assert
        await mailboxSession.Received(1).ObserveWindowWithoutSettingSeenAsync(
            Arg.Any<IReadOnlyList<ImapUid>>(),
            Arg.Any<ulong?>(),
            Arg.Any<CancellationToken>());
        await mailboxSession.DidNotReceive().FetchEmailContentWithoutSettingSeenAsync(
            Arg.Any<EmailOccurrenceId>(),
            Arg.Any<long>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A folder with nothing stored costs no command, so a first run over an empty mailbox is free.</summary>
    [Fact]
    public async Task ReconcileAsync_FolderHoldsNothingLocally_ReachesNoServerAndReportsNoWork()
    {
        // Arrange
        var store = new FakeReconciliationStore(StoredOccurrences());
        await using var mailboxSession = CreateSessionHolding();
        var reconciler = CreateReconciler(store, RemotelyDeletedEmailDisposition.RetainTombstone);

        // Act
        var result = await reconciler.ReconcileAsync(
            mailboxSession,
            Account,
            InboxFolder,
            SelectedUidValidity,
            reconciledThroughModSeq: null,
            CancellationToken.None);

        // Assert
        Assert.Equal(0, result.ObservedEmailCount);
        Assert.False(result.EmailsRemain);
        await mailboxSession.DidNotReceive().ObserveWindowWithoutSettingSeenAsync(
            Arg.Any<IReadOnlyList<ImapUid>>(),
            Arg.Any<ulong?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReconcileAsync_CallerCancels_StopsWithoutCommittingTheWindow()
    {
        // Arrange
        var store = new FakeReconciliationStore(StoredOccurrences(10, 11));
        await using var mailboxSession = CreateSessionHolding(10);
        var reconciler = CreateReconciler(store, RemotelyDeletedEmailDisposition.RetainTombstone);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act and assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reconciler.ReconcileAsync(
            mailboxSession,
            Account,
            InboxFolder,
            SelectedUidValidity,
            reconciledThroughModSeq: null,
            cancellation.Token));
        Assert.Empty(store.TombstonedUids);
    }

    /// <summary>
    /// A window replayed after a commit conflict carries an answer the server gave before it, so it must not undo what
    /// the writer that won has since recorded — least of all by deleting an email that writer proved still exists.
    /// </summary>
    [Fact]
    public async Task ReconcileAsync_AnotherWriterRecordedANewerObservation_LeavesItAloneRatherThanDeletingTheEmail()
    {
        // Arrange
        var store = new FakeReconciliationStore(StoredOccurrences(10, 11));
        await using var mailboxSession = CreateSessionHolding(10);
        var reconciler = CreateReconciler(store, RemotelyDeletedEmailDisposition.EraseLocalCopy);
        store.RowOf(11).ObservedAt = RunInstant.AddHours(1);

        // Act
        await reconciler.ReconcileAsync(
            mailboxSession,
            Account,
            InboxFolder,
            SelectedUidValidity,
            reconciledThroughModSeq: null,
            CancellationToken.None);

        // Assert
        Assert.Empty(store.RemovedUids);
        Assert.Empty(store.TombstonedUids);
    }


    /// <summary>
    /// A server that says only what changed leaves the rest of the window confirmed rather than described, and both
    /// count as observed: the confirmed ones keep their stored flags and still leave the queue, which is what lets the
    /// next window reach further into the folder.
    /// </summary>
    [Fact]
    public async Task ReconcileAsync_ServerConfirmsOccurrencesWithoutDescribingThem_CountsThemObservedAndMovesThemDownTheQueue()
    {
        // Arrange
        var store = new FakeReconciliationStore(StoredOccurrences(10, 11, 12));
        await using var mailboxSession = CreateSessionReporting(
            describedUids: [10],
            confirmedUids: [11]);
        var reconciler = CreateReconciler(store, RemotelyDeletedEmailDisposition.RetainTombstone);

        // Act
        var result = await reconciler.ReconcileAsync(
            mailboxSession,
            Account,
            InboxFolder,
            SelectedUidValidity,
            reconciledThroughModSeq: 40UL,
            CancellationToken.None);

        // Assert
        Assert.Equal(2, result.ObservedEmailCount);
        Assert.Equal(1, result.RemotelyDeletedEmailCount);
        Assert.Equal([12U], store.TombstonedUids);

        // The confirmed occurrence keeps the flags nobody said had changed, and its place in the queue moves.
        Assert.Null(store.RowOf(11).Snapshot);
        Assert.Equal(RunInstant, store.RowOf(11).ObservedAt);
    }

    /// <summary>
    /// The claim the optimization rests on: a narrowed answer and a full scan leave the same emails stored and the same
    /// ones gone. Only the work differs.
    /// </summary>
    [Fact]
    public async Task ReconcileAsync_NarrowedAnswerAndFullScan_ReachTheSameEndState()
    {
        // Arrange
        var fullScanStore = new FakeReconciliationStore(StoredOccurrences(10, 11, 12));
        await using var fullScanSession = CreateSessionHolding(10, 11);
        var fullScanReconciler = CreateReconciler(fullScanStore, RemotelyDeletedEmailDisposition.RetainTombstone);

        var narrowedStore = new FakeReconciliationStore(StoredOccurrences(10, 11, 12));
        await using var narrowedSession = CreateSessionReporting(describedUids: [10], confirmedUids: [11]);
        var narrowedReconciler = CreateReconciler(narrowedStore, RemotelyDeletedEmailDisposition.RetainTombstone);

        // Act
        var fullScan = await fullScanReconciler.ReconcileAsync(
            fullScanSession,
            Account,
            InboxFolder,
            SelectedUidValidity,
            reconciledThroughModSeq: null,
            CancellationToken.None);
        var narrowed = await narrowedReconciler.ReconcileAsync(
            narrowedSession,
            Account,
            InboxFolder,
            SelectedUidValidity,
            reconciledThroughModSeq: 40UL,
            CancellationToken.None);

        // Assert
        Assert.Equal(fullScan.ObservedEmailCount, narrowed.ObservedEmailCount);
        Assert.Equal(fullScan.RemotelyDeletedEmailCount, narrowed.RemotelyDeletedEmailCount);
        Assert.Equal(fullScanStore.TombstonedUids, narrowedStore.TombstonedUids);
    }

    /// <summary>The sequence the caller holds is what the server is asked to narrow by, and it has to arrive unchanged.</summary>
    [Fact]
    public async Task ReconcileAsync_CheckpointCarriesASequence_AsksTheServerToNarrowByIt()
    {
        // Arrange
        var store = new FakeReconciliationStore(StoredOccurrences(10));
        await using var mailboxSession = CreateSessionHolding(10);
        var reconciler = CreateReconciler(store, RemotelyDeletedEmailDisposition.RetainTombstone);

        // Act
        await reconciler.ReconcileAsync(
            mailboxSession,
            Account,
            InboxFolder,
            SelectedUidValidity,
            reconciledThroughModSeq: 40UL,
            CancellationToken.None);

        // Assert
        await mailboxSession.Received(1).ObserveWindowWithoutSettingSeenAsync(
            Arg.Any<IReadOnlyList<ImapUid>>(),
            40UL,
            Arg.Any<CancellationToken>());
    }

    /// <summary>A pass that emptied the folder's queue may record how far it covered; that is what a later run narrows by.</summary>
    [Fact]
    public async Task ReconcileAsync_WindowCoveredTheWholeFolder_ReportsTheSequenceItWasReadUnder()
    {
        // Arrange
        var store = new FakeReconciliationStore(StoredOccurrences(10, 11));
        await using var mailboxSession = CreateSessionHolding(folderHighestModSeq: 91UL, presentUids: [10, 11]);
        var reconciler = CreateReconciler(store, RemotelyDeletedEmailDisposition.RetainTombstone);

        // Act
        var result = await reconciler.ReconcileAsync(
            mailboxSession,
            Account,
            InboxFolder,
            SelectedUidValidity,
            reconciledThroughModSeq: null,
            CancellationToken.None);

        // Assert
        Assert.False(result.EmailsRemain);
        Assert.Equal(91UL, result.ReconciledThroughModSeq);
    }

    /// <summary>
    /// A partial pass may not record a sequence. Doing so would assert that everything older than it is accounted for,
    /// including the occurrences this window never reached, which would then never be asked about again.
    /// </summary>
    [Fact]
    public async Task ReconcileAsync_WindowLeftEmailsBehind_ReportsNoSequenceToNarrowByLater()
    {
        // Arrange
        var storedUids = Enumerable.Range(10, 6).Select(uid => (uint)uid).ToArray();
        var store = new FakeReconciliationStore(StoredOccurrences(storedUids));
        await using var mailboxSession = CreateSessionHolding(folderHighestModSeq: 91UL, presentUids: storedUids);
        var reconciler = CreateReconciler(
            store,
            RemotelyDeletedEmailDisposition.RetainTombstone,
            maxReconciledEmailsPerRun: 3);

        // Act
        var result = await reconciler.ReconcileAsync(
            mailboxSession,
            Account,
            InboxFolder,
            SelectedUidValidity,
            reconciledThroughModSeq: null,
            CancellationToken.None);

        // Assert
        Assert.True(result.EmailsRemain);
        Assert.Null(result.ReconciledThroughModSeq);
    }

    /// <summary>Builds a session that describes some of the window, confirms some of it, and says nothing about the rest.</summary>
    private static IMailboxSession CreateSessionReporting(uint[] describedUids, uint[] confirmedUids)
    {
        var mailboxSession = Substitute.For<IMailboxSession>();
        var observation = new RemoteFolderWindowObservation(
            [
                .. describedUids.Select(uid => new RemoteEmailFlagObservation(
                    ImapUid.Create(uid),
                    new RemoteEmailFlagSnapshot(
                        RunInstant,
                        IsSeen: true,
                        IsAnswered: false,
                        IsFlagged: false,
                        IsDraft: false,
                        IsDeleted: false,
                        Keywords: RemoteEmailKeywords.None))),
            ],
            [.. confirmedUids.Select(ImapUid.Create)],
            FolderHighestModSeq: 91UL);

        mailboxSession
            .ObserveWindowWithoutSettingSeenAsync(Arg.Any<IReadOnlyList<ImapUid>>(), Arg.Any<ulong?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(observation));

        return mailboxSession;
    }

    /// <summary>Builds a session whose folder holds the named UIDs and reports one <c>\Seen</c> value for all of them.</summary>
    private static IMailboxSession CreateSessionReportingSeenState(bool isSeen, params uint[] presentUids) =>
        CreateSessionReportingFlags(isSeen, isFlagged: false, RemoteEmailKeywords.None, presentUids);

    /// <summary>Builds a session whose folder holds the named UIDs and reports the same three writable values for all of them.</summary>
    private static IMailboxSession CreateSessionReportingFlags(
        bool isSeen,
        bool isFlagged,
        RemoteEmailKeywords keywords,
        params uint[] presentUids)
    {
        var mailboxSession = Substitute.For<IMailboxSession>();
        IReadOnlyList<RemoteEmailFlagObservation> observations =
        [
            .. presentUids.Select(uid => new RemoteEmailFlagObservation(
                ImapUid.Create(uid),
                new RemoteEmailFlagSnapshot(
                    RunInstant,
                    isSeen,
                    IsAnswered: false,
                    isFlagged,
                    IsDraft: false,
                    IsDeleted: false,
                    keywords))),
        ];

        mailboxSession
            .ObserveWindowWithoutSettingSeenAsync(Arg.Any<IReadOnlyList<ImapUid>>(), Arg.Any<ulong?>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(RemoteFolderWindowObservation.FromDescribedOccurrences(
                [
                    .. observations.Where(observation =>
                        call.Arg<IReadOnlyList<ImapUid>>()!.Contains(observation.Uid)),
                ],
                folderHighestModSeq: null)));

        return mailboxSession;
    }

    /// <summary>Builds a session whose folder still holds exactly the named UIDs, and says nothing about the rest.</summary>
    private static IMailboxSession CreateSessionHolding(params uint[] presentUids) =>
        CreateSessionHolding(folderHighestModSeq: null, presentUids);

    /// <summary>Builds the same session, for a folder whose server reports a modification sequence.</summary>
    private static IMailboxSession CreateSessionHolding(ulong? folderHighestModSeq, params uint[] presentUids)
    {
        var mailboxSession = Substitute.For<IMailboxSession>();
        IReadOnlyList<RemoteEmailFlagObservation> observations =
        [
            .. presentUids.Select(uid => new RemoteEmailFlagObservation(
                ImapUid.Create(uid),
                new RemoteEmailFlagSnapshot(
                    RunInstant,
                    IsSeen: false,
                    IsAnswered: false,
                    IsFlagged: false,
                    IsDraft: false,
                    IsDeleted: false,
                    Keywords: RemoteEmailKeywords.None))),
        ];

        mailboxSession
            .ObserveWindowWithoutSettingSeenAsync(Arg.Any<IReadOnlyList<ImapUid>>(), Arg.Any<ulong?>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(RemoteFolderWindowObservation.FromDescribedOccurrences(
                [
                    .. observations.Where(observation =>
                        call.Arg<IReadOnlyList<ImapUid>>()!.Contains(observation.Uid)),
                ],
                folderHighestModSeq)));

        return mailboxSession;
    }

    /// <summary>A pass that moved something says so once, naming the rows a reader has to read again.</summary>
    [Fact]
    public async Task ReconcileAsync_WithAClientListening_SignalsTheOccurrencesThatMovedInThatFolder()
    {
        // Arrange
        var store = new FakeReconciliationStore(StoredOccurrences(10, 11, 12));
        await using var mailboxSession = CreateSessionHolding(10, 12);
        var channel = new RecordingClientSignalChannel();
        var clock = new FakeTimeProvider(RunInstant);
        await using var signals = new ClientSignals([channel], clock);
        var reconciler = CreateReconciler(
            store,
            RemotelyDeletedEmailDisposition.RetainTombstone,
            timeProvider: clock,
            signals: signals);

        // Act
        await reconciler.ReconcileAsync(
            mailboxSession,
            Account,
            InboxFolder,
            SelectedUidValidity,
            reconciledThroughModSeq: null,
            CancellationToken.None);

        clock.Advance(ClientSignals.FoldingWindow);
        await signals.DrainAsync();

        // Assert
        var signal = Assert.Single(channel.Published);
        Assert.Equal(ClientSignalKind.MailChanged, signal.Kind);
        Assert.Equal(Account.Id, signal.Account);
        Assert.Equal(InboxFolder.Alias, signal.Folder);
        Assert.Equal(3, signal.Emails.Count);
    }

    /// <summary>A pass that moved nothing says nothing, so an idle deployment is silent rather than chatty.</summary>
    [Fact]
    public async Task ReconcileAsync_WithNothingMoved_SignalsNothing()
    {
        // Arrange
        var store = new FakeReconciliationStore(StoredOccurrences());
        await using var mailboxSession = CreateSessionHolding();
        var channel = new RecordingClientSignalChannel();
        var clock = new FakeTimeProvider(RunInstant);
        await using var signals = new ClientSignals([channel], clock);
        var reconciler = CreateReconciler(
            store,
            RemotelyDeletedEmailDisposition.RetainTombstone,
            timeProvider: clock,
            signals: signals);

        // Act
        await reconciler.ReconcileAsync(
            mailboxSession,
            Account,
            InboxFolder,
            SelectedUidValidity,
            reconciledThroughModSeq: null,
            CancellationToken.None);

        clock.Advance(ClientSignals.FoldingWindow);
        await signals.DrainAsync();

        // Assert
        Assert.Empty(channel.Published);
    }

    private static MailboxReconciler CreateReconciler(
        FakeReconciliationStore store,
        RemotelyDeletedEmailDisposition disposition,
        int maxReconciledEmailsPerRun = 100,
        FakeTimeProvider? timeProvider = null,
        InMemoryMailboxMutationReconciliationStore? mutationStore = null,
        ClientSignals? signals = null)
    {
        var clock = timeProvider ?? new FakeTimeProvider(RunInstant);
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        var dispositionReader = Substitute.For<IRemotelyDeletedEmailDispositionReader>();
        dispositionReader.GetDisposition(Arg.Any<MailAccountId>()).Returns(disposition);

        return new MailboxReconciler(
            store,
            mutationStore ?? new InMemoryMailboxMutationReconciliationStore(),
            dispositionReader,
            new OptimisticConcurrencyRetryPolicy(sessionFactory, new PersistenceConcurrencyOptions(), clock),
            signals ?? ClientSignalPublishers.ReachingNobody,
            clock,
            new MailboxSynchronizationOptions { MaxReconciledEmailsPerRun = maxReconciledEmailsPerRun });
    }

    /// <summary>Builds the record a completed relocation or delete of one stored occurrence would have left behind.</summary>
    private static MailboxMutationRecord MutationRemoving(
        StoredEmailId storedEmailId,
        uint uid,
        bool isRelocation,
        DateTimeOffset? recordedAt = null,
        AuthoredDeleteEmailDisposition localDisposition = AuthoredDeleteEmailDisposition.RetainLocalCopy,
        AuthoredDeleteEmailDisposition? relocationDisposition = null)
    {
        var occurrence = EmailOccurrenceId.Create(Account.Id, InboxFolder.Id, SelectedUidValidity, ImapUid.Create(uid));
        var requester = MailboxMutationRequester.Rule("file-newsletters", "1");
        var opened = recordedAt ?? RunInstant;

        return new MailboxMutationRecord
        {
            Id = MailboxMutationRecordId.Create(Guid.CreateVersion7(opened)),
            Request = isRelocation
                ? MailboxMutationRequest.Relocate(
                    storedEmailId, SyntheticMailOwner.Deployment,
                    occurrence,
                    requester,
                    RemoteFolderPath.Create("Archive", '/'),
                    relocationDisposition)
                : MailboxMutationRequest.Delete(
                    storedEmailId, SyntheticMailOwner.Deployment,
                    occurrence,
                    requester,
                    localDisposition),
            Stage = MailboxMutationStage.Completed,
            IsAudited = false,
            RequiresSourceRemoval = isRelocation,
            Placement = isRelocation
                ? RemoteEmailPlacement.Reported(ImapUidValidity.Create(99), ImapUid.Create(4))
                : RemoteEmailPlacement.NotReported(),
            AttemptCount = 1,
            RecordedAt = opened,
            StageChangedAt = opened,
            LastFailure = null,
            PlacementObservedAt = null,
            SourceRemovalObservedAt = null,
        };
    }

    /// <summary>Builds the record a completed <c>\Seen</c> store against one stored occurrence would have left behind.</summary>
    private static MailboxMutationRecord MutationSettingSeen(
        StoredEmailId storedEmailId,
        uint uid,
        bool isSeen,
        MailboxMutationStage stage = MailboxMutationStage.Completed,
        DateTimeOffset? stagedAt = null)
    {
        var occurrence = EmailOccurrenceId.Create(Account.Id, InboxFolder.Id, SelectedUidValidity, ImapUid.Create(uid));
        var staged = stagedAt ?? RunInstant;

        return new MailboxMutationRecord
        {
            Id = MailboxMutationRecordId.Create(Guid.CreateVersion7(staged)),
            Request = MailboxMutationRequest.SetSeen(
                storedEmailId, SyntheticMailOwner.Deployment,
                occurrence,
                MailboxMutationRequester.Rule("mark-newsletters-read", "1"),
                isSeen),
            Stage = stage,
            IsAudited = false,
            RequiresSourceRemoval = false,
            Placement = RemoteEmailPlacement.NotReported(),
            AttemptCount = 1,
            RecordedAt = staged,
            StageChangedAt = staged,
            LastFailure = null,
            PlacementObservedAt = null,
            SourceRemovalObservedAt = null,
        };
    }

    /// <summary>Builds the record a completed <c>\Flagged</c> store against one stored occurrence would have left behind.</summary>
    private static MailboxMutationRecord MutationSettingFlagged(
        StoredEmailId storedEmailId,
        uint uid,
        bool isFlagged,
        DateTimeOffset? stagedAt = null) =>
        MutationSettingSeen(storedEmailId, uid, isSeen: false, stagedAt: stagedAt) with
        {
            Request = MailboxMutationRequest.SetFlagged(
                storedEmailId, SyntheticMailOwner.Deployment,
                EmailOccurrenceId.Create(Account.Id, InboxFolder.Id, SelectedUidValidity, ImapUid.Create(uid)),
                MailboxMutationRequester.Command("triage-1"),
                isFlagged),
        };

    /// <summary>Builds the record a completed keyword addition against one stored occurrence would have left behind.</summary>
    private static MailboxMutationRecord MutationAddingKeywords(
        StoredEmailId storedEmailId,
        uint uid,
        params string[] keywords) =>
        MutationSettingSeen(storedEmailId, uid, isSeen: false) with
        {
            Request = MailboxMutationRequest.AddKeywords(
                storedEmailId, SyntheticMailOwner.Deployment,
                EmailOccurrenceId.Create(Account.Id, InboxFolder.Id, SelectedUidValidity, ImapUid.Create(uid)),
                MailboxMutationRequester.Command("triage-1"),
                AuthoredMailKeywords.Create(keywords)),
        };

    private static IReadOnlyList<StoredOccurrence> StoredOccurrences(params uint[] uids) =>
    [
        .. uids.Select(uid => new StoredOccurrence(
            StoredEmailId.Create(Guid.CreateVersion7(RunInstant.AddSeconds(uid))),
            uid)),
    ];

    /// <summary>Builds one stored occurrence whose remote flags an earlier run already read, so a later reading can differ from them.</summary>
    private static IReadOnlyList<StoredOccurrence> ObservedOccurrence(
        uint uid,
        bool isSeen,
        bool isFlagged = false,
        params string[] keywords) =>
    [
        new StoredOccurrence(
            StoredEmailId.Create(Guid.CreateVersion7(RunInstant.AddSeconds(uid))),
            uid,
            isSeen,
            isFlagged,
            RemoteEmailKeywords.Create(keywords)),
    ];

    /// <summary>One locally stored occurrence a test arranged, before any run has touched it.</summary>
    /// <param name="StoredEmailId">The local identity the outcome is written against.</param>
    /// <param name="Uid">The UID the folder holds it at.</param>
    /// <param name="PreviouslyObservedSeenState">
    /// The remote <c>\Seen</c> value an earlier run recorded, or <see langword="null" /> when no run has read this
    /// occurrence's flags. Supplying one is what makes the row previously observed, exactly as the column pair does in
    /// the database.
    /// </param>
    /// <param name="PreviouslyObservedFlaggedState">Where the <c>\Flagged</c> flag stood at that earlier reading.</param>
    /// <param name="PreviouslyObservedKeywords">The keywords that earlier reading recorded, or <see langword="null" /> for none.</param>
    /// <param name="PreviouslyObservedAt">
    /// When that earlier reading happened, defaulting to an hour before the run. Two occurrences read at different
    /// instants are what a test needs to say which of them a bound computed across the window is taken from.
    /// </param>
    private sealed record StoredOccurrence(
        StoredEmailId StoredEmailId,
        uint Uid,
        bool? PreviouslyObservedSeenState = null,
        bool PreviouslyObservedFlaggedState = false,
        RemoteEmailKeywords? PreviouslyObservedKeywords = null,
        DateTimeOffset? PreviouslyObservedAt = null);

    /// <summary>
    /// Holds the reconciliation queue the way the database does, because the queue is the mechanism under test: the
    /// window is ordered by how long ago each row was observed, so a fake that returned an arbitrary order would let a
    /// pass that never advances pass its own test.
    /// </summary>
    private sealed class FakeReconciliationStore : IStoredEmailReconciliationStore
    {
        private readonly Dictionary<StoredEmailId, ReconciledRow> rowsById;

        public FakeReconciliationStore(IReadOnlyList<StoredOccurrence> storedOccurrences)
        {
            this.rowsById = storedOccurrences.ToDictionary(
                occurrence => occurrence.StoredEmailId,
                static occurrence => new ReconciledRow(
                    occurrence.Uid,
                    occurrence.PreviouslyObservedSeenState,
                    occurrence.PreviouslyObservedFlaggedState,
                    occurrence.PreviouslyObservedKeywords,
                    occurrence.PreviouslyObservedAt));
        }

        public List<uint> AskedAboutUids { get; } = [];

        public List<ReconciledFolderOutcome> AppliedOutcomes { get; } = [];

        public IReadOnlyList<uint> TombstonedUids =>
        [
            .. this.rowsById.Values
                .Where(row => row.RemoteExpungeObservedAt is not null)
                .Select(row => row.Uid)
                .Order(),
        ];

        public IReadOnlyList<uint> RemovedUids => [.. this.removedUids.Order()];

        private readonly List<uint> removedUids = [];

        public ReconciledRow RowOf(uint uid) => this.rowsById.Values.Single(row => row.Uid == uid);

        public StoredEmailId StoredEmailIdOf(uint uid) =>
            this.rowsById.Single(entry => entry.Value.Uid == uid).Key;

        public Task<IReadOnlyList<StoredEmailAwaitingReconciliation>> GetReconciliationWindowAsync(
            MailAccountIdentity account,
            MailFolderResolutionId folderResolutionId,
            ImapUidValidity uidValidity,
            int maxEmailCount,
            CancellationToken cancellationToken)
        {
            // Occurrences are stored under the selected UIDVALIDITY only, so a folder the server renumbered selects
            // none of them rather than reporting the whole mailbox as missing.
            if (uidValidity != SelectedUidValidity)
            {
                return Task.FromResult<IReadOnlyList<StoredEmailAwaitingReconciliation>>([]);
            }

            var eligible = this.rowsById.Where(entry => entry.Value.RemoteExpungeObservedAt is null).ToArray();
            var previouslyObserved = eligible
                .Where(entry => entry.Value.ObservedAt is not null)
                .OrderBy(entry => entry.Value.ObservedAt)
                .ThenBy(entry => entry.Value.Uid)
                .Take(maxEmailCount)
                .ToArray();

            // The same division the store applies, so a reconciler that let new mail crowd out older rows would fail
            // here rather than only against a real database.
            var neverObserved = eligible
                .Where(entry => entry.Value.ObservedAt is null)
                .OrderBy(entry => entry.Value.Uid)
                .Take(ReconciliationWindowBudget.NeverObservedShareOf(maxEmailCount, previouslyObserved.Length))
                .ToArray();

            IReadOnlyList<StoredEmailAwaitingReconciliation> window =
            [
                .. neverObserved
                    .Concat(previouslyObserved.Take(maxEmailCount - neverObserved.Length))
                    .Select(static entry => new StoredEmailAwaitingReconciliation(
                        entry.Key,
                        ImapUid.Create(entry.Value.Uid),
                        entry.Value.ObservedAt is { } observedAt
                            ? new RemoteWritableFlagObservation(
                                observedAt,
                                entry.Value.Snapshot?.IsSeen ?? false,
                                entry.Value.Snapshot?.IsFlagged ?? false,
                                entry.Value.Snapshot?.Keywords ?? RemoteEmailKeywords.None)
                            : null)),
            ];

            this.AskedAboutUids.AddRange(window.Select(candidate => candidate.Uid.Value));

            return Task.FromResult(window);
        }

        public Task ApplyReconciliationOutcomeAsync(
            IPersistenceSession session,
            ReconciledFolderOutcome outcome,
            CancellationToken cancellationToken)
        {
            this.AppliedOutcomes.Add(outcome);

            foreach (var observed in outcome.StillPresent)
            {
                if (!this.rowsById.TryGetValue(observed.StoredEmailId, out var observedRow)
                    || HasNewerObservationThan(observedRow, observed.Snapshot.ObservedAt))
                {
                    continue;
                }

                observedRow.ObservedAt = observed.Snapshot.ObservedAt;
                observedRow.Snapshot = observed.Snapshot;
            }

            foreach (var storedEmailId in outcome.ConfirmedUnchanged)
            {
                if (this.rowsById.TryGetValue(storedEmailId, out var confirmedRow)
                    && !HasNewerObservationThan(confirmedRow, outcome.ObservedAt))
                {
                    confirmedRow.ObservedAt = outcome.ObservedAt;
                }
            }

            foreach (var attributed in outcome.RemovedByOwnMutation)
            {
                if (this.rowsById.TryGetValue(attributed.StoredEmailId, out var attributedRow)
                    && !HasNewerObservationThan(attributedRow, outcome.ObservedAt))
                {
                    attributedRow.ObservedAt = outcome.ObservedAt;
                }
            }

            foreach (var storedEmailId in outcome.Disappeared)
            {
                if (!this.rowsById.TryGetValue(storedEmailId, out var row)
                    || HasNewerObservationThan(row, outcome.ObservedAt))
                {
                    continue;
                }

                if (outcome.Disposition is RemotelyDeletedEmailDisposition.EraseLocalCopy)
                {
                    this.removedUids.Add(row.Uid);
                    this.rowsById.Remove(storedEmailId);

                    continue;
                }

                row.RemoteExpungeObservedAt ??= outcome.ObservedAt;
                row.ObservedAt ??= outcome.ObservedAt;
            }

            return Task.CompletedTask;
        }

        private static bool HasNewerObservationThan(ReconciledRow row, DateTimeOffset? observedAt) =>
            row.ObservedAt is { } recordedAt && observedAt is { } proposedAt && recordedAt > proposedAt;
    }

    /// <summary>The columns reconciliation writes on one stored email.</summary>
    /// <remarks>
    /// A seeded <c>\Seen</c> value arrives as an observation rather than as a bare flag, because that is the only shape
    /// the database can hold: the stored flags and the timestamp saying they were read move together, and a fake that
    /// let them disagree would answer a window with a previous value nobody had ever observed.
    /// </remarks>
    private sealed class ReconciledRow
    {
        public ReconciledRow(
            uint uid,
            bool? previouslyObservedSeenState = null,
            bool previouslyObservedFlaggedState = false,
            RemoteEmailKeywords? previouslyObservedKeywords = null,
            DateTimeOffset? previouslyObservedAt = null)
        {
            this.Uid = uid;

            if (previouslyObservedSeenState is not { } seenState)
            {
                return;
            }

            var seededAt = previouslyObservedAt ?? RunInstant.AddHours(-1);

            this.ObservedAt = seededAt;
            this.Snapshot = new RemoteEmailFlagSnapshot(
                seededAt,
                seenState,
                IsAnswered: false,
                previouslyObservedFlaggedState,
                IsDraft: false,
                IsDeleted: false,
                previouslyObservedKeywords ?? RemoteEmailKeywords.None);
        }

        public uint Uid { get; }

        public DateTimeOffset? ObservedAt { get; set; }

        public DateTimeOffset? RemoteExpungeObservedAt { get; set; }

        public RemoteEmailFlagSnapshot? Snapshot { get; set; }
    }

    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
