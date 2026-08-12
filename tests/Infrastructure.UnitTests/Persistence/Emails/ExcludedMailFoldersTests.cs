// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Emails;

/// <summary>Covers the narrowing every read applies to the folders a configuration decision took out of it.</summary>
/// <remarks>
/// The predicate is composed here and evaluated by PostgreSQL, so what these tests establish is which rows it selects
/// rather than what SQL it becomes. That distinction is the whole point of the account-and-alias pair: a predicate
/// written over the alias alone would translate perfectly and withhold another account's mail.
/// </remarks>
public sealed class ExcludedMailFoldersTests
{
    private static readonly MailFolderIdentity WorkPrivate = new(
        MailAccountId.Create("work"),
        MailFolderAlias.Create("PRIVATE"));

    /// <summary>A folder decision belongs to one account, so the same alias elsewhere is somebody else's folder.</summary>
    [Fact]
    public void Excluding_AFolderOfOneAccount_LeavesTheSameAliasOfAnotherAccountReadable()
    {
        // Arrange
        var emails = Emails(("work", "PRIVATE"), ("work", "INBOX"), ("home", "PRIVATE"));

        // Act
        var readable = ExcludedMailFolders.Excluding(emails, [WorkPrivate]);

        // Assert
        Assert.Equal(
            [("work", "INBOX"), ("home", "PRIVATE")],
            readable.AsEnumerable().Select(email => (email.MailboxAccountId, email.MailFolder.Alias)));
    }

    /// <summary>A deployment that withholds nothing pays for no clause at all, which is what keeps the switch free.</summary>
    [Fact]
    public void Excluding_NothingExcluded_LeavesTheQueryAsItWas()
    {
        // Arrange
        var emails = Emails(("work", "PRIVATE"), ("home", "INBOX"));

        // Act
        var readable = ExcludedMailFolders.Excluding(emails, []);

        // Assert
        Assert.Same(emails, readable);
    }

    /// <summary>Several folders of one account are one clause, so none of them may survive it.</summary>
    [Fact]
    public void Excluding_SeveralFoldersOfOneAccount_LeavesNoneOfThemReadable()
    {
        // Arrange
        var emails = Emails(("work", "PRIVATE"), ("work", "SPAM"), ("work", "INBOX"));
        var excluded = new[]
        {
            WorkPrivate,
            new MailFolderIdentity(MailAccountId.Create("work"), MailFolderAlias.Create("SPAM")),
        };

        // Act
        var readable = ExcludedMailFolders.Excluding(emails, excluded);

        // Assert
        Assert.Equal(["INBOX"], readable.Select(email => email.MailFolder.Alias));
    }

    /// <summary>The freshness a tool reports is read from the bindings, so a withheld folder may not be named there either.</summary>
    [Fact]
    public void Excluding_FolderBindings_LeavesOutTheExcludedBindingOnly()
    {
        // Arrange
        var folders = new[]
        {
            Folder("work", "PRIVATE"),
            Folder("work", "INBOX"),
            Folder("home", "PRIVATE"),
        }.AsQueryable();

        // Act
        var readable = ExcludedMailFolders.Excluding(folders, [WorkPrivate]);

        // Assert
        Assert.Equal(
            [("work", "INBOX"), ("home", "PRIVATE")],
            readable.AsEnumerable().Select(folder => (folder.MailboxAccountId, folder.Alias)));
    }

    /// <summary>A read that has already found one email asks about that pair, and an alias alone would answer for two accounts.</summary>
    [Theory]
    [InlineData("work", "PRIVATE", true)]
    [InlineData("home", "PRIVATE", false)]
    [InlineData("work", "INBOX", false)]
    public void Contains_APair_AnswersForThatAccountAndAliasTogether(string accountId, string alias, bool excluded)
    {
        // Arrange, Act
        var isExcluded = ExcludedMailFolders.Contains([WorkPrivate], accountId, alias);

        // Assert
        Assert.Equal(excluded, isExcluded);
    }

    private static IQueryable<StoredEmailEntity> Emails(params (string AccountId, string Alias)[] folders) => folders
        .Select(folder => new StoredEmailEntity
        {
            MailboxAccountId = folder.AccountId,
            MailFolder = Folder(folder.AccountId, folder.Alias),
        })
        .AsQueryable();

    private static MailFolderEntity Folder(string accountId, string alias) => new()
    {
        MailboxAccountId = accountId,
        Alias = alias,
        RemotePath = alias,
        MailboxAccount = new MailboxAccountEntity { Id = accountId },
    };
}
