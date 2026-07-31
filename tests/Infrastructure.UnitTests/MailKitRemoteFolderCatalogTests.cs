// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailFathom.Application.Synchronization;
using MailFathom.Domain.Folders;
using MailKit;
using NSubstitute;
using Xunit;
using static MailFathom.Infrastructure.UnitTests.MailKitImapSessionTestContext;

namespace MailFathom.Infrastructure.UnitTests;

public sealed class MailKitRemoteFolderCatalogTests
{
    [Fact]
    public async Task ListFoldersAsync_ServerAdvertisesSpecialUse_ReportsEachRoleWithItsPathAndDelimiter()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient
        {
            InboxFolder = CreateAdvertisedFolder("INBOX", '/', FolderAttributes.Inbox),
        };
        var personalNamespace = client.PersonalNamespaces[0];
        client.FoldersByNamespace[personalNamespace] =
        [
            CreateAdvertisedFolder("Verzonden items", '/', FolderAttributes.Sent),
            CreateAdvertisedFolder("Archief/2026", '/', FolderAttributes.Archive),
        ];
        var catalog = CreateFolderCatalog(resilience, client);

        // Act
        var advertisedFolders = await catalog.ListFoldersAsync(
            PrimaryAccount,
            TlsOnConnectWithPlainPolicy,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [("INBOX", MailFolderSpecialUse.Inbox), ("Verzonden items", MailFolderSpecialUse.Sent), ("Archief/2026", MailFolderSpecialUse.Archive)],
            advertisedFolders.Select(folder => (folder.Path.Value, folder.SpecialUses.Single())));
        Assert.All(advertisedFolders, folder => Assert.Equal('/', folder.Path.HierarchyDelimiter));
    }

    /// <summary>A server without RFC 6154 is ordinary, and discovery must still describe every folder it lists.</summary>
    [Fact]
    public async Task ListFoldersAsync_ServerReportsNoSpecialUseAttributes_ReportsThePathsWithNoRoles()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient
        {
            InboxFolder = CreateAdvertisedFolder("INBOX", '.', FolderAttributes.None),
        };
        client.FoldersByNamespace[client.PersonalNamespaces[0]] =
        [
            CreateAdvertisedFolder("INBOX.Sent", '.', FolderAttributes.None),
        ];
        var catalog = CreateFolderCatalog(resilience, client);

        // Act
        var advertisedFolders = await catalog.ListFoldersAsync(
            PrimaryAccount,
            TlsOnConnectWithPlainPolicy,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["INBOX", "INBOX.Sent"], advertisedFolders.Select(folder => folder.Path.Value));
        Assert.All(advertisedFolders, folder => Assert.Empty(folder.SpecialUses));
    }

    /// <summary>A namespace listing that already covers the inbox must not describe it twice.</summary>
    [Fact]
    public async Task ListFoldersAsync_NamespaceListingIncludesTheInbox_ReportsItOnce()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var inbox = CreateAdvertisedFolder("INBOX", '/', FolderAttributes.Inbox);
        var client = new FakeImapClient { InboxFolder = inbox };
        client.FoldersByNamespace[client.PersonalNamespaces[0]] =
        [
            CreateAdvertisedFolder("INBOX", '/', FolderAttributes.Inbox),
            CreateAdvertisedFolder("Archive", '/', FolderAttributes.Archive),
        ];
        var catalog = CreateFolderCatalog(resilience, client);

        // Act
        var advertisedFolders = await catalog.ListFoldersAsync(
            PrimaryAccount,
            TlsOnConnectWithPlainPolicy,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["INBOX", "Archive"], advertisedFolders.Select(folder => folder.Path.Value));
    }

    /// <summary>A folder the server lists only as a hierarchy placeholder holds no mail and must not be bindable.</summary>
    [Fact]
    public async Task ListFoldersAsync_ServerListsANonExistentFolder_LeavesItOutOfTheCatalog()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient
        {
            InboxFolder = CreateAdvertisedFolder("INBOX", '/', FolderAttributes.Inbox),
        };
        client.FoldersByNamespace[client.PersonalNamespaces[0]] =
        [
            CreateAdvertisedFolder("Archive", '/', FolderAttributes.NonExistent | FolderAttributes.Archive),
        ];
        var catalog = CreateFolderCatalog(resilience, client);

        // Act
        var advertisedFolders = await catalog.ListFoldersAsync(
            PrimaryAccount,
            TlsOnConnectWithPlainPolicy,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["INBOX"], advertisedFolders.Select(folder => folder.Path.Value));
    }

    /// <summary>A listing entry that names no folder must cost that entry and not the folders listed beside it.</summary>
    [Fact]
    public async Task ListFoldersAsync_ServerListsAnEntryThatNamesNoFolder_ReportsTheRemainingFolders()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient
        {
            InboxFolder = CreateAdvertisedFolder("INBOX", '/', FolderAttributes.Inbox),
        };
        client.FoldersByNamespace[client.PersonalNamespaces[0]] =
        [
            CreateAdvertisedFolder(string.Empty, '/', FolderAttributes.None),
            CreateAdvertisedFolder("Archive", '/', FolderAttributes.Archive),
        ];
        var catalog = CreateFolderCatalog(resilience, client);

        // Act
        var advertisedFolders = await catalog.ListFoldersAsync(
            PrimaryAccount,
            TlsOnConnectWithPlainPolicy,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["INBOX", "Archive"], advertisedFolders.Select(folder => folder.Path.Value));
    }

    /// <summary>A hierarchy container is not a mailbox, and binding an alias to one would fail every later selection.</summary>
    [Fact]
    public async Task ListFoldersAsync_ServerListsANoSelectContainer_LeavesItOutOfTheCatalog()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient
        {
            InboxFolder = CreateAdvertisedFolder("INBOX", '/', FolderAttributes.Inbox),
        };
        client.FoldersByNamespace[client.PersonalNamespaces[0]] =
        [
            CreateAdvertisedFolder("Archive", '/', FolderAttributes.NoSelect),
            CreateAdvertisedFolder("Archive/2026", '/', FolderAttributes.None),
        ];
        var catalog = CreateFolderCatalog(resilience, client);

        // Act
        var advertisedFolders = await catalog.ListFoldersAsync(
            PrimaryAccount,
            TlsOnConnectWithPlainPolicy,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["INBOX", "Archive/2026"], advertisedFolders.Select(folder => folder.Path.Value));
    }

    /// <summary>A delegated mailbox is a folder an operator may name, and the server will open it.</summary>
    [Fact]
    public async Task ListFoldersAsync_AccountReachesSharedAndOtherNamespaces_ListsThoseFoldersToo()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient
        {
            InboxFolder = CreateAdvertisedFolder("INBOX", '/', FolderAttributes.Inbox),
            OtherNamespaces = [new FolderNamespace('/', "Other Users/")],
            SharedNamespaces = [new FolderNamespace('/', "Shared Folders/")],
        };
        client.FoldersByNamespace[client.PersonalNamespaces[0]] = [CreateAdvertisedFolder("Archive", '/', FolderAttributes.Archive)];
        client.FoldersByNamespace[client.OtherNamespaces[0]] = [CreateAdvertisedFolder("Other Users/anna/INBOX", '/', FolderAttributes.None)];
        client.FoldersByNamespace[client.SharedNamespaces[0]] = [CreateAdvertisedFolder("Shared Folders/Support", '/', FolderAttributes.None)];
        var catalog = CreateFolderCatalog(resilience, client);

        // Act
        var advertisedFolders = await catalog.ListFoldersAsync(
            PrimaryAccount,
            TlsOnConnectWithPlainPolicy,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            ["INBOX", "Archive", "Other Users/anna/INBOX", "Shared Folders/Support"],
            advertisedFolders.Select(folder => folder.Path.Value));
    }

    /// <summary>The folder tree is a remote answer, so what one listing retains has to be bounded like any other.</summary>
    [Fact]
    public async Task ListFoldersAsync_ServerAdvertisesAnImplausibleFolderTree_FailsInsteadOfRetainingItAll()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient
        {
            InboxFolder = CreateAdvertisedFolder("INBOX", '/', FolderAttributes.Inbox),
        };
        client.FoldersByNamespace[client.PersonalNamespaces[0]] =
        [
            .. Enumerable.Range(0, 10_001).Select(index =>
                CreateAdvertisedFolder($"Folder{index}", '/', FolderAttributes.None)),
        ];
        var catalog = CreateFolderCatalog(resilience, client);

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => catalog.ListFoldersAsync(PrimaryAccount, TlsOnConnectWithPlainPolicy, TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("10000", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Discovery precedes every folder selection, so nothing it does may be able to touch a message flag.</summary>
    [Fact]
    public async Task ListFoldersAsync_Always_SelectsNoFolderAndRequestsNoFlagUpdate()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var archive = CreateAdvertisedFolder("Archive", '/', FolderAttributes.Archive);
        var client = new FakeImapClient
        {
            InboxFolder = CreateAdvertisedFolder("INBOX", '/', FolderAttributes.Inbox),
        };
        client.FoldersByNamespace[client.PersonalNamespaces[0]] = [archive];
        var catalog = CreateFolderCatalog(resilience, client);

        // Act
        await catalog.ListFoldersAsync(PrimaryAccount, TlsOnConnectWithPlainPolicy, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, client.GetFolderAsyncCount);
        await archive.DidNotReceive().OpenAsync(Arg.Any<FolderAccess>(), Arg.Any<CancellationToken>());
        await archive.DidNotReceive().StoreAsync(Arg.Any<IList<UniqueId>>(), Arg.Any<IStoreFlagsRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListFoldersAsync_ServerRefusesTheListing_ReportsTheAccountAsUnavailableWithoutNamingAFolder()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient
        {
            InboxFolder = CreateAdvertisedFolder("INBOX", '/', FolderAttributes.Inbox),
            GetFoldersException = new IOException("the server dropped the listing"),
        };
        var catalog = CreateFolderCatalog(resilience, client);

        // Act
        var failure = await Assert.ThrowsAsync<MailboxUnavailableException>(
            () => catalog.ListFoldersAsync(PrimaryAccount, TlsOnConnectWithPlainPolicy, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(PrimaryAccount, failure.AccountId);
        Assert.Null(failure.FolderAlias);
    }

    private static IMailFolder CreateAdvertisedFolder(string fullName, char directorySeparator, FolderAttributes attributes)
    {
        var folder = Substitute.For<IMailFolder>();
        folder.FullName.Returns(fullName);
        folder.DirectorySeparator.Returns(directorySeparator);
        folder.Attributes.Returns(attributes);

        return folder;
    }
}
