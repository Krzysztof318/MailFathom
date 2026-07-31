// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailFathom.Application.Emails;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using Xunit;

namespace MailFathom.Application.UnitTests;

/// <summary>Covers how a query scope is normalized and where it refuses to grow.</summary>
public sealed class MailboxScopeTests
{
    [Fact]
    public void Create_NoAccountsAndNoFolders_RestrictsNothing()
    {
        // Act
        var scope = MailboxScope.Create(accountIds: null, folderAliases: null);

        // Assert
        Assert.Empty(scope.AccountIds);
        Assert.Empty(scope.FolderAliases);
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
        Assert.NotSame(MailboxScope.Unrestricted, scope);
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

    /// <summary>The limit counts the values a request names, so repeating one account is not a way past it.</summary>
    [Fact]
    public void Create_TheSameAccountRepeatedPastTheLimit_IsRejected()
    {
        // Arrange
        var accountIds = Enumerable.Repeat(MailAccountId.Create("primary"), MailboxScope.MaximumAccountIds + 1);

        // Act
        var failure = Assert.Throws<MailboxQueryFilterInvalidException>(() =>
            MailboxScope.Create(accountIds, folderAliases: null));

        // Assert
        Assert.Equal("accounts", failure.FilterName);
    }

    /// <summary>Refused while the caller's sequence is read, so an over-long list is never materialized to be counted.</summary>
    [Fact]
    public void Create_MoreAccountsThanAQueryMayName_StopsReadingAtTheValueThatCrossesTheLimit()
    {
        // Arrange
        var readCount = 0;
        var accountIds = Enumerable.Range(0, MailboxScope.MaximumAccountIds + 100)
            .Select(index =>
            {
                readCount++;

                return MailAccountId.Create($"account-{index}");
            });

        // Act
        Assert.Throws<MailboxQueryFilterInvalidException>(() =>
            MailboxScope.Create(accountIds, folderAliases: null));

        // Assert
        Assert.Equal(MailboxScope.MaximumAccountIds + 1, readCount);
    }
}
