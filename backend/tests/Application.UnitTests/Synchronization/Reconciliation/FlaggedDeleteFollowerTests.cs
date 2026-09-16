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

public sealed class FlaggedDeleteFollowerTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("primary");

    private static readonly MailFolderResolution InboxFolder = MailFolderResolution.FirstBindingOf(
        MailFolderAlias.Create("inbox"),
        RemoteFolderPath.Create("INBOX", '/'));

    private static readonly ImapUidValidity SelectedUidValidity = ImapUidValidity.Create(7);

    private static readonly DateTimeOffset RecordedAt = new(2026, 9, 16, 9, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset RunInstant = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    private static readonly StoredEmailId DeletedEmail = StoredEmailId.Create(Guid.CreateVersion7(RecordedAt));

    /// <summary>The first reading that still sees the flag applies the disposition the delete was recorded under.</summary>
    [Theory]
    [InlineData(AuthoredDeleteEmailDisposition.EraseLocalCopy)]
    [InlineData(AuthoredDeleteEmailDisposition.RetainTombstone)]
    [InlineData(AuthoredDeleteEmailDisposition.RetainLocalCopy)]
    public async Task FollowAsync_AFlagOnlyDeleteSeenFlagged_SettlesItUnderItsRecordedDisposition(
        AuthoredDeleteEmailDisposition disposition)
    {
        // Arrange
        var context = new FollowerContext().Recording(FlagOnlyDelete(42, disposition));
        await using var session = SessionReporting((42, IsDeleted: true));

        // Act
        var followUp = await context.FollowAsync(session);

        // Assert
        var settled = Assert.Single(context.Store.Settlements).Settled;
        Assert.Equal([new SettledFlaggedDelete(DeletedEmail, disposition)], settled);
        Assert.Equal(RunInstant, context.Mutations.RecordOf(context.RecordId).DeleteFlagSettledAt);
        Assert.True(context.Mutations.RecordOf(context.RecordId).IsReconciled);
        var suppressed = Assert.Single(followUp.SuppressedChanges);
        Assert.Equal(MailboxChangeKind.EmailLeftFolder, suppressed.Kind);
        Assert.Equal(DeletedEmail, suppressed.StoredEmailId);
    }

    /// <summary>An expunging delete is the backward pass's to settle, so this pass never asks the server about it.</summary>
    [Fact]
    public async Task FollowAsync_OnlyExpungingDeletes_AsksTheServerNothing()
    {
        // Arrange
        var context = new FollowerContext().Recording(Delete(42, AuthoredDeleteServerDisposition.Expunge));
        await using var session = SessionReporting((42, IsDeleted: true));

        // Act
        var followUp = await context.FollowAsync(session);

        // Assert
        Assert.Same(FlaggedDeleteFollowUp.Nothing, followUp);
        Assert.Empty(context.Store.Settlements);
        await session.DidNotReceive().ObserveWindowWithoutSettingSeenAsync(
            Arg.Any<IReadOnlyList<ImapUid>>(),
            Arg.Any<ulong?>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A delete undone on the server before any reading saw its flag is settled with nothing to dispose of.</summary>
    [Fact]
    public async Task FollowAsync_AFlagOnlyDeleteUndoneBeforeItWasSeen_SettlesItAndDisposesOfNothing()
    {
        // Arrange
        var context = new FollowerContext().Recording(FlagOnlyDelete(42, AuthoredDeleteEmailDisposition.EraseLocalCopy));
        await using var session = SessionReporting((42, IsDeleted: false));

        // Act
        var followUp = await context.FollowAsync(session);

        // Assert
        Assert.Empty(Assert.Single(context.Store.Settlements).Settled);
        Assert.Equal(RunInstant, context.Mutations.RecordOf(context.RecordId).DeleteFlagSettledAt);
        Assert.Empty(followUp.SuppressedChanges);
    }

    /// <summary>
    /// An occurrence gone before any reading saw its flag is the backward pass's to attribute, which applies the delete's
    /// disposition exactly as it does for a delete that expunged.
    /// </summary>
    [Fact]
    public async Task FollowAsync_AFlagOnlyDeleteWhoseOccurrenceIsAlreadyGone_LeavesItToTheBackwardPass()
    {
        // Arrange
        var context = new FollowerContext().Recording(FlagOnlyDelete(42, AuthoredDeleteEmailDisposition.EraseLocalCopy));
        await using var session = SessionReporting();

        // Act
        await context.FollowAsync(session);

        // Assert
        Assert.Empty(Assert.Single(context.Store.Settlements).Settled);
        Assert.Null(context.Mutations.RecordOf(context.RecordId).DeleteFlagSettledAt);
    }

    /// <summary>An occurrence the server still holds flagged only moves to the back of the queue.</summary>
    [Fact]
    public async Task FollowAsync_AFollowedOccurrenceStillFlagged_IsKeptAndNothingComesBack()
    {
        // Arrange
        var followed = Followed(42, keptEmail: DeletedEmail);
        var context = new FollowerContext().Following(followed);
        await using var session = SessionReporting((42, IsDeleted: true));

        // Act
        var followUp = await context.FollowAsync(session);

        // Assert
        var settlement = Assert.Single(context.Store.Settlements);
        Assert.Equal([followed], settlement.StillFlagged);
        Assert.Empty(settlement.Expunged);
        Assert.Empty(settlement.Restored);
        Assert.Empty(followUp.AwaitingRestore);
    }

    /// <summary>
    /// A later expunge by somebody else is recorded against the row and nothing else: the local copy stays as the delete
    /// left it, and the account's setting for mail somebody else deleted is never asked.
    /// </summary>
    [Fact]
    public async Task FollowAsync_AFollowedOccurrenceExpungedElsewhere_RecordsTheExpungeAndStopsFollowingIt()
    {
        // Arrange
        var followed = Followed(42, keptEmail: DeletedEmail);
        var context = new FollowerContext().Following(followed);
        await using var session = SessionReporting();

        // Act
        var followUp = await context.FollowAsync(session);

        // Assert
        var settlement = Assert.Single(context.Store.Settlements);
        Assert.Equal([followed], settlement.Expunged);
        Assert.Empty(settlement.Restored);
        Assert.Empty(followUp.AwaitingRestore);
    }

    /// <summary>The flag removed on the server undoes the delete, and a row that was kept comes back as it is.</summary>
    [Fact]
    public async Task FollowAsync_AKeptOccurrenceUndeletedOnTheServer_BringsTheRowBackAndSignalsIt()
    {
        // Arrange
        var followed = Followed(42, keptEmail: DeletedEmail);
        var channel = new RecordingClientSignalChannel();
        var context = new FollowerContext(channel).Following(followed);
        await using var session = SessionReporting((42, IsDeleted: false));

        // Act
        var followUp = await context.FollowAsync(session);
        context.Clock.Advance(ClientSignals.FoldingWindow);
        await context.Signals.DrainAsync();

        // Assert
        Assert.Equal([followed], Assert.Single(context.Store.Settlements).Restored);
        Assert.Empty(followUp.AwaitingRestore);
        var signal = Assert.Single(channel.Published);
        Assert.Equal(ClientSignalKind.MailChanged, signal.Kind);
        Assert.Equal([DeletedEmail], signal.Emails);
    }

    /// <summary>An erased copy cannot come back by itself, so its occurrence is handed to the run to store again.</summary>
    [Fact]
    public async Task FollowAsync_AnErasedOccurrenceUndeletedOnTheServer_HandsItToTheRunToStoreAgain()
    {
        // Arrange
        var followed = Followed(42, keptEmail: null);
        var context = new FollowerContext().Following(followed);
        await using var session = SessionReporting((42, IsDeleted: false));

        // Act
        var followUp = await context.FollowAsync(session);

        // Assert
        Assert.Equal([followed], followUp.AwaitingRestore);
        Assert.Empty(Assert.Single(context.Store.Settlements).Restored);
        Assert.Empty(context.Store.Retired);
    }

    /// <summary>An occurrence the server confirmed without describing said nothing about its flag, so nothing is decided about it.</summary>
    [Fact]
    public async Task FollowAsync_AFollowedOccurrenceConfirmedWithoutFlags_DecidesNothingAboutIt()
    {
        // Arrange
        var context = new FollowerContext().Following(Followed(42, keptEmail: DeletedEmail));
        await using var session = Substitute.For<IMailboxSession>();
        session
            .ObserveWindowWithoutSettingSeenAsync(Arg.Any<IReadOnlyList<ImapUid>>(), Arg.Any<ulong?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new RemoteFolderWindowObservation([], [ImapUid.Create(42)], FolderHighestModSeq: null)));

        // Act
        await context.FollowAsync(session);

        // Assert
        var settlement = Assert.Single(context.Store.Settlements);
        Assert.Empty(settlement.StillFlagged);
        Assert.Empty(settlement.Expunged);
        Assert.Empty(settlement.Restored);
    }

    /// <summary>Retiring an occurrence the run has stored again is one commit of its own.</summary>
    [Fact]
    public async Task RetireAsync_AnOccurrenceAwaitingRestore_StopsFollowingIt()
    {
        // Arrange
        var followed = Followed(42, keptEmail: null);
        var context = new FollowerContext().Following(followed);

        // Act
        await context.Follower.RetireAsync(followed, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([followed.Id], context.Store.Retired);
    }

    private static DeleteLeftFlagged Followed(uint uid, StoredEmailId? keptEmail) =>
        new(new DeleteLeftFlaggedId(Guid.CreateVersion7(RecordedAt)), ImapUid.Create(uid), keptEmail);

    private static MailboxMutationRecord FlagOnlyDelete(uint uid, AuthoredDeleteEmailDisposition disposition) =>
        Delete(uid, AuthoredDeleteServerDisposition.FlagDeleted, disposition);

    private static MailboxMutationRecord Delete(
        uint uid,
        AuthoredDeleteServerDisposition serverDisposition,
        AuthoredDeleteEmailDisposition localDisposition = AuthoredDeleteEmailDisposition.EraseLocalCopy)
    {
        var occurrence = EmailOccurrenceId.Create(Account, InboxFolder.Id, SelectedUidValidity, ImapUid.Create(uid));

        return new MailboxMutationRecord
        {
            Id = MailboxMutationRecordId.Create(Guid.CreateVersion7(RecordedAt)),
            Request = MailboxMutationRequest.Delete(
                DeletedEmail,
                occurrence,
                MailboxMutationRequester.Rule("drop-notifications", "1"),
                localDisposition,
                serverDisposition),
            Stage = MailboxMutationStage.Completed,
            IsAudited = false,
            RequiresSourceRemoval = false,
            Placement = RemoteEmailPlacement.NotReported(),
            AttemptCount = 1,
            RecordedAt = RecordedAt,
            StageChangedAt = RecordedAt,
            LastFailure = null,
            PlacementObservedAt = null,
            SourceRemovalObservedAt = null,
        };
    }

    /// <summary>Builds a session describing exactly the named occurrences, each with its <c>\Deleted</c> flag as given.</summary>
    private static IMailboxSession SessionReporting(params (uint Uid, bool IsDeleted)[] present)
    {
        var session = Substitute.For<IMailboxSession>();
        IReadOnlyList<RemoteEmailFlagObservation> observations =
        [
            .. present.Select(occurrence => new RemoteEmailFlagObservation(
                ImapUid.Create(occurrence.Uid),
                new RemoteEmailFlagSnapshot(
                    RunInstant,
                    IsSeen: false,
                    IsAnswered: false,
                    IsFlagged: false,
                    IsDraft: false,
                    occurrence.IsDeleted,
                    RemoteEmailKeywords.None))),
        ];

        session
            .ObserveWindowWithoutSettingSeenAsync(Arg.Any<IReadOnlyList<ImapUid>>(), Arg.Any<ulong?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(RemoteFolderWindowObservation.FromDescribedOccurrences(observations, folderHighestModSeq: null)));

        return session;
    }

    private sealed class FollowerContext
    {
        internal FollowerContext(RecordingClientSignalChannel? channel = null)
        {
            var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
            sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

            this.Signals = channel is null ? ClientSignalPublishers.ReachingNobody : new ClientSignals([channel], this.Clock);
            this.Follower = new FlaggedDeleteFollower(
                this.Store,
                this.Mutations,
                new OptimisticConcurrencyRetryPolicy(sessionFactory, new PersistenceConcurrencyOptions(), this.Clock),
                this.Signals,
                this.Clock,
                new MailboxSynchronizationOptions());
        }

        internal FakeTimeProvider Clock { get; } = new(RunInstant);

        internal ClientSignals Signals { get; }

        internal RecordingFlaggedDeleteStore Store { get; } = new();

        internal InMemoryMailboxMutationReconciliationStore Mutations { get; } = new();

        internal FlaggedDeleteFollower Follower { get; }

        internal MailboxMutationRecordId RecordId { get; private set; }

        internal FollowerContext Recording(MailboxMutationRecord record)
        {
            this.Mutations.Add(record);
            this.RecordId = record.Id;

            return this;
        }

        internal FollowerContext Following(DeleteLeftFlagged followed)
        {
            this.Store.Followed.Add(followed);

            return this;
        }

        internal Task<FlaggedDeleteFollowUp> FollowAsync(IMailboxSession session) => this.Follower.FollowAsync(
            session,
            Account,
            InboxFolder,
            SelectedUidValidity,
            TestContext.Current.CancellationToken);
    }

    /// <summary>Keeps what the follower asked the store to do, which is everything this pass decides.</summary>
    private sealed class RecordingFlaggedDeleteStore : IStoredEmailReconciliationStore
    {
        internal List<DeleteLeftFlagged> Followed { get; } = [];

        internal List<FlaggedDeleteSettlement> Settlements { get; } = [];

        internal List<DeleteLeftFlaggedId> Retired { get; } = [];

        public Task<IReadOnlyList<DeleteLeftFlagged>> GetDeletesLeftFlaggedAsync(
            MailAccountId account,
            MailFolderResolutionId folderResolutionId,
            ImapUidValidity uidValidity,
            int maxCount,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DeleteLeftFlagged>>([.. this.Followed.Take(maxCount)]);

        public Task ApplyFlaggedDeleteSettlementAsync(
            IPersistenceSession session,
            FlaggedDeleteSettlement settlement,
            CancellationToken cancellationToken)
        {
            this.Settlements.Add(settlement);

            return Task.CompletedTask;
        }

        public Task RetireDeletesLeftFlaggedAsync(
            IPersistenceSession session,
            IReadOnlyList<DeleteLeftFlaggedId> ids,
            CancellationToken cancellationToken)
        {
            this.Retired.AddRange(ids);

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<StoredEmailAwaitingReconciliation>> GetReconciliationWindowAsync(
            MailAccountId account,
            MailFolderResolutionId folderResolutionId,
            ImapUidValidity uidValidity,
            int maxEmailCount,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("Following flag-only deletes never reads the backward window.");

        public Task ApplyReconciliationOutcomeAsync(
            IPersistenceSession session,
            ReconciledFolderOutcome outcome,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("Following flag-only deletes never applies a backward outcome.");
    }

    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
