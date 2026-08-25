// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Folders;
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

namespace MailFathom.Application.UnitTests.Folders;

/// <summary>Covers the one read a mail screen draws its tree from: which mailboxes, which folders, and how current each is.</summary>
/// <remarks>
/// Whose accounts these are, what a state means, and what a caller without the grant is answered are decided in the
/// reading this composes and are covered there. What is asserted here is what only this use case decides: which folders
/// a tree holds, where each of them sits on its mail server, what role it plays, and how much mail is in it.
/// </remarks>
public sealed class MailFolderDirectoryReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 10, 0, 0, TimeSpan.Zero);

    private static readonly ServedMailAccount Work = SyntheticServedAccount.Of("work");

    private static readonly ServedMailAccount Private = SyntheticServedAccount.Of("private");

    /// <summary>The user story: several mailboxes and their folders arrive as one tree rather than as one request per mailbox.</summary>
    [Fact]
    public async Task ReadAsync_AnOwnerWithSeveralMailboxes_AnswersEveryOneOfThemWithItsFoldersInOneRead()
    {
        // Arrange
        var reader = ReaderOver(
            Freshness((Work.Id, "inbox", Now), (Private.Id, "inbox", Now)),
            OwningAccounts(Work, Private),
            StoredFolders(
                Stored(Work.Id, "inbox", "INBOX", storedEmailCount: 12, unreadEmailCount: 3),
                Stored(Private.Id, "inbox", "INBOX", storedEmailCount: 4, unreadEmailCount: 0)));

        // Act
        var directory = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([Work, Private], directory.Accounts.Select(account => account.Account.Account));
        Assert.Equal([1, 1], directory.Accounts.Select(account => account.Folders.Count));
    }

    /// <summary>The counts are what the store answered, and they reach the folder they were counted for.</summary>
    [Fact]
    public async Task ReadAsync_AFolderHoldingMail_CarriesWhatIsStoredAndWhatIsUnread()
    {
        // Arrange
        var reader = ReaderOver(
            Freshness((Work.Id, "inbox", Now)),
            OwningAccounts(Work),
            StoredFolders(Stored(Work.Id, "inbox", "INBOX", storedEmailCount: 12, unreadEmailCount: 3)));

        // Act
        var directory = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        var folder = Assert.Single(Assert.Single(directory.Accounts).Folders);
        Assert.Equal(12, folder.StoredEmailCount);
        Assert.Equal(3, folder.UnreadEmailCount);
    }

    /// <summary>
    /// The story a client cannot serve itself: which folder is the sent one is the service's answer, because a server
    /// advertises the role by attribute and names the folder in whatever language it likes.
    /// </summary>
    [Fact]
    public async Task ReadAsync_AFolderConfigurationLabelledWithARole_PublishesTheRoleRatherThanLeavingItToBeGuessed()
    {
        // Arrange
        var mappings = new StubMailFolderMappings()
            .With(Work.Id, MailFolderMapping.ToSpecialUse(MailFolderAlias.Create("sent"), MailFolderSpecialUse.Sent))
            .With(Work.Id, MailFolderMapping.ToRemotePath(MailFolderAlias.Create("2024"), RemoteFolderPath.Create("Archiwum.2024")));
        var reader = ReaderOver(
            Freshness((Work.Id, "2024", Now), (Work.Id, "sent", Now)),
            OwningAccounts(Work),
            StoredFolders(
                Stored(Work.Id, "2024", "Archiwum.2024", storedEmailCount: 0, unreadEmailCount: 0, delimiter: '.'),
                Stored(Work.Id, "sent", "Gesendete Objekte", storedEmailCount: 2, unreadEmailCount: 0)),
            mappings);

        // Act
        var directory = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [null, MailFolderSpecialUse.Sent],
            Assert.Single(directory.Accounts).Folders.Select(folder => folder.Role));
    }

    /// <summary>The hierarchy is the server's own path split into its levels, so a client builds a tree without knowing a delimiter exists.</summary>
    [Fact]
    public async Task ReadAsync_AFolderNestedOnItsServer_PublishesItsLevelsOutermostFirst()
    {
        // Arrange
        var reader = ReaderOver(
            Freshness((Work.Id, "2024", Now)),
            OwningAccounts(Work),
            StoredFolders(
                Stored(Work.Id, "2024", "Archiwum.Praca.2024", storedEmailCount: 0, unreadEmailCount: 0, delimiter: '.')));

        // Act
        var directory = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            ["Archiwum", "Praca", "2024"],
            Assert.Single(Assert.Single(directory.Accounts).Folders).HierarchyLevels);
    }

    /// <summary>A server that reports no delimiter has a flat mailbox, and the whole path is the one level a tree draws.</summary>
    [Fact]
    public async Task ReadAsync_AFolderOnAServerReportingNoDelimiter_PublishesItsWholePathAsOneLevel()
    {
        // Arrange
        var reader = ReaderOver(
            Freshness((Work.Id, "inbox", Now)),
            OwningAccounts(Work),
            StoredFolders(Stored(Work.Id, "inbox", "INBOX", storedEmailCount: 1, unreadEmailCount: 1)));

        // Act
        var directory = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["INBOX"], Assert.Single(Assert.Single(directory.Accounts).Folders).HierarchyLevels);
    }

    /// <summary>
    /// An alias nothing has bound to a remote folder yet has no place on a server and no mail, and it is answered as
    /// exactly that rather than left out — its own freshness is what says an empty folder from an unsynchronized one.
    /// </summary>
    [Fact]
    public async Task ReadAsync_AFolderTheStoreHoldsNoBindingFor_CarriesNoHierarchyAndNoCounts()
    {
        // Arrange
        var reader = ReaderOver(
            Freshness((Work.Id, "inbox", null)),
            OwningAccounts(Work),
            StoredFolders());

        // Act
        var directory = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        var folder = Assert.Single(Assert.Single(directory.Accounts).Folders);
        Assert.Empty(folder.HierarchyLevels);
        Assert.Equal(0, folder.StoredEmailCount);
        Assert.Equal(0, folder.UnreadEmailCount);
        Assert.Equal(MailSynchronizationState.NeverSynchronized, folder.Freshness.State);
    }

    /// <summary>The folder's freshness is the composed reading's own, so the tree and the mailbox list beside it cannot disagree.</summary>
    [Fact]
    public async Task ReadAsync_AFolderWhoseTurnFailed_CarriesTheSameReadingTheMailboxListWouldGive()
    {
        // Arrange
        var ledger = Ledger();
        ledger.RecordFolderUnsynchronized(
            new MailFolderIdentity(Work.Id, MailFolderAlias.Create("inbox")),
            MailFolderRunOutcome.DeferredAfterMailServerUnavailable);
        var reader = ReaderOver(
            Freshness((Work.Id, "inbox", Now)),
            OwningAccounts(Work),
            StoredFolders(Stored(Work.Id, "inbox", "INBOX", storedEmailCount: 12, unreadEmailCount: 3)),
            StubMailFolderMappings.Nothing,
            ledger);

        // Act
        var directory = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        var account = Assert.Single(directory.Accounts);
        Assert.Equal(MailSynchronizationState.Unreachable, account.Account.State);
        Assert.Equal(MailSynchronizationState.Unreachable, Assert.Single(account.Folders).Freshness.State);
    }

    /// <summary>An owner who owns no account reads an empty tree, and nothing counts mail on their behalf.</summary>
    [Fact]
    public async Task ReadAsync_AnOwnerOwningNoAccount_AnswersAnEmptyTreeWithoutCountingAnything()
    {
        // Arrange
        var storedFolders = StoredFolders();
        var reader = ReaderOver(Freshness(), OwningAccounts(), storedFolders);

        // Act
        var directory = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(directory.Accounts);
        await storedFolders.DidNotReceive().ReadAsync(Arg.Any<MailboxScope>(), Arg.Any<CancellationToken>());
    }

    /// <summary>The deployment's own switch travels with the tree, because no per-folder value says whether anything is still refreshing it.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReadAsync_ADeploymentThatSwitchedSynchronizationOff_SaysSoBesideTheTree(bool synchronizationEnabled)
    {
        // Arrange
        var reader = ReaderOver(
            Freshness((Work.Id, "inbox", Now)),
            OwningAccounts(synchronizationEnabled, Work),
            StoredFolders(Stored(Work.Id, "inbox", "INBOX", storedEmailCount: 1, unreadEmailCount: 0)));

        // Act
        var directory = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(synchronizationEnabled, directory.SynchronizationEnabled);
    }

    /// <summary>
    /// Naming an owner's folders is the same disclosure as naming their mailboxes, so a credential without the mailbox
    /// grant is refused here exactly as it is there, and before anything is counted.
    /// </summary>
    [Fact]
    public async Task ReadAsync_ACallerWithoutTheMailboxGrant_IsRefusedRatherThanAnsweredWithAnEmptyTree()
    {
        // Arrange
        var reader = ReaderOver(
            Freshness(),
            OwningAccounts(Work),
            StoredFolders(),
            StubMailFolderMappings.Nothing,
            Ledger(),
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailAsk));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            reader.ReadAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.MailRead, refusal.RequiredPermission);
    }

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

    /// <summary>Builds one entry of what local state holds about a folder.</summary>
    private static StoredMailFolder Stored(
        MailAccountId accountId,
        string folderAlias,
        string remotePath,
        int storedEmailCount,
        int unreadEmailCount,
        char? delimiter = null) =>
        new(
            new MailFolderIdentity(accountId, MailFolderAlias.Create(folderAlias)),
            RemoteFolderPath.Create(remotePath, delimiter),
            storedEmailCount,
            unreadEmailCount);

    /// <summary>Answers where each folder sits on its mail server and how much of it is stored.</summary>
    private static IStoredMailFolderReader StoredFolders(params StoredMailFolder[] folders)
    {
        var reader = Substitute.For<IStoredMailFolderReader>();
        reader.ReadAsync(Arg.Any<MailboxScope>(), Arg.Any<CancellationToken>()).Returns(_ => folders);

        return reader;
    }

    /// <summary>Answers the folders local state knows of, which is what a tree's folder list is composed from.</summary>
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

    private static MailFolderDirectoryReader ReaderOver(
        ISynchronizationFreshnessReader freshnessReader,
        ICallerMailAccountCatalog catalog,
        IStoredMailFolderReader storedFolders,
        StubMailFolderMappings? mappings = null,
        MailSynchronizationRunLedger? runLedger = null) =>
        ReaderOver(
            freshnessReader,
            catalog,
            storedFolders,
            mappings ?? StubMailFolderMappings.Nothing,
            runLedger ?? Ledger(),
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead));

    private static MailFolderDirectoryReader ReaderOver(
        ISynchronizationFreshnessReader freshnessReader,
        ICallerMailAccountCatalog catalog,
        IStoredMailFolderReader storedFolders,
        StubMailFolderMappings mappings,
        MailSynchronizationRunLedger runLedger,
        AccessAuthorization authorization)
    {
        var scopeResolver = new MailboxScopeResolver(
            catalog,
            StubMailFolderParticipation.Nothing,
            StubJunkMailFolderCatalog.None,
            mappings.Resolver);

        return new MailFolderDirectoryReader(
            new MailAccountFreshnessReader(
                new MailAccountDirectoryReader(
                    catalog,
                    freshnessReader,
                    scopeResolver,
                    new RecordingMailboxReadTelemetry(),
                    authorization),
                runLedger),
            scopeResolver,
            storedFolders,
            mappings);
    }
}
