// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Synchronization.Administration;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Synchronization.Administration;

/// <summary>Covers the answer composed from configuration, the running process, and the durable checkpoints.</summary>
public sealed class MailSynchronizationStatusReaderTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 15, 10, 0, 0, TimeSpan.Zero);
    private static readonly MailAccountId Work = MailAccountId.Create("work");
    private static readonly MailFolderIdentity Inbox = new(Work, MailFolderAlias.Create("inbox"));
    private static readonly MailFolderIdentity Archive = new(Work, MailFolderAlias.Create("archive"));

    /// <summary>The operator's own switch, which is what makes every count below it still.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReadAsync_ReportsWhetherTheDeploymentSynchronizesAtAll(bool enabled)
    {
        // Arrange
        var reader = Reader(new MailSynchronizationRunLedger(new FakeTimeProvider(Start)), enabled: enabled);

        // Act
        var status = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(enabled, status.SynchronizationEnabled);
    }

    /// <summary>A folder no run has reached is the case an operator is most likely asking about, so it is reported rather than omitted.</summary>
    [Fact]
    public async Task ReadAsync_WithoutAnyRun_ReportsEveryMappedFolderWithNoProgress()
    {
        // Arrange
        var reader = Reader(new MailSynchronizationRunLedger(new FakeTimeProvider(Start)));

        // Act
        var status = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        var account = Assert.Single(status.Accounts);
        Assert.Equal(MailAccountRunPhase.NotStarted, account.Run.Phase);
        Assert.Equal(
            [Archive.Alias, Inbox.Alias],
            account.Folders.Select(static folder => folder.Alias));
        Assert.All(account.Folders, static folder =>
        {
            Assert.Null(folder.ProgressAdvancedAt);
            Assert.Null(folder.LastRun);
        });
    }

    /// <summary>A folder configuration maps and stopped mirroring is reported as exactly that, because no run schedules it.</summary>
    [Fact]
    public async Task ReadAsync_ReportsWhetherEachMappedFolderIsMirrored()
    {
        // Arrange
        var participation = StubMailFolderParticipation.Mapping(Inbox, Archive).Unmirroring(Archive);
        var reader = Reader(new MailSynchronizationRunLedger(new FakeTimeProvider(Start)), participation: participation);

        // Act
        var status = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        var folders = Assert.Single(status.Accounts).Folders;
        Assert.False(folders.Single(folder => folder.Alias == Archive.Alias).Mirrored);
        Assert.True(folders.Single(folder => folder.Alias == Inbox.Alias).Mirrored);
    }

    /// <summary>
    /// The reading this whole surface exists for. Both folders' progress stopped moving at the same instant and their
    /// last turns say why: one has nothing left to fetch, the other has been failing to get past a batch, and no single
    /// source distinguishes them.
    /// </summary>
    [Fact]
    public async Task ReadAsync_SeparatesAFolderWithNothingLeftToFetchFromOneThatKeepsFailing()
    {
        // Arrange
        var clock = new FakeTimeProvider(Start);
        var ledger = new MailSynchronizationRunLedger(clock);
        ledger.RecordFolderSynchronized(Inbox, 0, 0, 0, hasMoreEmails: false);
        ledger.RecordFolderUnsynchronized(Archive, MailFolderRunOutcome.UnexpectedFailure);
        var reader = Reader(ledger, progress: [Advanced(Inbox, 4120), Advanced(Archive, 6997)]);

        // Act
        var status = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        var folders = Assert.Single(status.Accounts).Folders;
        var idle = folders.Single(folder => folder.Alias == Inbox.Alias);
        var stalled = folders.Single(folder => folder.Alias == Archive.Alias);
        Assert.Equal(MailFolderRunOutcome.Synchronized, idle.LastRun?.Outcome);
        Assert.Equal(MailFolderRunOutcome.UnexpectedFailure, stalled.LastRun?.Outcome);
        Assert.Equal(Start, stalled.ProgressAdvancedAt);
    }

    /// <summary>How far a folder has come, which is the figure an operator watching a backfill reads.</summary>
    [Fact]
    public async Task ReadAsync_ReportsHowFarEachFoldersDurableProgressHasCome()
    {
        // Arrange
        var reader = Reader(
            new MailSynchronizationRunLedger(new FakeTimeProvider(Start)),
            progress: [Advanced(Inbox, 6997)]);

        // Act
        var status = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        var inbox = Assert.Single(status.Accounts).Folders.Single(folder => folder.Alias == Inbox.Alias);
        Assert.Equal(ImapUid.Create(6997), inbox.LastSeenUid);
        Assert.Equal(ImapUidValidity.Create(1), inbox.UidValidity);
        Assert.Equal(Start, inbox.ProgressAdvancedAt);
    }

    /// <summary>The account's own half of the answer: the phase, the wait, and the failures the wait grew from.</summary>
    [Fact]
    public async Task ReadAsync_ReportsWhatEachAccountsSupervisorIsDoing()
    {
        // Arrange
        var ledger = new MailSynchronizationRunLedger(new FakeTimeProvider(Start));
        ledger.RecordRunEnded(Work, scheduledFolderCount: 2, failedFolderCount: 2, mutationConvergenceFailed: false);
        ledger.RecordNextRunDue(Work, TimeSpan.FromMinutes(20), consecutiveFailureCount: 3);
        var reader = Reader(ledger);

        // Act
        var status = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        var run = Assert.Single(status.Accounts).Run;
        Assert.Equal(MailAccountRunPhase.WaitingForNextRun, run.Phase);
        Assert.Equal(Start + TimeSpan.FromMinutes(20), run.NextRunDueAt);
        Assert.Equal(3, run.ConsecutiveFailureCount);
        Assert.True(run.LastRun?.Failed);
    }

    /// <summary>Progress recorded for another account's alias of the same name never reaches this one.</summary>
    [Fact]
    public async Task ReadAsync_KeepsOneAccountsProgressOutOfAnotherAccountsFolderOfTheSameName()
    {
        // Arrange
        var personalInbox = new MailFolderIdentity(MailAccountId.Create("personal"), Inbox.Alias);
        var reader = Reader(
            new MailSynchronizationRunLedger(new FakeTimeProvider(Start)),
            progress: [Advanced(personalInbox, 6997)]);

        // Act
        var status = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        var inbox = Assert.Single(status.Accounts).Folders.Single(folder => folder.Alias == Inbox.Alias);
        Assert.Null(inbox.LastSeenUid);
    }

    private static MailFolderSynchronizationProgress Advanced(MailFolderIdentity folder, uint lastSeenUid) =>
        new(folder, ImapUidValidity.Create(1), ImapUid.Create(lastSeenUid), Start);

    /// <summary>Builds the reader over one account mapping two folders, which is the arrangement every test above narrows.</summary>
    private static MailSynchronizationStatusReader Reader(
        MailSynchronizationRunLedger ledger,
        bool enabled = true,
        StubMailFolderParticipation? participation = null,
        IReadOnlyList<MailFolderSynchronizationProgress>? progress = null)
    {
        var accounts = Substitute.For<IMailAccountCatalog>();
        accounts.SynchronizationEnabled.Returns(enabled);
        accounts.ServedAccounts.Returns([SyntheticServedAccount.Of(Work)]);

        var progressReader = Substitute.For<IMailFolderSynchronizationProgressReader>();
        progressReader.ReadAsync(Arg.Any<CancellationToken>()).Returns(progress ?? []);

        return new MailSynchronizationStatusReader(
            accounts,
            participation ?? StubMailFolderParticipation.Mapping(Inbox, Archive),
            ledger,
            progressReader);
    }
}
