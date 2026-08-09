// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Synchronization;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Mailboxes;

public sealed class MailboxScopeResolverTests
{
    private static readonly ServedMailAccount Work = new(
        MailAccountId.Create("acct-1"),
        MailAccountDisplayName.Create("Work mail"),
        MailSynchronizationMode.Polling);

    private static readonly ServedMailAccount Private = new(
        MailAccountId.Create("acct-2"),
        MailAccountDisplayName.Create("Private mail"),
        MailSynchronizationMode.Push);

    [Fact]
    public void ReadableScope_AnAccountNamedByItsIdentifier_ResolvesToThatAccount()
    {
        // Arrange
        var resolver = ResolverServing(Work, Private);

        // Act
        var scope = resolver.ReadableScope([MailAccountSelector.Create("acct-1")], []);

        // Assert
        Assert.Equal([Work.Id], scope.AccountIds);
    }

    /// <summary>The display name is what a person reads back to an assistant, so it selects the account the identifier does.</summary>
    [Theory]
    [InlineData("Work mail")]
    [InlineData("work mail")]
    [InlineData("WORK MAIL")]
    [InlineData("  Work mail  ")]
    public void ReadableScope_AnAccountNamedByItsDisplayName_ResolvesToThatAccountWhateverTheCase(string named)
    {
        // Arrange
        var resolver = ResolverServing(Work, Private);

        // Act
        var scope = resolver.ReadableScope([MailAccountSelector.Create(named)], []);

        // Assert
        Assert.Equal([Work.Id], scope.AccountIds);
    }

    /// <summary>Both spellings resolve to one identity, so a request written either way is one query with one cursor.</summary>
    [Fact]
    public void ReadableScope_OneAccountNamedBothWays_IsOneAccountInTheScope()
    {
        // Arrange
        var resolver = ResolverServing(Work, Private);

        // Act
        var scope = resolver.ReadableScope(
            [MailAccountSelector.Create("acct-1"), MailAccountSelector.Create("Work mail")],
            []);

        // Assert
        Assert.Equal([Work.Id], scope.AccountIds);
    }

    /// <summary>An identifier is a configured key, so a request that recases one names no account rather than that account.</summary>
    [Fact]
    public void ReadableScope_AnIdentifierNamedInAnotherCase_IsRefused()
    {
        // Arrange
        var resolver = ResolverServing(Work);

        // Act, Assert
        Assert.Throws<MailAccountNotAccessibleException>(
            () => resolver.ReadableScope([MailAccountSelector.Create("ACCT-1")], []));
    }

    /// <summary>Text naming nothing meets the refusal an unserved identifier meets, so a caller learns neither which spelling was wrong nor that the other exists.</summary>
    [Theory]
    [InlineData("acct-3")]
    [InlineData("Somebody else's mail")]
    public void ReadableScope_TextNamingNoServedAccount_IsRefusedTheSameWay(string named)
    {
        // Arrange
        var resolver = ResolverServing(Work, Private);

        // Act
        var failure = Assert.Throws<MailAccountNotAccessibleException>(
            () => resolver.ReadableScope([MailAccountSelector.Create(named)], []));

        // Assert
        Assert.Equal(MailAccountSelector.Create(named), failure.RequestedAccount);
        Assert.Contains(named, failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Naming no account reads every served one, which is what stops a removed account's stored mail from being published.</summary>
    [Fact]
    public void ReadableScope_NoAccountNamed_IsRestrictedToTheServedAccounts()
    {
        // Arrange
        var resolver = ResolverServing(Private, Work);

        // Act
        var scope = resolver.ReadableScope([], [MailFolderAlias.Create("INBOX")]);

        // Assert
        Assert.Equal([Work.Id, Private.Id], scope.AccountIds);
        Assert.Equal([MailFolderAlias.Create("INBOX")], scope.FolderAliases);
    }

    /// <summary>A deployment serving nothing resolves to an empty scope rather than to an unrestricted one.</summary>
    [Fact]
    public void ReadableScope_ADeploymentServingNoAccount_ResolvesToAnEmptyScope()
    {
        // Arrange
        var resolver = ResolverServing();

        // Act
        var scope = resolver.ReadableScope([], []);

        // Assert
        Assert.Empty(scope.AccountIds);
    }

    /// <summary>The count is refused before anything is resolved, so a request enumerating names never walks the served set once per name.</summary>
    [Fact]
    public void ReadableScope_MoreAccountsNamedThanTheLimitPermits_IsRefusedAsAFilter()
    {
        // Arrange
        var resolver = ResolverServing(Work);
        var tooMany = Enumerable
            .Range(0, MailboxScope.MaximumAccountIds + 1)
            .Select(position => MailAccountSelector.Create($"acct-{position}"))
            .ToArray();

        // Act, Assert
        Assert.Throws<MailboxQueryFilterInvalidException>(() => resolver.ReadableScope(tooMany, []));
    }

    private static MailboxScopeResolver ResolverServing(params ServedMailAccount[] servedAccounts)
    {
        var catalog = Substitute.For<IMailAccountCatalog>();
        catalog.ServedAccounts.Returns(
        [
            .. servedAccounts.OrderBy(account => account.Id.Value, StringComparer.Ordinal),
        ]);

        return new MailboxScopeResolver(catalog);
    }
}
