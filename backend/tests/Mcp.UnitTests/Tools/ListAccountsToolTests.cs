// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Observability;
using MailFathom.Application.Synchronization.Checkpoints;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Synchronization;
using MailFathom.Mcp.Tools;
using MailFathom.Mcp.Tools.Results;
using MailFathom.Mcp.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Mcp.UnitTests.Tools;

/// <summary>Covers what the <c>list_accounts</c> tool itself owns: publishing the served accounts a use case described.</summary>
/// <remarks>
/// The tool calls the real <see cref="MailAccountDirectoryReader" /> rather than a substitute for it, because which
/// accounts may be published is the use case's decision; what the stubs replace is storage below it.
/// </remarks>
public sealed class ListAccountsToolTests
{
    private static readonly DateTimeOffset SynchronizedAt = new(2026, 3, 4, 9, 0, 0, TimeSpan.Zero);

    private static readonly ServedMailAccount Work = new(
        SyntheticMailOwner.Deployment,
        MailAccountId.Create("acct-1"),
        MailAccountDisplayName.Create("Work mail"),
        MailSynchronizationMode.Polling);

    private static readonly ServedMailAccount Private = new(
        SyntheticMailOwner.Deployment,
        MailAccountId.Create("acct-2"),
        MailAccountDisplayName.Create("Private mail"),
        MailSynchronizationMode.Push);

