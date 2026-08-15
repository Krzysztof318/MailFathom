// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Synchronization.Administration;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Host.Api;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers what the synchronization status route answers with, and what it never puts on the wire.</summary>
public sealed class MailboxSynchronizationStatusEndpointTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 10, 0, 0, TimeSpan.Zero);
    private static readonly MailAccountId Work = MailAccountId.Create("work");
    private static readonly MailFolderIdentity Inbox = new(Work, MailFolderAlias.Create("inbox"));

    /// <summary>
    /// The deployment's half of an agreement with a command it cannot reference. <c>mfctl</c> composes this path from a
    /// constant of its own and its suite pins the same literal, because a rename on either side compiles cleanly and
    /// leaves the command reaching a 404 that reads exactly like an endpoint nobody enabled.
    /// </summary>
    [Fact]
    public void StatusRoute_IsThePathTheCommandComposes() =>
        Assert.Equal("/mailbox/synchronization", MailboxSynchronizationStatusEndpoint.StatusRoute);

    /// <summary>A deployment that has run nothing yet is a supported state and the one whose operator is asking, so it is answered rather than refused.</summary>
    [Fact]
    public async Task ReadStatusAsync_BeforeAnyRun_AnswersWithTheAbsencesRatherThanARefusal()
    {
        // Arrange
        var reader = Reader(new MailSynchronizationRunLedger(new FakeTimeProvider(Now)));

        // Act
        var result = await MailboxSynchronizationStatusEndpoint.ReadStatusAsync(
            reader,
            TestContext.Current.CancellationToken);

        // Assert
        var account = Assert.Single(result.Value!.Accounts);
        Assert.Equal(nameof(MailAccountRunPhase.NotStarted), account.Phase);
        Assert.Null(account.LastRun);
        Assert.Null(Assert.Single(account.Folders).LastRun);
    }

    /// <summary>The whole answer, as an operator reading a stalled folder meets it.</summary>
    [Fact]
    public async Task ReadStatusAsync_ReportsThePhaseTheBackoffAndWhatEachFolderLastDid()
    {
        // Arrange
        var ledger = new MailSynchronizationRunLedger(new FakeTimeProvider(Now));
        ledger.RecordRunEnded(Work, scheduledFolderCount: 1, failedFolderCount: 1, mutationConvergenceFailed: false);
        ledger.RecordNextRunDue(Work, TimeSpan.FromMinutes(20), consecutiveFailureCount: 4);
        ledger.RecordFolderUnsynchronized(Inbox, MailFolderRunOutcome.DeferredAfterMailServerUnavailable);
        var reader = Reader(ledger, progress: [new(Inbox, ImapUidValidity.Create(3), ImapUid.Create(6997), Now)]);

        // Act
        var result = await MailboxSynchronizationStatusEndpoint.ReadStatusAsync(
            reader,
            TestContext.Current.CancellationToken);

        // Assert
        var account = Assert.Single(result.Value!.Accounts);
        Assert.Equal("work", account.Account);
        Assert.Equal(nameof(MailAccountRunPhase.WaitingForNextRun), account.Phase);
        Assert.Equal(Now + TimeSpan.FromMinutes(20), account.NextRunDueAt);
        Assert.Equal(4, account.ConsecutiveFailureCount);
        Assert.True(account.LastRun?.Failed);

        var folder = Assert.Single(account.Folders);
        Assert.Equal(Inbox.Alias.Value, folder.Alias);
        Assert.Equal(3u, folder.UidValidity);
        Assert.Equal(6997u, folder.LastSeenUid);
        Assert.Equal(Now, folder.ProgressAdvancedAt);
        Assert.Equal(
            nameof(MailFolderRunOutcome.DeferredAfterMailServerUnavailable),
            folder.LastRun?.Outcome);
    }

    /// <summary>
    /// A failure reaches the wire as its classification and nothing else. The counts are what a turn that opened its
    /// folder measured, so reporting any of them for a turn that failed would describe mail the run never reached — and
    /// the classification is deliberately the whole of what a caller learns about why.
    /// </summary>
    [Fact]
    public async Task ReadStatusAsync_ReportsAFailedFolderAsItsClassificationWithNoCounts()
    {
        // Arrange
        var ledger = new MailSynchronizationRunLedger(new FakeTimeProvider(Now));
        ledger.RecordFolderUnsynchronized(Inbox, MailFolderRunOutcome.UnexpectedFailure);
        var reader = Reader(ledger);

        // Act
        var result = await MailboxSynchronizationStatusEndpoint.ReadStatusAsync(
            reader,
            TestContext.Current.CancellationToken);

        // Assert
        var lastRun = Assert.Single(Assert.Single(result.Value!.Accounts).Folders).LastRun;
        Assert.Equal(
            new MailFolderRunResponse(nameof(MailFolderRunOutcome.UnexpectedFailure), Now, 0, 0, 0, HasMoreEmails: false),
            lastRun);
    }

    private static MailSynchronizationStatusReader Reader(
        MailSynchronizationRunLedger ledger,
        IReadOnlyList<MailFolderSynchronizationProgress>? progress = null)
    {
        var accounts = Substitute.For<IMailAccountCatalog>();
        accounts.SynchronizationEnabled.Returns(true);
        accounts.ServedAccounts.Returns([SyntheticServedAccount.Of(Work)]);

        var progressReader = Substitute.For<IMailFolderSynchronizationProgressReader>();
        progressReader.ReadAsync(Arg.Any<CancellationToken>()).Returns(progress ?? []);

        return new MailSynchronizationStatusReader(
            accounts,
            StubMailFolderParticipation.Mapping(Inbox),
            ledger,
            progressReader);
    }
}
