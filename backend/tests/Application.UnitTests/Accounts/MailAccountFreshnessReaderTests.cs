// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Synchronization.Administration;
using MailFathom.Application.Synchronization.Checkpoints;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Accounts;

/// <summary>Covers the reading a client draws its mailbox list from: whose accounts, and whether each is current.</summary>
/// <remarks>
/// The two facts are asserted apart from each other throughout, because that separation is the point of the use case.
/// A stale copy and a failing account carry the same instant, and a client that could not tell them apart would either
/// alarm about a quiet mailbox or say nothing about a broken one.
/// </remarks>
public sealed class MailAccountFreshnessReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 10, 0, 0, TimeSpan.Zero);

    private static readonly ServedMailAccount Work = SyntheticServedAccount.Of("work");

    private static readonly ServedMailAccount Private = SyntheticServedAccount.Of("private");

    /// <summary>The accounts are the owner's own, published under the names configuration gave them.</summary>
    [Fact]
    public async Task ReadAsync_AnOwnerOwningTwoAccounts_PublishesBothUnderTheirConfiguredNames()
    {
        // Arrange
        var reader = ReaderOver(Freshness(), OwningAccounts(Work, Private));

        // Act
        var directory = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([Work, Private], directory.Accounts.Select(account => account.Account));
    }

    /// <summary>
    /// The scoping is the catalog's, and this is what it looks like from here: an owner owning nothing reads an empty
    /// collection, which is what an account belonging to somebody else looks like too.
    /// </summary>
    [Fact]
    public async Task ReadAsync_AnOwnerOwningNoAccount_PublishesAnEmptyCollectionRatherThanAnError()
    {
        // Arrange
        var reader = ReaderOver(Freshness(), OwningAccounts());

        // Act
        var directory = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(directory.Accounts);
    }

    /// <summary>An owner reads only what the caller-scoped catalog answered, so nothing here reaches the deployment's own set.</summary>
    [Fact]
    public async Task ReadAsync_AnAccountAnotherOwnerHolds_IsAbsentBecauseTheCatalogNeverNamedIt()
    {
        // Arrange
        var reader = ReaderOver(
            Freshness((Private.Id, "inbox", Now)),
            OwningAccounts(Work));

        // Act
        var directory = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([Work.Id], directory.Accounts.Select(account => account.Account.Id));
    }

    /// <summary>The instant is the newest of the account's folders, so a folder that has been empty since it was mapped does not age the account.</summary>
    [Fact]
    public async Task ReadAsync_AnAccountWithSeveralFolders_ReportsTheNewestProgressAsItsOwn()
    {
        // Arrange
        var reader = ReaderOver(
            Freshness(
                (Work.Id, "archive", Now - TimeSpan.FromDays(30)),
                (Work.Id, "inbox", Now),
                (Work.Id, "junk", null)),
            OwningAccounts(Work));

        // Act
        var directory = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(Now, Assert.Single(directory.Accounts).LastSynchronizedAt);
    }

    /// <summary>An account whose folders have all committed progress and whose last run did not fail is current as far as anything here knows.</summary>
    [Fact]
    public async Task ReadAsync_AnAccountWhoseLastRunSucceeded_IsSynchronized()
    {
        // Arrange
        var ledger = Ledger();
        ledger.RecordRunEnded(Work.Id, scheduledFolderCount: 2, failedFolderCount: 0, mutationConvergenceFailed: false);
        var reader = ReaderOver(Freshness((Work.Id, "inbox", Now)), OwningAccounts(Work), ledger);

        // Act
        var directory = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        var account = Assert.Single(directory.Accounts);
        Assert.Equal(MailSynchronizationState.Synchronized, account.State);
        Assert.Equal(Now, account.LastSynchronizedAt);
    }

    /// <summary>
    /// The user story this route exists for: one failing account among several is named, and it keeps the instant its
    /// copy last moved so a reader can see how far behind the failure has left it.
    /// </summary>
    [Theory]
    [InlineData(1, false)]
    [InlineData(0, true)]
    public async Task ReadAsync_AnAccountWhoseLastRunFailed_IsFailingAndKeepsItsLastInstant(
        int failedFolderCount,
        bool mutationConvergenceFailed)
    {
        // Arrange
        var ledger = Ledger();
        ledger.RecordRunEnded(Work.Id, scheduledFolderCount: 2, failedFolderCount, mutationConvergenceFailed);
        var reader = ReaderOver(
            Freshness((Work.Id, "inbox", Now), (Private.Id, "inbox", Now)),
            OwningAccounts(Work, Private),
            ledger);

        // Act
        var directory = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        var failing = directory.Accounts.Single(account => account.Account == Work);
        Assert.Equal(MailSynchronizationState.Failing, failing.State);
        Assert.Equal(Now, failing.LastSynchronizedAt);
        Assert.Equal(
            MailSynchronizationState.Synchronized,
            directory.Accounts.Single(account => account.Account == Private).State);
    }

    /// <summary>An account nothing has ever committed progress for is a different state from one that is merely behind, and it carries no instant.</summary>
    [Fact]
    public async Task ReadAsync_AnAccountNoRunHasEverReached_IsNeverSynchronizedAndCarriesNoInstant()
    {
        // Arrange
        var reader = ReaderOver(Freshness((Work.Id, "inbox", null)), OwningAccounts(Work));

        // Act
        var directory = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        var account = Assert.Single(directory.Accounts);
        Assert.Equal(MailSynchronizationState.NeverSynchronized, account.State);
        Assert.Null(account.LastSynchronizedAt);
    }

    /// <summary>A failing account that has never synchronized is reported as failing, because that is the fact worth acting on.</summary>
    [Fact]
    public async Task ReadAsync_AnAccountThatFailedBeforeItEverSynchronized_IsFailing()
    {
        // Arrange
        var ledger = Ledger();
        ledger.RecordRunEnded(Work.Id, scheduledFolderCount: 1, failedFolderCount: 1, mutationConvergenceFailed: false);
        var reader = ReaderOver(Freshness(), OwningAccounts(Work), ledger);

        // Act
        var directory = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        var account = Assert.Single(directory.Accounts);
        Assert.Equal(MailSynchronizationState.Failing, account.State);
        Assert.Null(account.LastSynchronizedAt);
    }

    /// <summary>A process that has run nothing yet reports what its stored progress says, rather than inventing a failure it has not seen.</summary>
    [Fact]
    public async Task ReadAsync_BeforeThisProcessHasRunTheAccount_ReportsWhatTheStoredProgressSays()
    {
        // Arrange
        var reader = ReaderOver(Freshness((Work.Id, "inbox", Now)), OwningAccounts(Work));

        // Act
        var directory = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailSynchronizationState.Synchronized, Assert.Single(directory.Accounts).State);
    }

    /// <summary>
    /// The acceptance this route was extended for: a mailbox whose server is not answering is named as that rather than
    /// as broken, because the two ask different things of whoever reads them.
    /// </summary>
    [Fact]
    public async Task ReadAsync_AnAccountWhoseFoldersFoundNoMailServer_IsUnreachableRatherThanFailing()
    {
        // Arrange
        var ledger = Ledger();
        ledger.RecordFolderUnsynchronized(
            Folder(Work.Id, "inbox"),
            MailFolderRunOutcome.DeferredAfterMailServerUnavailable);
        ledger.RecordRunEnded(Work.Id, scheduledFolderCount: 1, failedFolderCount: 1, mutationConvergenceFailed: false);
        var reader = ReaderOver(Freshness((Work.Id, "inbox", Now)), OwningAccounts(Work), ledger);

        // Act
        var directory = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        var account = Assert.Single(directory.Accounts);
        Assert.Equal(MailSynchronizationState.Unreachable, account.State);
        Assert.Equal(Now, account.LastSynchronizedAt);
    }

    /// <summary>
    /// A run that also failed some other way reached the server, so the account is failing rather than unreachable — the
    /// unreachable folder is still named as one, because that is the folder's own answer.
    /// </summary>
    [Fact]
    public async Task ReadAsync_AnAccountFailingOneFolderAndUnableToReachAnother_IsFailingAndKeepsBothFolderReadings()
    {
        // Arrange
        var ledger = Ledger();
        ledger.RecordFolderUnsynchronized(
            Folder(Work.Id, "archive"),
            MailFolderRunOutcome.DeferredAfterMailServerUnavailable);
        ledger.RecordFolderUnsynchronized(Folder(Work.Id, "inbox"), MailFolderRunOutcome.AliasUnresolved);
        ledger.RecordRunEnded(Work.Id, scheduledFolderCount: 2, failedFolderCount: 2, mutationConvergenceFailed: false);
        var reader = ReaderOver(
            Freshness((Work.Id, "archive", Now), (Work.Id, "inbox", Now)),
            OwningAccounts(Work),
            ledger);

        // Act
        var directory = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        var account = Assert.Single(directory.Accounts);
        Assert.Equal(MailSynchronizationState.Failing, account.State);
        Assert.Equal(
            [MailSynchronizationState.Unreachable, MailSynchronizationState.Failing],
            account.Folders.Select(folder => folder.State));
    }

    /// <summary>Every way a folder's turn can fail reaches the folder as a failure, so a screen never has to read an outcome.</summary>
    [Theory]
    [InlineData(MailFolderRunOutcome.AliasUnresolved)]
    [InlineData(MailFolderRunOutcome.AliasAmbiguous)]
    [InlineData(MailFolderRunOutcome.DeferredAfterConcurrencyConflict)]
    [InlineData(MailFolderRunOutcome.UnexpectedFailure)]
    public async Task ReadAsync_AFolderWhoseTurnDidNotComplete_IsFailing(MailFolderRunOutcome outcome)
    {
        // Arrange
        var ledger = Ledger();
        ledger.RecordFolderUnsynchronized(Folder(Work.Id, "inbox"), outcome);
        var reader = ReaderOver(Freshness((Work.Id, "inbox", Now)), OwningAccounts(Work), ledger);

        // Act
        var directory = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        var folder = Assert.Single(Assert.Single(directory.Accounts).Folders);
        Assert.Equal(MailSynchronizationState.Failing, folder.State);
        Assert.Equal(Now, folder.SynchronizedAt);
    }

    /// <summary>
    /// A host shutting down is not a failure — the supervisor counts it under none, so an account backed off for every
    /// restart would be one approached less often for being stopped — and the folder reports what its progress says.
    /// </summary>
    [Fact]
    public async Task ReadAsync_AFolderInterruptedByShutdown_ReportsWhatItsStoredProgressSays()
    {
        // Arrange
        var ledger = Ledger();
        ledger.RecordFolderUnsynchronized(Folder(Work.Id, "inbox"), MailFolderRunOutcome.InterruptedByShutdown);
        var reader = ReaderOver(Freshness((Work.Id, "inbox", Now)), OwningAccounts(Work), ledger);

        // Act
        var directory = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        var account = Assert.Single(directory.Accounts);
        Assert.Equal(MailSynchronizationState.Synchronized, account.State);
        Assert.Equal(MailSynchronizationState.Synchronized, Assert.Single(account.Folders).State);
    }

    /// <summary>
    /// Being behind is not a state, because a turn that succeeded within its batch budget leaves mail for the next one:
    /// the folder says so beside its state, and the account says so because one of its folders did.
    /// </summary>
    [Fact]
    public async Task ReadAsync_AFolderWhoseTurnLeftMailBehind_SaysSoOnTheFolderAndOnTheAccount()
    {
        // Arrange
        var ledger = Ledger();
        ledger.RecordFolderSynchronized(
            Folder(Work.Id, "inbox"),
            storedEmailCount: 200,
            skippedOversizedEmailCount: 0,
            unreadableMimeEmailCount: 0,
            hasMoreEmails: true);
        ledger.RecordFolderSynchronized(
            Folder(Work.Id, "archive"),
            storedEmailCount: 3,
            skippedOversizedEmailCount: 0,
            unreadableMimeEmailCount: 0,
            hasMoreEmails: false);
        var reader = ReaderOver(
            Freshness((Work.Id, "archive", Now), (Work.Id, "inbox", Now)),
            OwningAccounts(Work),
            ledger);

        // Act
        var directory = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        var account = Assert.Single(directory.Accounts);
        Assert.Equal(MailSynchronizationState.Synchronized, account.State);
        Assert.True(account.IsBehind);
        Assert.Equal([false, true], account.Folders.Select(folder => folder.IsBehind));
    }

    /// <summary>A folder no run of this process has reached is read from its own stored progress, one entry per folder the directory knows of.</summary>
    [Fact]
    public async Task ReadAsync_FoldersThisProcessHasNotRun_PublishesOneReadingPerFolderFromItsProgress()
    {
        // Arrange
        var reader = ReaderOver(
            Freshness((Work.Id, "archive", null), (Work.Id, "inbox", Now)),
            OwningAccounts(Work));

        // Act
        var directory = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        var account = Assert.Single(directory.Accounts);
        Assert.Equal(
            [MailSynchronizationState.NeverSynchronized, MailSynchronizationState.Synchronized],
            account.Folders.Select(folder => folder.State));
        Assert.Equal([null, Now], account.Folders.Select(folder => folder.SynchronizedAt));
        Assert.False(account.IsBehind);
    }

    /// <summary>The deployment's own switch is answered beside the accounts, because a stale copy on a deployment that stopped trying is a different fact.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReadAsync_ADeploymentThatSwitchedSynchronizationOff_StillPublishesItsAccountsAndSaysSo(
        bool synchronizationEnabled)
    {
        // Arrange
        var reader = ReaderOver(Freshness(), OwningAccounts(synchronizationEnabled, Work));

        // Act
        var directory = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(synchronizationEnabled, directory.SynchronizationEnabled);
        Assert.Single(directory.Accounts);
    }

    /// <summary>
    /// Naming the accounts an owner has is publishing that they exist, so a credential without the mailbox grant is
    /// refused rather than answered with the empty collection an owner owning nothing receives.
    /// </summary>
    [Fact]
    public async Task ReadAsync_ACallerWithoutTheMailboxGrant_IsRefusedRatherThanAnsweredWithNothing()
    {
        // Arrange
        var reader = ReaderOver(
            Freshness(),
            OwningAccounts(Work),
            Ledger(),
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailAsk));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            reader.ReadAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.MailRead, refusal.RequiredPermission);
    }

    /// <summary>Work no caller requested is a distinct kind of principal rather than a caller holding everything.</summary>
    [Fact]
    public async Task ReadAsync_TheProcessIdentity_IsRefused()
    {
        // Arrange
        var reader = ReaderOver(
            Freshness(),
            OwningAccounts(Work),
            Ledger(),
            AccessAuthorizations.ForPrincipal(AuthorizedPrincipal.Process));

        // Act, Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            reader.ReadAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>Names one folder of one account, which is the pair the run ledger records a turn against.</summary>
    private static MailFolderIdentity Folder(MailAccountId accountId, string folderAlias) =>
        new(accountId, MailFolderAlias.Create(folderAlias));

    private static MailSynchronizationRunLedger Ledger() => new(new FakeTimeProvider(Now));

    /// <summary>Answers the accounts the caller's owner owns, and whether the deployment refreshes them.</summary>
    private static ICallerMailAccountCatalog OwningAccounts(params ServedMailAccount[] ownedAccounts) =>
        OwningAccounts(synchronizationEnabled: true, ownedAccounts);

    private static ICallerMailAccountCatalog OwningAccounts(
        bool synchronizationEnabled,
        params ServedMailAccount[] ownedAccounts)
    {
        var catalog = Substitute.For<ICallerMailAccountCatalog>();
        catalog.SynchronizationEnabled.Returns(synchronizationEnabled);
        catalog.OwnedAccounts.Returns([.. ownedAccounts]);

        return catalog;
    }

    private static MailAccountFreshnessReader ReaderOver(
        ISynchronizationFreshnessReader freshnessReader,
        ICallerMailAccountCatalog catalog,
        MailSynchronizationRunLedger? runLedger = null) =>
        ReaderOver(
            freshnessReader,
            catalog,
            runLedger ?? Ledger(),
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead));

    private static MailAccountFreshnessReader ReaderOver(
        ISynchronizationFreshnessReader freshnessReader,
        ICallerMailAccountCatalog catalog,
        MailSynchronizationRunLedger runLedger,
        AccessAuthorization authorization) =>
        new(
            new MailAccountDirectoryReader(
                catalog,
                freshnessReader,
                new MailboxScopeResolver(
                    catalog,
                    StubMailFolderParticipation.Nothing,
                    StubJunkMailFolderCatalog.None,
                    StubMailFolderMappings.ResolvingNothing),
                new RecordingMailboxReadTelemetry(),
                authorization),
            runLedger);

    /// <summary>Answers the folders local state knows of, which is what the account-level instant is reduced from.</summary>
    private static ISynchronizationFreshnessReader Freshness(
        params (MailAccountId AccountId, string FolderAlias, DateTimeOffset? SynchronizedAt)[] folders)
    {
        var freshnessReader = Substitute.For<ISynchronizationFreshnessReader>();
        freshnessReader
            .ReadAsync(Arg.Any<MailboxScope>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            [
                .. folders.Select(folder => new MailboxFolderFreshness(
                    folder.AccountId,
                    MailFolderAlias.Create(folder.FolderAlias),
                    folder.SynchronizedAt)),
            ]);

        return freshnessReader;
    }
}
