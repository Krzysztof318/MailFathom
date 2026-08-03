// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Application.Synchronization.Reconciliation;
using MailFathom.Application.Synchronization.Sessions;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Synchronization.Reconciliation;

public sealed class MailboxReconcilerTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("primary");

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
                    IsDeleted: true)),
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
        var store = new FakeReconciliationStore([]);
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
                        IsDeleted: false))),
            ],
            [.. confirmedUids.Select(ImapUid.Create)],
            FolderHighestModSeq: 91UL);

        mailboxSession
            .ObserveWindowWithoutSettingSeenAsync(Arg.Any<IReadOnlyList<ImapUid>>(), Arg.Any<ulong?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(observation));

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
                    IsDeleted: false))),
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

    private static MailboxReconciler CreateReconciler(
        FakeReconciliationStore store,
        RemotelyDeletedEmailDisposition disposition,
        int maxReconciledEmailsPerRun = 100,
        FakeTimeProvider? timeProvider = null)
    {
        var clock = timeProvider ?? new FakeTimeProvider(RunInstant);
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        var dispositionReader = Substitute.For<IRemotelyDeletedEmailDispositionReader>();
        dispositionReader.GetDisposition(Arg.Any<MailAccountId>()).Returns(disposition);

        return new MailboxReconciler(
            store,
            dispositionReader,
            new OptimisticConcurrencyRetryPolicy(sessionFactory, new PersistenceConcurrencyOptions(), clock),
            clock,
            new MailboxSynchronizationOptions { MaxReconciledEmailsPerRun = maxReconciledEmailsPerRun });
    }

    private static IReadOnlyList<StoredOccurrence> StoredOccurrences(params uint[] uids) =>
    [
        .. uids.Select(uid => new StoredOccurrence(
            StoredEmailId.Create(Guid.CreateVersion7(RunInstant.AddSeconds(uid))),
            uid)),
    ];

    /// <summary>One locally stored occurrence a test arranged, before any run has touched it.</summary>
    private sealed record StoredOccurrence(StoredEmailId StoredEmailId, uint Uid);

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
                occurrence => new ReconciledRow(occurrence.Uid));
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

        public Task<IReadOnlyList<StoredEmailAwaitingReconciliation>> GetReconciliationWindowAsync(
            MailAccountId accountId,
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
                    .Select(entry => new StoredEmailAwaitingReconciliation(entry.Key, ImapUid.Create(entry.Value.Uid))),
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
    private sealed class ReconciledRow(uint uid)
    {
        public uint Uid { get; } = uid;

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
