// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.Emails;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Folders;
using Xunit;

namespace MailMcp.Application.UnitTests;

/// <summary>Covers how a query scope is normalized and where it refuses to grow.</summary>
public sealed class MailboxScopeTests
{
    [Fact]
    public void Create_NoAccountsAndNoFolders_RestrictsNothing()
    {
        // Act
        var scope = MailboxScope.Create(accountIds: null, folderAliases: null);

        // Assert
        Assert.True(scope.IsUnrestricted);
        Assert.Same(MailboxScope.Unrestricted, scope);
    }

    [Fact]
    public void Create_EmptyLists_RestrictNothingJustAsAbsentOnesDo()
    {
        // Act
        var scope = MailboxScope.Create([], []);

        // Assert
        Assert.True(scope.IsUnrestricted);
    }

    /// <summary>Deduplicated and ordered, so two spellings of one scope are one query with one cursor.</summary>
    [Fact]
    public void Create_RepeatedAndUnorderedValues_ProducesOneCanonicalScope()
    {
        // Act
        var scope = MailboxScope.Create(
            [MailAccountId.Create("secondary"), MailAccountId.Create("primary"), MailAccountId.Create("secondary")],
            [MailFolderAlias.Create("SENT"), MailFolderAlias.Create("ARCHIVE"), MailFolderAlias.Create("archive")]);

        // Assert
        Assert.Equal([MailAccountId.Create("primary"), MailAccountId.Create("secondary")], scope.AccountIds);
        Assert.Equal([MailFolderAlias.Create("ARCHIVE"), MailFolderAlias.Create("SENT")], scope.FolderAliases);
    }

    [Fact]
    public void Create_NamingOnlyFolders_IsStillARestrictedScope()
    {
        // Act
        var scope = MailboxScope.Create(accountIds: null, [MailFolderAlias.Create("INBOX")]);

        // Assert
        Assert.False(scope.IsUnrestricted);
        Assert.Empty(scope.AccountIds);
    }

    [Fact]
    public void Create_MoreAccountsThanAQueryMayName_IsRejected()
    {
        // Arrange
        var accountIds = Enumerable.Range(0, MailboxScope.MaximumAccountIds + 1)
            .Select(index => MailAccountId.Create($"account-{index}"))
            .ToArray();

        // Act
        var failure = Assert.Throws<MailboxQueryFilterInvalidException>(() =>
            MailboxScope.Create(accountIds, folderAliases: null));

        // Assert
        Assert.Equal("accounts", failure.FilterName);
    }

    [Fact]
    public void Create_MoreFolderAliasesThanAQueryMayName_IsRejected()
    {
        // Arrange
        var folderAliases = Enumerable.Range(0, MailboxScope.MaximumFolderAliases + 1)
            .Select(index => MailFolderAlias.Create($"folder-{index}"))
            .ToArray();

        // Act
        var failure = Assert.Throws<MailboxQueryFilterInvalidException>(() =>
            MailboxScope.Create(accountIds: null, folderAliases));

        // Assert
        Assert.Equal("folder aliases", failure.FilterName);
    }

    /// <summary>The limit counts distinct values, so repeating one account is not a way to reach it.</summary>
    [Fact]
    public void Create_TheSameAccountRepeatedPastTheLimit_IsAccepted()
    {
        // Arrange
        var accountIds = Enumerable.Repeat(MailAccountId.Create("primary"), MailboxScope.MaximumAccountIds + 10)
            .ToArray();

        // Act
        var scope = MailboxScope.Create(accountIds, folderAliases: null);

        // Assert
        Assert.Equal(MailAccountId.Create("primary"), Assert.Single(scope.AccountIds));
    }
}
