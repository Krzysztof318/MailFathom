// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Folders;
using MailFathom.Domain.Folders;
using MailFathom.Host.Api;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers what the folders route puts on the wire, which is what a client draws its mailbox tree from.</summary>
/// <remarks>
/// The use case's own decisions — whose folders these are, which of them a tree holds, and what a state means — are
/// covered where they are taken. What is asserted here is the translation: that every fact the use case produced
/// reaches the response, that the account half is the same shape the accounts route publishes, and that nothing of the
/// mail itself travels beside them.
/// </remarks>
public sealed class ClientMailFoldersEndpointTests
{
    private static readonly DateTimeOffset SynchronizedAt = new(2026, 8, 15, 10, 0, 0, TimeSpan.Zero);

    private static readonly ServedMailAccount Work = SyntheticServedAccount.Of("work");

    private static readonly ServedMailAccount Private = SyntheticServedAccount.Of("private");

    /// <summary>The path a client appends to the address it was configured with, pinned because the client composes it from a constant of its own.</summary>
    [Fact]
    public void MailFoldersRoute_IsThePathAClientComposes() =>
        Assert.Equal("/folders", ClientMailFoldersEndpoint.MailFoldersRoute);

    /// <summary>Each folder reaches the wire as its name, its role, its place on the server, its two counts, and its freshness.</summary>
    [Fact]
    public void For_AFolderHoldingMail_CarriesEveryFactATreeIsDrawnFrom()
    {
        // Arrange
        var directory = Directory(Account(
            Work,
            Folder("inbox", MailFolderSpecialUse.Inbox, ["INBOX"], storedEmailCount: 12, unreadEmailCount: 3)));

        // Act
        var response = ClientMailFoldersResponse.For(directory);

        // Assert
        var folder = Assert.Single(Assert.Single(response.Accounts).Folders);
        Assert.Equal("INBOX", folder.Alias);
        Assert.Equal(nameof(MailFolderSpecialUse.Inbox), folder.Role);
        Assert.Equal(["INBOX"], folder.Path);
        Assert.Equal(12, folder.StoredEmailCount);
        Assert.Equal(3, folder.UnreadEmailCount);
        Assert.Equal(nameof(MailSynchronizationState.Synchronized), folder.SynchronizationState);
        Assert.Equal(SynchronizedAt, folder.LastSynchronizedAt);
        Assert.False(folder.Behind);
    }

    /// <summary>The account half is the accounts route's own type, so a client parses one account shape across this surface.</summary>
    [Fact]
    public void For_AnAccountWithFolders_PublishesTheAccountExactlyAsTheAccountsRouteDoes()
    {
        // Arrange
        var freshness = new MailAccountFreshness(
            Work,
            MailSynchronizationState.Unreachable,
            SynchronizedAt,
            IsBehind: true,
            []);

        // Act
        var response = ClientMailFoldersResponse.For(Directory(new MailAccountFolders(freshness, [])));

        // Assert
        Assert.Equal(
            ClientMailAccountResponse.For(freshness),
            Assert.Single(response.Accounts).Account);
    }

    /// <summary>A folder configuration labelled with no role says so rather than being given one a client would then trust.</summary>
    [Fact]
    public void For_AFolderWithNoRole_CarriesNoneRatherThanAGuess()
    {
        // Arrange
        var directory = Directory(Account(
            Work,
            Folder("2024", role: null, ["Archiwum", "2024"], storedEmailCount: 0, unreadEmailCount: 0)));

        // Act
        var response = ClientMailFoldersResponse.For(directory);

        // Assert
        Assert.Null(Assert.Single(Assert.Single(response.Accounts).Folders).Role);
    }

    /// <summary>Each role reaches the wire under its own name, which is what a client matches a folder icon on.</summary>
    [Theory]
    [InlineData(MailFolderSpecialUse.Inbox)]
    [InlineData(MailFolderSpecialUse.Sent)]
    [InlineData(MailFolderSpecialUse.Junk)]
    [InlineData(MailFolderSpecialUse.Outbox)]
    public void For_AFolderPlayingARole_PublishesItUnderItsOwnName(MailFolderSpecialUse role)
    {
        // Arrange
        var directory = Directory(Account(
            Work,
            Folder("inbox", role, ["INBOX"], storedEmailCount: 0, unreadEmailCount: 0)));

        // Act
        var response = ClientMailFoldersResponse.For(directory);

        // Assert
        Assert.Equal(role.ToString(), Assert.Single(Assert.Single(response.Accounts).Folders).Role);
    }

