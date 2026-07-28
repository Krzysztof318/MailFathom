// Copyright © 2026 Krzysztof Kasprowicz

using MailKit;
using MailMcp.Application.Synchronization;
using MailMcp.Domain.Folders;
using NSubstitute;
using Xunit;
using static MailMcp.Infrastructure.UnitTests.MailKitImapSessionTestContext;

namespace MailMcp.Infrastructure.UnitTests;

public sealed class MailKitRemoteFolderCatalogTests
{
    [Fact]
    public async Task ListFoldersAsync_ServerAdvertisesSpecialUse_ReportsEachRoleWithItsPathAndDelimiter()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        await using var client = new FakeImapClient
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
        await using var client = new FakeImapClient
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
        await using var client = new FakeImapClient { InboxFolder = inbox };
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
        await using var client = new FakeImapClient
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
        await using var client = new FakeImapClient
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

    /// <summary>Discovery precedes every folder selection, so nothing it does may be able to touch a message flag.</summary>
    [Fact]
    public async Task ListFoldersAsync_Always_SelectsNoFolderAndRequestsNoFlagUpdate()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var archive = CreateAdvertisedFolder("Archive", '/', FolderAttributes.Archive);
        await using var client = new FakeImapClient
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
        await using var client = new FakeImapClient
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
