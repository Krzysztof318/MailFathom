// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
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
        var scope = MailboxScope.Create(SyntheticMailUser.Deployment, accountIds: null, selectedFolders: null);

        // Assert
        Assert.Empty(scope.AccountIds);
        Assert.Empty(scope.SelectedFolders);
        Assert.Same(MailboxScope.NothingReadable, scope);
    }

    [Fact]
    public void Create_EmptyLists_RestrictNothingJustAsAbsentOnesDo()
    {
        // Act
        var scope = MailboxScope.Create(SyntheticMailUser.Deployment, [], []);

        // Assert
        Assert.Same(MailboxScope.NothingReadable, scope);
    }

    /// <summary>Deduplicated and ordered, so two spellings of one scope are one query with one cursor.</summary>
    [Fact]
    public void Create_RepeatedAndUnorderedValues_ProducesOneCanonicalScope()
    {
        // Act
        var scope = MailboxScope.Create(
            SyntheticMailUser.Deployment,
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
        var scope = MailboxScope.Create(
            SyntheticMailUser.Deployment,
            [Primary, Secondary],
            [Folder(Secondary, "JUNK"), Folder(Primary, "JUNK")]);

        // Assert
        Assert.Equal([Folder(Primary, "JUNK"), Folder(Secondary, "JUNK")], scope.SelectedFolders);
    }

    [Fact]
    public void Create_NamingOnlyFolders_IsStillARestrictedScope()
    {
        // Act
        var scope = MailboxScope.Create(SyntheticMailUser.Deployment, accountIds: null, [Folder(Primary, "INBOX")]);

        // Assert
        Assert.NotSame(MailboxScope.NothingReadable, scope);
        Assert.Empty(scope.AccountIds);
    }

    /// <summary>An account that named no folder stays in scope, which is what a role only one account maps produces.</summary>
    [Fact]
    public void Create_AnAccountNoSelectedFolderNames_StaysInScope()
    {
        // Act
        var scope = MailboxScope.Create(SyntheticMailUser.Deployment, [Primary, Secondary], [Folder(Primary, "ARCHIVE")]);

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
            .Range(0, MailboxScope.MaximumFolders + 1)
            .Select(position => Folder(Primary, $"folder-{position}"))
            .ToArray();

        // Act
        var scope = MailboxScope.Create(SyntheticMailUser.Deployment, [Primary], folders);

        // Assert
        Assert.Equal(folders.Length, scope.SelectedFolders.Count);
    }

    /// <summary>Deduplicated and ordered like the other two lists, so one selection is one query with one cursor.</summary>
    [Fact]
    public void NarrowedToEmails_RepeatedAndUnorderedIdentifiers_ProduceOneCanonicalNarrowing()
    {
        // Arrange
        var first = StoredEmailId.Create(new Guid("11111111-1111-1111-1111-111111111111"));
        var second = StoredEmailId.Create(new Guid("22222222-2222-2222-2222-222222222222"));
        var scope = MailboxScope.Create(SyntheticMailUser.Deployment, [Primary], []);

        // Act
        var narrowed = scope.NarrowedToEmails([second, first, second]);

        // Assert
        Assert.Equal([first, second], narrowed.SelectedEmails);
    }

    /// <summary>The bound counts what the caller wrote, so repeating one identifier cannot buy a larger predicate.</summary>
    [Fact]
    public void NarrowedToEmails_MoreIdentifiersThanTheBoundAdmits_IsRefused()
    {
        // Arrange
        var repeated = Enumerable
            .Repeat(StoredEmailId.Create(new Guid("33333333-3333-3333-3333-333333333333")), MailboxScope.MaximumSelectedEmails + 1)
            .ToArray();
        var scope = MailboxScope.Create(SyntheticMailUser.Deployment, [Primary], []);

        // Act
        var refusal = Assert.Throws<MailboxQueryFilterInvalidException>(() => scope.NarrowedToEmails(repeated));

        // Assert
        Assert.Equal("selected emails", refusal.FilterName);
    }

    /// <summary>A narrowing narrows: everything the scope already restricted still restricts.</summary>
    [Fact]
    public void NarrowedToThread_AScopeThatNamedFolders_KeepsEveryOtherRestriction()
    {
        // Arrange
        var thread = EmailThreadId.Create(new Guid("44444444-4444-4444-4444-444444444444"));
        var scope = MailboxScope.Create(SyntheticMailUser.Deployment, [Primary], [Folder(Primary, "ARCHIVE")]);

        // Act
        var narrowed = scope.NarrowedToThread(thread);

        // Assert
        Assert.Equal(thread, narrowed.SelectedThread);
        Assert.Equal([Primary], narrowed.AccountIds);
        Assert.Equal([Folder(Primary, "ARCHIVE")], narrowed.SelectedFolders);
    }

    private static MailFolderIdentity Folder(MailAccountId accountId, string alias) =>
        new(accountId, MailFolderAlias.Create(alias));
}
