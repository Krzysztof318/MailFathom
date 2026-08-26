// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Mail.Maintenance;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization.Checkpoints;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Synchronization;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Maintenance;

/// <summary>Covers what happens when an operator asks for an account's mail to be read from the server again.</summary>
/// <remarks>
/// The cost is as much the contract as the removal. What the rewind itself does is take away a handful of rows, and
/// what that buys is a mailbox coming over IMAP again — so the figure an operator agrees to and the rows the command
/// then removes are two halves of one decision.
/// </remarks>
public sealed class MailSynchronizationRewindTests
{
    private static readonly MailAccountIdentity Account =
        MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("work"));
    private static readonly MailFolderAlias Archive = MailFolderAlias.Create("archive");
    private static readonly MailFolderAlias Inbox = MailFolderAlias.Create("inbox");

    /// <summary>The figure put in front of the operator is what the scope holds, counted through the walk's own reader.</summary>
    [Fact]
    public async Task AssessAsync_AScopeHoldingStoredMail_ReportsWhatWouldBeFetchedAgain()
    {
        // Arrange
        var counter = new RecordingCounter(storedEmailCount: 22_500);
        var rewind = RewindOver(new RecordingCheckpointStore([Inbox, Archive]), counter);
        var scope = new StoredMailScope(Account, null);

        // Act
        var storedEmailCount = await rewind.AssessAsync(scope, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(22_500, storedEmailCount);
        Assert.Equal([scope], counter.Scopes);
    }

    /// <summary>Assessing is a read, so nothing about the account's progress has changed once it has answered.</summary>
    [Fact]
    public async Task AssessAsync_AnyScope_DiscardsNoProgress()
    {
        // Arrange
        var checkpoints = new RecordingCheckpointStore([Inbox]);
        var rewind = RewindOver(checkpoints, new RecordingCounter(storedEmailCount: 4));

        // Act
        await rewind.AssessAsync(new StoredMailScope(Account, null), TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(checkpoints.Discards);
    }

    /// <summary>Every binding of the account is rewound in one transaction, and the operator is told which folders held progress.</summary>
    [Fact]
    public async Task RewindAsync_AnAccountWithSeveralFolders_DiscardsThemInOneCommittedTransaction()
    {
        // Arrange
        var checkpoints = new RecordingCheckpointStore([Inbox, Archive]);
        var rewind = RewindOver(checkpoints, new RecordingCounter(storedEmailCount: 12));

        // Act
        var rewound = await rewind.RewindAsync(
            new StoredMailScope(Account, null),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([Inbox, Archive], rewound);
        Assert.Equal([(Account, (MailFolderAlias?)null)], checkpoints.Discards);
        Assert.Single(checkpoints.Sessions.Distinct());
    }

    /// <summary>A narrowed rewind reaches one alias, so the rest of the account resumes exactly where it was.</summary>
    [Fact]
    public async Task RewindAsync_AScopeNarrowedToOneFolder_DiscardsOnlyThatAlias()
    {
        // Arrange
        var checkpoints = new RecordingCheckpointStore([Archive]);
        var rewind = RewindOver(checkpoints, new RecordingCounter(storedEmailCount: 12));

        // Act
        var rewound = await rewind.RewindAsync(
            new StoredMailScope(Account, Archive),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([Archive], rewound);
        Assert.Equal([(Account, (MailFolderAlias?)Archive)], checkpoints.Discards);
    }

    /// <summary>An account that has never synchronized has no progress to take away, which is an answer rather than a failure.</summary>
    [Fact]
    public async Task RewindAsync_AnAccountWithNoProgressAtAll_ReportsThatNoFolderHeldAny()
    {
        // Arrange
        var rewind = RewindOver(new RecordingCheckpointStore([]), new RecordingCounter(storedEmailCount: 0));

        // Act
        var rewound = await rewind.RewindAsync(
            new StoredMailScope(Account, null),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(rewound);
    }

    /// <summary>
    /// A run that decided from progress this removal took away is refused its advance rather than writing a checkpoint
    /// in front of mail the rewind was about to have re-read. The compare-and-set contract carries that, and the
    /// removal is what makes the run's expectation unsatisfiable.
    /// </summary>
    [Fact]
    public async Task DiscardCheckpointsAsync_AProgressAdvanceDecidedBeforeIt_IsRefusedRatherThanApplied()
    {
        // Arrange
        var checkpoints = new RecordingCheckpointStore([Inbox]);
        var rewind = RewindOver(checkpoints, new RecordingCounter(storedEmailCount: 3));
        var binding = new MailFolderResolutionId(Inbox, MailFolderResolutionGeneration.First);
        var decidedFrom = new SynchronizationCheckpoint(
            ImapUidValidity.Create(5),
            ImapUid.Create(40),
            SynchronizedAt: null);

        // Act
        await rewind.RewindAsync(new StoredMailScope(Account, null), TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<PersistenceConcurrencyConflictException>(() => checkpoints.SaveCheckpointAsync(
            new CommittingSession(),
            Account,
            binding,
            decidedFrom,
            decidedFrom.AdvanceTo(ImapUid.Create(80), DateTimeOffset.UnixEpoch),
            TestContext.Current.CancellationToken));
    }

    /// <summary>Reading what a rewind would cost is the administrative read, and a caller granted only the operating permission does not hold it.</summary>
    [Fact]
    public async Task AssessAsync_ACallerGrantedOnlyTheAdministrativeOperate_IsRefusedWithTheTransportAbsent()
    {
        // Arrange
        var rewind = RewindOver(
            new RecordingCheckpointStore([Inbox]),
            new RecordingCounter(storedEmailCount: 4),
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminOperate));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            rewind.AssessAsync(new StoredMailScope(Account, null), TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminRead, refusal.RequiredPermission);
    }

    /// <summary>The one route that makes a deployment pull a mailbox again asks to operate, which the administrative read does not carry.</summary>
    [Fact]
    public async Task RewindAsync_ACallerGrantedOnlyTheAdministrativeRead_IsRefusedWithTheTransportAbsent()
    {
        // Arrange
        var checkpoints = new RecordingCheckpointStore([Inbox]);
        var rewind = RewindOver(
            checkpoints,
            new RecordingCounter(storedEmailCount: 4),
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminRead));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            rewind.RewindAsync(new StoredMailScope(Account, null), TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminOperate, refusal.RequiredPermission);
        Assert.Empty(checkpoints.Discards);
    }

    private static MailSynchronizationRewind RewindOver(
        ISynchronizationCheckpointStore checkpoints,
        IStoredMailCounter counter,
        AccessAuthorization? authorization = null)
    {
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        return new MailSynchronizationRewind(
            checkpoints,
            counter,
            new OptimisticConcurrencyRetryPolicy(
                sessionFactory,
                new PersistenceConcurrencyOptions(),
                new FakeTimeProvider()),
            authorization ?? AccessAuthorizations.ForCallerGranted(
                MailFathomPermission.AdminRead,
                MailFathomPermission.AdminOperate));
    }

    /// <summary>Records which scopes were counted, and answers with the figure the test arranged.</summary>
    private sealed class RecordingCounter(int storedEmailCount) : IStoredMailCounter
    {
        public List<StoredMailScope> Scopes { get; } = [];

        public Task<int> CountStoredEmailsAsync(StoredMailScope scope, CancellationToken cancellationToken)
        {
            this.Scopes.Add(scope);

            return Task.FromResult(storedEmailCount);
        }
    }

    /// <summary>
    /// Stands in for the persisted progress, keeping the one behavior this use case depends on: a binding whose
    /// progress was discarded no longer satisfies the expectation a run in flight decided from.
    /// </summary>
    private sealed class RecordingCheckpointStore(IReadOnlyList<MailFolderAlias> foldersHoldingProgress)
        : ISynchronizationCheckpointStore
    {
        private readonly HashSet<MailFolderAlias> present = [.. foldersHoldingProgress];

        public List<(MailAccountIdentity Account, MailFolderAlias? FolderAlias)> Discards { get; } = [];

        public List<IPersistenceSession> Sessions { get; } = [];

        public Task<SynchronizationCheckpoint?> GetCheckpointAsync(
            MailAccountIdentity account,
            MailFolderResolutionId folderResolutionId,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                this.present.Contains(folderResolutionId.Alias)
                    ? new SynchronizationCheckpoint(ImapUidValidity.Create(5), ImapUid.Create(40), null)
                    : null);

        public Task SaveCheckpointAsync(
            IPersistenceSession session,
            MailAccountIdentity account,
            MailFolderResolutionId folderResolutionId,
            SynchronizationCheckpoint? expectedCheckpoint,
            SynchronizationCheckpoint checkpoint,
            CancellationToken cancellationToken)
        {
            if (expectedCheckpoint is not null && !this.present.Contains(folderResolutionId.Alias))
            {
                throw new PersistenceConcurrencyConflictException(
                    $"Synchronization progress expected for folder {folderResolutionId.Alias.Value} no longer exists.");
            }

            this.present.Add(folderResolutionId.Alias);

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<MailFolderAlias>> DiscardCheckpointsAsync(
            IPersistenceSession session,
            MailAccountIdentity account,
            MailFolderAlias? folderAlias,
            CancellationToken cancellationToken)
        {
            this.Discards.Add((account, folderAlias));
            this.Sessions.Add(session);

            IReadOnlyList<MailFolderAlias> discarded =
            [
                .. foldersHoldingProgress.Where(alias =>
                    this.present.Contains(alias) && (folderAlias is not { } narrowed || alias == narrowed)),
            ];

            foreach (var alias in discarded)
            {
                this.present.Remove(alias);
            }

            return Task.FromResult(discarded);
        }
    }

    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