    /// <summary>Both names are published, because either may be used to narrow a later call and a caller cannot tell which the deployment prefers.</summary>
    [Fact]
    public async Task ListAccountsAsync_ADeploymentServingTwoAccounts_PublishesBothNamesAndTheConfiguredMode()
    {
        // Arrange
        var tool = ToolOver(CatalogServing(Work, Private));

        // Act
        var result = await tool.ListAccountsAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [
                ("acct-1", "Work mail", AccountSynchronizationMode.Polling),
                ("acct-2", "Private mail", AccountSynchronizationMode.Push),
            ],
            result.Accounts.Select(account => (account.AccountId, account.DisplayName, account.SynchronizationMode)));
    }

    [Fact]
    public async Task ListAccountsAsync_AnAccountWithASynchronizedFolder_PublishesHowCurrentTheLocalCopyIs()
    {
        // Arrange
        var tool = ToolOver(
            CatalogServing(Work),
            new MailboxFolderFreshness(Work.Id, MailFolderAlias.Create("INBOX"), SynchronizedAt));

        // Act
        var result = await tool.ListAccountsAsync(TestContext.Current.CancellationToken);

        // Assert
        var folder = Assert.Single(Assert.Single(result.Accounts).Folders);
        Assert.Equal("INBOX", folder.FolderAlias);
        Assert.Equal(SynchronizedAt, folder.SynchronizedAt);
        Assert.True(folder.WasSynchronized);
    }

    /// <summary>A folder entry names its account the same way every other result does, so nothing has to be joined by the caller.</summary>
    [Fact]
    public async Task ListAccountsAsync_AFolderEntry_CarriesTheDisplayNameOfItsOwnAccount()
    {
        // Arrange
        var tool = ToolOver(
            CatalogServing(Work),
            new MailboxFolderFreshness(Work.Id, MailFolderAlias.Create("INBOX"), SynchronizedAt));

        // Act
        var result = await tool.ListAccountsAsync(TestContext.Current.CancellationToken);

        // Assert
        var folder = Assert.Single(Assert.Single(result.Accounts).Folders);
        Assert.Equal("acct-1", folder.AccountId);
        Assert.Equal("Work mail", folder.AccountDisplayName);
    }

    /// <summary>
    /// Both names are the owner's own and unique within them, so two owners may each declare an account under the same
    /// identifier and the same display name. Each caller is published their own, and nothing in either answer says the
    /// other exists.
    /// </summary>
    [Fact]
    public async Task ListAccountsAsync_TwoOwnersHoldingIdenticallyNamedAccounts_PublishesEachOwnerOnlyTheirOwn()
    {
        // Arrange
        var studio = SyntheticServedAccount.Of("studio", SyntheticMailOwner.Deployment);
        var ledger = SyntheticServedAccount.Of("ledger", SyntheticMailOwner.Another);
        var sharedOfOneOwner = SharedlyNamedAccountOf(SyntheticMailOwner.Deployment);
        var sharedOfAnotherOwner = SharedlyNamedAccountOf(SyntheticMailOwner.Another);
        var toOneOwner = ToolOver(
            CatalogServing(sharedOfOneOwner, studio),
            SynchronizedInbox(sharedOfOneOwner),
            SynchronizedInbox(studio));
        var toAnotherOwner = ToolOver(
            CatalogServing(ledger, sharedOfAnotherOwner),
            SynchronizedInbox(ledger),
            SynchronizedInbox(sharedOfAnotherOwner));

        // Act
        var forOneOwner = await toOneOwner.ListAccountsAsync(TestContext.Current.CancellationToken);
        var forAnotherOwner = await toAnotherOwner.ListAccountsAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["shared", "studio"], forOneOwner.Accounts.Select(static account => account.AccountId));
        Assert.Equal(["ledger", "shared"], forAnotherOwner.Accounts.Select(static account => account.AccountId));

        // The control the absence assertions need: a folder entry is published for every account, so the helper reads
        // both halves of each answer, and each owner's own distinctive names are observed present in what it reads.
        Assert.All(forOneOwner.Accounts, static account => Assert.NotEmpty(account.Folders));
        Assert.All(forAnotherOwner.Accounts, static account => Assert.NotEmpty(account.Folders));

        foreach (var ownName in new[] { studio.Id.Value, studio.DisplayName.Value })
        {
            Assert.Contains(ownName, PublishedNamesOf(forOneOwner), StringComparer.OrdinalIgnoreCase);
        }

        foreach (var ownName in new[] { ledger.Id.Value, ledger.DisplayName.Value })
        {
            Assert.Contains(ownName, PublishedNamesOf(forAnotherOwner), StringComparer.OrdinalIgnoreCase);
        }

        foreach (var nameOnlyTheOtherOwnerCarries in new[] { ledger.Id.Value, ledger.DisplayName.Value })
        {
            Assert.DoesNotContain(nameOnlyTheOtherOwnerCarries, PublishedNamesOf(forOneOwner), StringComparer.OrdinalIgnoreCase);
        }

        foreach (var nameOnlyTheOtherOwnerCarries in new[] { studio.Id.Value, studio.DisplayName.Value })
        {
            Assert.DoesNotContain(nameOnlyTheOtherOwnerCarries, PublishedNamesOf(forAnotherOwner), StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>An empty folder list says synchronization has never reached the account, which an empty mailbox answer cannot say for itself.</summary>
    [Fact]
    public async Task ListAccountsAsync_AnAccountSynchronizationHasNeverReached_PublishesItWithNoFolder()
    {
        // Arrange
        var tool = ToolOver(CatalogServing(Work));

        // Act
        var result = await tool.ListAccountsAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(Assert.Single(result.Accounts).Folders);
    }

    /// <summary>Serving nothing is a state configuration allows, and it answers rather than failing.</summary>
    [Fact]
    public async Task ListAccountsAsync_ADeploymentServingNoAccount_PublishesAnEmptyList()
    {
        // Arrange
        var tool = ToolOver(CatalogServing());

        // Act
        var result = await tool.ListAccountsAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Accounts);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ListAccountsAsync_ADeploymentWithSynchronizationOff_SaysSoWhilePublishingItsAccounts(bool synchronizationEnabled)
    {
        // Arrange
        var catalog = new StubMailAccountCatalog
        {
            SynchronizationEnabled = synchronizationEnabled,
            ServedAccounts = [Work],
        };
        var tool = ToolOver(catalog);

        // Act
        var result = await tool.ListAccountsAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(synchronizationEnabled, result.SynchronizationEnabled);
        Assert.Single(result.Accounts);
    }

    /// <summary>The tool answers about mailboxes and never about how MailFathom reaches one, so nothing it publishes could carry a server or a login.</summary>
    [Fact]
    public async Task ListAccountsAsync_AnyDeployment_PublishesNoConnectionDetail()
    {
        // Arrange
        var tool = ToolOver(CatalogServing(Work, Private));

        // Act
        var result = await tool.ListAccountsAsync(TestContext.Current.CancellationToken);

        // Assert
        var publishedPropertyNames = typeof(ListedMailAccount)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.Equal(
            ["AccountId", "DisplayName", "Folders", "SynchronizationMode"],
            [.. publishedPropertyNames.Order(StringComparer.Ordinal)]);
        Assert.NotEmpty(result.Accounts);
    }

    private static StubMailAccountCatalog CatalogServing(params ServedMailAccount[] servedAccounts) => new()
    {
        ServedAccounts = [.. servedAccounts],
    };

    /// <summary>Builds the one synchronized folder an account needs for its folder entries to be published at all.</summary>
    private static MailboxFolderFreshness SynchronizedInbox(ServedMailAccount account) =>
        new(account.Id, MailFolderAlias.Create("INBOX"), SynchronizedAt);

    /// <summary>Builds the account two owners each declare, under one identifier and one display name.</summary>
    private static ServedMailAccount SharedlyNamedAccountOf(MailOwnerId owner) => new(
        owner,
        MailAccountId.Create("shared"),
        MailAccountDisplayName.Create("The shared mailbox"),
        MailSynchronizationMode.Polling);

    /// <summary>Reads every name the answer published, wherever it named an account, so a leak anywhere in it is caught.</summary>
    private static IReadOnlyList<string> PublishedNamesOf(ListAccountsToolResult result) =>
    [
        .. result.Accounts.SelectMany(static account =>
            new[] { account.AccountId, account.DisplayName }.Concat(
                account.Folders.SelectMany(static folder => new[] { folder.AccountId, folder.AccountDisplayName }))),
    ];

    private static ListAccountsTool ToolOver(
        StubMailAccountCatalog catalog,
        params MailboxFolderFreshness[] folderFreshness) =>
        new(
            new MailAccountDirectoryReader(
                catalog,
                new StubSynchronizationFreshnessReader(folderFreshness),
                new MailboxScopeResolver(
                    catalog,
                    StubMailFolderParticipation.Nothing,
                    StubJunkMailFolderCatalog.None,
                    StubMailFolderMappings.ResolvingNothing),
                Substitute.For<IMailboxReadTelemetry>(),
                AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead)),
            catalog);
}