    /// <summary>Each folder state reaches the wire under its own name, so a client can tell an empty folder from one whose server is not answering.</summary>
    [Theory]
    [InlineData(MailSynchronizationState.NeverSynchronized)]
    [InlineData(MailSynchronizationState.Synchronized)]
    [InlineData(MailSynchronizationState.Failing)]
    [InlineData(MailSynchronizationState.Unreachable)]
    public void For_AnyFolderState_PublishesItUnderItsOwnName(MailSynchronizationState state)
    {
        // Arrange
        var directory = Directory(Account(
            Work,
            new DescribedMailFolder(
                new MailFolderFreshness(MailFolderAlias.Create("inbox"), state, SynchronizedAt, IsBehind: false),
                MailFolderSpecialUse.Inbox,
                ["INBOX"],
                StoredEmailCount: 0,
                UnreadEmailCount: 0)));

        // Act
        var response = ClientMailFoldersResponse.For(directory);

        // Assert
        Assert.Equal(
            state.ToString(),
            Assert.Single(Assert.Single(response.Accounts).Folders).SynchronizationState);
    }

    /// <summary>A folder with mail still to take in says so beside its state, because a working refresh leaves one behind too.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void For_AFolderWithMailStillToTakeIn_SaysSoBesideItsState(bool behind)
    {
        // Arrange
        var directory = Directory(Account(
            Work,
            new DescribedMailFolder(
                new MailFolderFreshness(
                    MailFolderAlias.Create("inbox"),
                    MailSynchronizationState.Synchronized,
                    SynchronizedAt,
                    behind),
                MailFolderSpecialUse.Inbox,
                ["INBOX"],
                StoredEmailCount: 0,
                UnreadEmailCount: 0)));

        // Act
        var response = ClientMailFoldersResponse.For(directory);

        // Assert
        Assert.Equal(behind, Assert.Single(Assert.Single(response.Accounts).Folders).Behind);
    }

    /// <summary>An alias nothing has bound to a remote folder carries no hierarchy rather than a level nobody can read.</summary>
    [Fact]
    public void For_AFolderWithNoRemoteBinding_CarriesNoHierarchy()
    {
        // Arrange
        var directory = Directory(Account(
            Work,
            Folder("inbox", MailFolderSpecialUse.Inbox, [], storedEmailCount: 0, unreadEmailCount: 0)));

        // Act
        var response = ClientMailFoldersResponse.For(directory);

        // Assert
        Assert.Empty(Assert.Single(Assert.Single(response.Accounts).Folders).Path);
    }

    /// <summary>The order the use case answered in is the order a client renders, so nothing here sorts either level of the tree again.</summary>
    [Fact]
    public void For_SeveralAccounts_KeepsTheOrderTheUseCaseAnsweredIn()
    {
        // Arrange
        var directory = Directory(
            Account(Private, Folder("inbox", MailFolderSpecialUse.Inbox, ["INBOX"], 1, 1)),
            Account(
                Work,
                Folder("archive", role: null, ["Archive"], 5, 0),
                Folder("inbox", MailFolderSpecialUse.Inbox, ["INBOX"], 2, 2)));

        // Act
        var response = ClientMailFoldersResponse.For(directory);

        // Assert
        Assert.Equal([Private.Id.Value, Work.Id.Value], response.Accounts.Select(account => account.Account.Id));
        Assert.Equal(["ARCHIVE", "INBOX"], response.Accounts[1].Folders.Select(folder => folder.Alias));
    }

    /// <summary>An owner with no account reads an empty tree, which is a state a client renders rather than an error.</summary>
    [Fact]
    public void For_AnOwnerWithNoAccount_CarriesAnEmptyCollection()
    {
        // Arrange
        var directory = new MailFolderDirectory(SynchronizationEnabled: true, []);

        // Act
        var response = ClientMailFoldersResponse.For(directory);

        // Assert
        Assert.Empty(response.Accounts);
    }

    /// <summary>The deployment-wide switch is reported beside the tree, because no per-folder value carries it.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void For_ADeploymentThatSwitchedSynchronizationOff_SaysSoBesideTheTree(bool synchronizationEnabled)
    {
        // Arrange
        var directory = new MailFolderDirectory(
            synchronizationEnabled,
            [Account(Work, Folder("inbox", MailFolderSpecialUse.Inbox, ["INBOX"], 1, 0))]);

        // Act
        var response = ClientMailFoldersResponse.For(directory);

        // Assert
        Assert.Equal(synchronizationEnabled, response.SynchronizationEnabled);
    }

    private static MailFolderDirectory Directory(params MailAccountFolders[] accounts) =>
        new(SynchronizationEnabled: true, accounts);

    private static MailAccountFolders Account(ServedMailAccount account, params DescribedMailFolder[] folders) =>
        new(
            new MailAccountFreshness(
                account,
                MailSynchronizationState.Synchronized,
                SynchronizedAt,
                IsBehind: false,
                [.. folders.Select(folder => folder.Freshness)]),
            folders);

    private static DescribedMailFolder Folder(
        string alias,
        MailFolderSpecialUse? role,
        IReadOnlyList<string> hierarchyLevels,
        int storedEmailCount,
        int unreadEmailCount) =>
        new(
            new MailFolderFreshness(
                MailFolderAlias.Create(alias),
                MailSynchronizationState.Synchronized,
                SynchronizedAt,
                IsBehind: false),
            role,
            hierarchyLevels,
            storedEmailCount,
            unreadEmailCount);
}
