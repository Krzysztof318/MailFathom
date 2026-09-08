// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Discovery.Runs;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Retrieval;
using MailFathom.Application.Synchronization.Administration;
using MailFathom.Application.Synchronization.Checkpoints;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Discovery.Runs;

/// <summary>Covers what a run reports having drawn on: which accounts, how far, and how current each copy was.</summary>
public sealed class DiscoveryCoverageReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    private static readonly MailAccountId Work = MailAccountId.Create("work");

    private static readonly MailAccountId Archive = MailAccountId.Create("archive");

    private static readonly MailFolderAlias Inbox = MailFolderAlias.Create("INBOX");

    /// <summary>An account that answered nothing may be the reason the question is unanswered, so it is reported too.</summary>
    [Fact]
    public async Task ReadAsync_AnAccountTheRunFoundNothingIn_IsStillReported()
    {
        // Arrange
        var reader = ReaderOver(
            [Folder(Work, Now.AddHours(-1)), Folder(Archive, Now.AddDays(-3))],
            new MailSynchronizationRunLedger(new FakeTimeProvider(Now)));

        // Act
        var coverage = await reader.ReadAsync(Scope(), [], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["archive", "work"], coverage.Select(account => account.Account.Value));
        Assert.All(coverage, account => Assert.Null(account.EarliestReceivedAt));
    }

    /// <summary>The dates bound what the run actually drew on rather than what the mailbox holds.</summary>
    [Fact]
    public async Task ReadAsync_MailTheRunDrewOn_ReportsTheOldestAndTheNewestOfIt()
    {
        // Arrange
        var reader = ReaderOver(
            [Folder(Work, Now.AddHours(-1))],
            new MailSynchronizationRunLedger(new FakeTimeProvider(Now)));
        var passages = new[]
        {
            Passage(Work, Now.AddDays(-30)),
            Passage(Work, Now.AddDays(-2)),
            Passage(Work, receivedAt: null),
        };

        // Act
        var coverage = await reader.ReadAsync(Scope(Work), passages, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(Now.AddDays(-30), coverage[0].EarliestReceivedAt);
        Assert.Equal(Now.AddDays(-2), coverage[0].LatestReceivedAt);
    }

    /// <summary>An account is as current as its most recently reconciled folder, which is when the mailbox last took anything in.</summary>
    [Fact]
    public async Task ReadAsync_AnAccountNothingIsBehindOn_ReportsTheNewestOfItsFolders()
    {
        // Arrange
        var reader = ReaderOver(
            [
                Folder(Work, Now.AddDays(-9), "ARCHIVE"),
                Folder(Work, Now.AddHours(-1)),
            ],
            new MailSynchronizationRunLedger(new FakeTimeProvider(Now)));

        // Act
        var coverage = await reader.ReadAsync(Scope(Work), [], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(PresentationStaleness.Current, coverage[0].Freshness.Staleness);
        Assert.Equal(Now.AddHours(-1), coverage[0].Freshness.ObservedAt);
    }

    /// <summary>A folder whose last turn left mail behind is a copy known to be behind rather than one that might be.</summary>
    [Fact]
    public async Task ReadAsync_AFolderWhoseLastTurnLeftMailBehind_ReportsTheAccountAsBehind()
    {
        // Arrange
        var ledger = new MailSynchronizationRunLedger(new FakeTimeProvider(Now));
        ledger.RecordFolderSynchronized(
            new MailFolderIdentity(Work, Inbox),
            storedEmailCount: 40,
            skippedOversizedEmailCount: 0,
            unreadableMimeEmailCount: 0,
            hasMoreEmails: true);
        var reader = ReaderOver([Folder(Work, Now.AddHours(-1))], ledger);

        // Act
        var coverage = await reader.ReadAsync(Scope(Work), [], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(PresentationStaleness.Stale, coverage[0].Freshness.Staleness);
    }

    /// <summary>An account whose own run ended with a folder it could not finish is behind whatever its folders last committed.</summary>
    [Fact]
    public async Task ReadAsync_AnAccountWhoseOwnRunFailed_ReportsTheAccountAsBehind()
    {
        // Arrange
        var ledger = new MailSynchronizationRunLedger(new FakeTimeProvider(Now));
        ledger.RecordRunEnded(
            Work,
            scheduledFolderCount: 3,
            failedFolderCount: 1,
            mutationConvergenceFailed: false);
        var reader = ReaderOver([Folder(Work, Now.AddHours(-1))], ledger);

        // Act
        var coverage = await reader.ReadAsync(Scope(Work), [], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(PresentationStaleness.Stale, coverage[0].Freshness.Staleness);
    }

    /// <summary>A mailbox the scope reached that local state holds no folder of is reported rather than passed over in silence.</summary>
    [Fact]
    public async Task ReadAsync_AnAccountInScopeWithNoFolderInLocalState_IsReportedWithUnknownFreshness()
    {
        // Arrange
        var reader = ReaderOver(
            [Folder(Work, Now.AddHours(-1))],
            new MailSynchronizationRunLedger(new FakeTimeProvider(Now)));

        // Act
        var coverage = await reader.ReadAsync(Scope(), [], TestContext.Current.CancellationToken);

        // Assert
        var archive = Assert.Single(coverage, account => account.Account.Value == "archive");
        Assert.Equal(PresentationStaleness.Unknown, archive.Freshness.Staleness);
    }

    /// <summary>An account nothing has reconciled is unknown rather than behind, because no elapsed time is read as staleness here.</summary>
    [Fact]
    public async Task ReadAsync_AnAccountSynchronizationHasNeverCommittedProgressFor_ReportsItsFreshnessAsUnknown()
    {
        // Arrange
        var reader = ReaderOver(
            [Folder(Work, synchronizedAt: null)],
            new MailSynchronizationRunLedger(new FakeTimeProvider(Now)));

        // Act
        var coverage = await reader.ReadAsync(Scope(Work), [], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(PresentationStaleness.Unknown, coverage[0].Freshness.Staleness);
    }

    /// <summary>A deployment serving more mailboxes than a plan may report produces a plan rather than a refusal.</summary>
    [Fact]
    public async Task ReadAsync_MoreAccountsThanAPlanMayReport_ReportsAsManyAsItMay()
    {
        // Arrange
        var folders = Enumerable
            .Range(0, PresentationPlan.MaxAccountsCovered + 4)
            .Select(index => Folder(MailAccountId.Create($"account-{index:00}"), Now))
            .ToArray();
        var reader = ReaderOver(folders, new MailSynchronizationRunLedger(new FakeTimeProvider(Now)));

        // Act
        var coverage = await reader.ReadAsync(Scope(), [], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(PresentationPlan.MaxAccountsCovered, coverage.Count);
    }

    private static DiscoveryCoverageReader ReaderOver(
        IReadOnlyList<MailboxFolderFreshness> folders,
        MailSynchronizationRunLedger ledger)
    {
        var freshnessReader = Substitute.For<ISynchronizationFreshnessReader>();
        freshnessReader.ReadAsync(Arg.Any<MailboxScope>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(folders));

        return new DiscoveryCoverageReader(freshnessReader, ledger);
    }

    private static MailboxFolderFreshness Folder(
        MailAccountId accountId,
        DateTimeOffset? synchronizedAt,
        string folderAlias = "INBOX") =>
        new(accountId, MailFolderAlias.Create(folderAlias), synchronizedAt);

    private static EmailKnowledgePassage Passage(MailAccountId accountId, DateTimeOffset? receivedAt) =>
        ScriptedEmailKnowledgeSearch.Passage("an extract") with
        {
            AccountId = accountId,
            ReceivedAt = receivedAt,
        };

    private static MailboxScope Scope(params MailAccountId[] accountIds) =>
        MailboxScope.Create(
            SyntheticMailUser.Deployment,
            accountIds.Length is 0 ? [Work, Archive] : accountIds,
            []);
}
