// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.Emails.AttachmentText.Administration;
using MailFathom.Application.Synchronization.Administration;
using MailFathom.Domain.Access;
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
    /// <summary>The grant is the authority here rather than at the transport, so an entrypoint that passed no filter meets the same refusal.</summary>
    [Fact]
    public async Task ReadAsync_ACallerGrantedOnlyTheAdministrativeOperate_IsRefusedWithTheTransportAbsent()
    {
        // Arrange
        var reader = Reader(
            new MailSynchronizationRunLedger(new FakeTimeProvider(Start)),
            authorization: AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminOperate));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            reader.ReadAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminRead, refusal.RequiredPermission);
    }

    /// <summary>
    /// Attachment coverage is counted per account rather than once for the deployment, because that is the scope this
    /// answer is read at: an operator asking why one mailbox is not searchable is told about that mailbox. So what is
    /// asserted is both that the figure reaches the account it was read for and that a read happened under each
    /// account's own identity — a reader passed the wrong identity would answer plausibly and report somebody else.
    /// </summary>
    [Fact]
    public async Task ReadAsync_SeveralServedAccounts_ReadsAttachmentCoverageUnderEachAccountsOwnIdentity()
    {
        // Arrange
        var personal = MailAccountId.Create("personal");
        var coverage = new InMemoryAttachmentDerivationCoverageReader
        {
            Coverage = AttachmentDerivationCoverage.Nothing with
            {
                EmailsWithAttachmentCount = 40,
                ReadEmailCount = 25,
                DescribedImageCount = 7,
            },
        };

        var reader = Reader(
            new MailSynchronizationRunLedger(new FakeTimeProvider(Start)),
            attachmentCoverage: coverage,
            served: [Work, personal]);

        // Act
        var status = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [Work, personal],
            status.Accounts.Select(account => account.AccountId));
        Assert.All(
            status.Accounts,
            account =>
            {
                Assert.Equal(40, account.AttachmentText.EmailsWithAttachmentCount);
                Assert.Equal(25, account.AttachmentText.ReadEmailCount);
                Assert.Equal(7, account.AttachmentText.DescribedImageCount);
            });
        MailAccountId[] scopedTo = [.. coverage.Reads.Select(read => read!.Value.Id)];
        Assert.Equal([Work, personal], scopedTo);
    }

    private static MailSynchronizationStatusReader Reader(
        MailSynchronizationRunLedger ledger,
        bool enabled = true,
        StubMailFolderParticipation? participation = null,
        IReadOnlyList<MailFolderSynchronizationProgress>? progress = null,
        AccessAuthorization? authorization = null,
        InMemoryAttachmentDerivationCoverageReader? attachmentCoverage = null,
        IReadOnlyList<MailAccountId>? served = null)
    {
        var accounts = Substitute.For<IDeploymentMailAccountCatalog>();
        accounts.SynchronizationEnabled.Returns(enabled);
        accounts.ServedAccounts.Returns(
            [.. (served ?? [Work]).Select(account => SyntheticServedAccount.Of(account))]);

        var progressReader = Substitute.For<IMailFolderSynchronizationProgressReader>();
        progressReader.ReadAsync(Arg.Any<CancellationToken>()).Returns(progress ?? []);

        return new MailSynchronizationStatusReader(
            accounts,
            participation ?? StubMailFolderParticipation.Mapping(Inbox, Archive),
            ledger,
            progressReader,
            attachmentCoverage ?? new InMemoryAttachmentDerivationCoverageReader(),
            authorization ?? AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminRead));
    }
}
