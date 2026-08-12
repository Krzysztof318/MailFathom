// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Mailboxes;

/// <summary>Covers how a query scope normalizes what resolution handed it.</summary>
public sealed class MailboxScopeTests
{
    private static readonly MailAccountId Primary = MailAccountId.Create("primary");
    private static readonly MailAccountId Secondary = MailAccountId.Create("secondary");

    [Fact]
    public void Create_NoAccountsAndNoFolders_RestrictsNothing()
    {
        // Act
        var scope = MailboxScope.Create(accountIds: null, selectedFolders: null);

        // Assert
        Assert.Empty(scope.AccountIds);
        Assert.Empty(scope.SelectedFolders);
        Assert.Same(MailboxScope.Unrestricted, scope);
    }

    [Fact]
    public void Create_EmptyLists_RestrictNothingJustAsAbsentOnesDo()
    {
        // Act
        var scope = MailboxScope.Create([], []);

        // Assert
        Assert.Same(MailboxScope.Unrestricted, scope);
    }

    /// <summary>Deduplicated and ordered, so two spellings of one scope are one query with one cursor.</summary>
    [Fact]
    public void Create_RepeatedAndUnorderedValues_ProducesOneCanonicalScope()
    {
        // Act
        var scope = MailboxScope.Create(
            [Secondary, Primary, Secondary],
            [
                Folder(Secondary, "SENT"),
                Folder(Primary, "ARCHIVE"),
                Folder(Primary, "ARCHIVE"),
            ]);

        // Assert
        Assert.Equal([Primary, Secondary], scope.AccountIds);
        Assert.Equal([Folder(Primary, "ARCHIVE"), Folder(Secondary, "SENT")], scope.SelectedFolders);
    }

    /// <summary>One alias on two accounts is two folders, which is what keeps a role's two answers apart.</summary>
    [Fact]
    public void Create_OneAliasOnTwoAccounts_KeepsBothPairs()
    {
        // Act
        var scope = MailboxScope.Create([Primary, Secondary], [Folder(Secondary, "JUNK"), Folder(Primary, "JUNK")]);

        // Assert
        Assert.Equal([Folder(Primary, "JUNK"), Folder(Secondary, "JUNK")], scope.SelectedFolders);
    }

    [Fact]
    public void Create_NamingOnlyFolders_IsStillARestrictedScope()
    {
        // Act
        var scope = MailboxScope.Create(accountIds: null, [Folder(Primary, "INBOX")]);

        // Assert
        Assert.NotSame(MailboxScope.Unrestricted, scope);
        Assert.Empty(scope.AccountIds);
    }

    /// <summary>An account that named no folder stays in scope, which is what a role only one account maps produces.</summary>
    [Fact]
    public void Create_AnAccountNoSelectedFolderNames_StaysInScope()
    {
        // Act
        var scope = MailboxScope.Create([Primary, Secondary], [Folder(Primary, "ARCHIVE")]);

        // Assert
        Assert.Equal([Primary, Secondary], scope.AccountIds);
        Assert.Equal([Folder(Primary, "ARCHIVE")], scope.SelectedFolders);
    }

    /// <summary>No ceiling applies to a resolved list, because one role a request named can reach every served account.</summary>
    [Fact]
    public void Create_MoreFoldersThanARequestMayName_IsAccepted()
    {
        // Arrange
        var folders = Enumerable
            .Range(0, MailboxScope.MaximumFolderAliases + 1)
            .Select(position => Folder(Primary, $"folder-{position}"))
            .ToArray();

        // Act
        var scope = MailboxScope.Create([Primary], folders);

        // Assert
        Assert.Equal(folders.Length, scope.SelectedFolders.Count);
    }

    private static MailFolderIdentity Folder(MailAccountId accountId, string alias) =>
        new(accountId, MailFolderAlias.Create(alias));
}
