// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Folders;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Synchronization;
using MailFathom.TestSupport;
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
        var scope = resolver.ReadableScope([MailAccountSelector.Create("acct-1")], [], JunkMailInclusion.Excluded);

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
        var scope = resolver.ReadableScope([MailAccountSelector.Create(named)], [], JunkMailInclusion.Excluded);

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
            [],
            JunkMailInclusion.Excluded);

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
            () => resolver.ReadableScope([MailAccountSelector.Create("ACCT-1")], [], JunkMailInclusion.Excluded));
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
            () => resolver.ReadableScope([MailAccountSelector.Create(named)], [], JunkMailInclusion.Excluded));

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
        var scope = resolver.ReadableScope([], [MailFolderAlias.Create("INBOX")], JunkMailInclusion.Excluded);

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
        var scope = resolver.ReadableScope([], [], JunkMailInclusion.Excluded);

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
        Assert.Throws<MailboxQueryFilterInvalidException>(() => resolver.ReadableScope(tooMany, [], JunkMailInclusion.Excluded));
    }

    /// <summary>A withheld folder reaches every read model through the scope, which is what makes one decision cover four tools.</summary>
    [Fact]
    public void ReadableScope_AFolderWithheldFromTools_CarriesItAsHiddenWhateverTheRequestNamed()
    {
        // Arrange
        var privateFolder = new MailFolderIdentity(Work.Id, MailFolderAlias.Create("PRIVATE"));
        var resolver = ResolverServing(StubMailFolderParticipation.Hiding(privateFolder), Work, Private);

        // Act
        var scope = resolver.ReadableScope([], [MailFolderAlias.Create("PRIVATE")], JunkMailInclusion.Excluded);

        // Assert
        Assert.Equal([privateFolder], scope.HiddenFolders);
        Assert.Equal([MailFolderAlias.Create("PRIVATE")], scope.FolderAliases);
    }

    /// <summary>Configuration that withholds nothing leaves the scope exactly as it was, so nothing pays for a switch it never set.</summary>
    [Fact]
    public void ReadableScope_NothingWithheld_CarriesNoHiddenFolder()
    {
        // Arrange
        var resolver = ResolverServing(Work);

        // Act
        var scope = resolver.ReadableScope([], [], JunkMailInclusion.Excluded);

        // Assert
        Assert.Empty(scope.HiddenFolders);
    }

    /// <summary>Two readings of one configuration must produce one predicate, so the hidden folders are ordered rather than left as read.</summary>
    [Fact]
    public void ReadableScope_SeveralFoldersWithheld_OrdersThemByAccountAndAlias()
    {
        // Arrange
        var resolver = ResolverServing(
            StubMailFolderParticipation.Hiding(
                new MailFolderIdentity(Private.Id, MailFolderAlias.Create("PRIVATE")),
                new MailFolderIdentity(Work.Id, MailFolderAlias.Create("SPAM")),
                new MailFolderIdentity(Work.Id, MailFolderAlias.Create("DRAFTS"))),
            Work,
            Private);

        // Act
        var scope = resolver.ReadableScope([], [], JunkMailInclusion.Excluded);

        // Assert
        Assert.Equal(
            [
                new MailFolderIdentity(Work.Id, MailFolderAlias.Create("DRAFTS")),
                new MailFolderIdentity(Work.Id, MailFolderAlias.Create("SPAM")),
                new MailFolderIdentity(Private.Id, MailFolderAlias.Create("PRIVATE")),
            ],
            scope.HiddenFolders);
    }

    /// <summary>The reads that reach an email by its identifier ask the same question, so a withheld folder is unreadable through them too.</summary>
    [Fact]
    public void IsReadableByTools_AFolderWithheldFromTools_IsNotReadable()
    {
        // Arrange
        var resolver = ResolverServing(
            StubMailFolderParticipation.Hiding(new MailFolderIdentity(Work.Id, MailFolderAlias.Create("PRIVATE"))),
            Work);

        // Act, Assert
        Assert.False(resolver.IsReadableByTools(Work.Id, MailFolderAlias.Create("PRIVATE")));
        Assert.True(resolver.IsReadableByTools(Work.Id, MailFolderAlias.Create("INBOX")));
    }

    /// <summary>An account the deployment stopped serving keeps its stored rows, and neither question may admit them.</summary>
    [Fact]
    public void IsReadableByTools_AnAccountTheDeploymentDoesNotServe_IsNotReadable()
    {
        // Arrange
        var resolver = ResolverServing(Work);

        // Act, Assert
        Assert.False(resolver.IsReadableByTools(Private.Id, MailFolderAlias.Create("INBOX")));
    }

    /// <summary>Junk is what a reader means by mail they never asked to see, so a read that says nothing about it gets none.</summary>
    [Fact]
    public void ReadableScope_ARequestSayingNothingAboutJunk_WithholdsEveryMappedJunkFolder()
    {
        // Arrange
        var junkFolder = new MailFolderIdentity(Work.Id, MailFolderAlias.Create("JUNK"));
        var resolver = ResolverServing(StubJunkMailFolderCatalog.Naming(junkFolder), Work, Private);

        // Act
        var scope = resolver.ReadableScope([], [], JunkMailInclusion.Excluded);

        // Assert
        Assert.Equal([junkFolder], scope.WithheldJunkFolders);
        Assert.Equal([junkFolder], scope.WithheldFolders);
        Assert.False(scope.IncludesJunkMail);
    }

    /// <summary>Somebody looking for a message a filter took is the whole reason the override exists.</summary>
    [Fact]
    public void ReadableScope_ARequestAskingForJunk_WithholdsNoneOfItAndRecordsTheAnswer()
    {
        // Arrange
        var resolver = ResolverServing(
            StubJunkMailFolderCatalog.Naming(new MailFolderIdentity(Work.Id, MailFolderAlias.Create("JUNK"))),
            Work);

        // Act
        var scope = resolver.ReadableScope([], [], JunkMailInclusion.Included);

        // Assert
        Assert.Empty(scope.WithheldJunkFolders);
        Assert.Empty(scope.WithheldFolders);
        Assert.True(scope.IncludesJunkMail);
    }

    /// <summary>The caller's answer may add mail back, never a folder the operator withheld from every tool.</summary>
    [Fact]
    public void ReadableScope_AJunkFolderAlsoWithheldFromTools_StaysWithheldWhenTheCallerAsksForJunk()
    {
        // Arrange
        var junkFolder = new MailFolderIdentity(Work.Id, MailFolderAlias.Create("JUNK"));
        var resolver = ResolverServing(
            StubMailFolderParticipation.Hiding(junkFolder),
            StubJunkMailFolderCatalog.Naming(junkFolder),
            Work);

        // Act
        var scope = resolver.ReadableScope([], [], JunkMailInclusion.Included);

        // Assert
        Assert.Equal([junkFolder], scope.WithheldFolders);
        Assert.True(scope.IncludesJunkMail);
    }

    /// <summary>A predicate does not care why a folder is out, so what a read consumes is the union of both decisions.</summary>
    [Fact]
    public void ReadableScope_AHiddenFolderBesideAJunkFolder_WithholdsBothAsOneList()
    {
        // Arrange
        var privateFolder = new MailFolderIdentity(Work.Id, MailFolderAlias.Create("PRIVATE"));
        var junkFolder = new MailFolderIdentity(Work.Id, MailFolderAlias.Create("JUNK"));
        var resolver = ResolverServing(
            StubMailFolderParticipation.Hiding(privateFolder),
            StubJunkMailFolderCatalog.Naming(junkFolder),
            Work);

        // Act
        var scope = resolver.ReadableScope([], [], JunkMailInclusion.Excluded);

        // Assert
        Assert.Equal([privateFolder], scope.HiddenFolders);
        Assert.Equal([junkFolder], scope.WithheldJunkFolders);
        Assert.Equal(
            [junkFolder, privateFolder],
            scope.WithheldFolders.OrderBy(folder => folder.Alias.Value, StringComparer.Ordinal));
    }

    /// <summary>One folder both decisions withhold is one folder, so a query is not handed the same exclusion twice.</summary>
    [Fact]
    public void ReadableScope_AFolderBothDecisionsWithhold_NamesItOnce()
    {
        // Arrange
        var junkFolder = new MailFolderIdentity(Work.Id, MailFolderAlias.Create("JUNK"));
        var resolver = ResolverServing(
            StubMailFolderParticipation.Hiding(junkFolder),
            StubJunkMailFolderCatalog.Naming(junkFolder),
            Work);

        // Act
        var scope = resolver.ReadableScope([], [], JunkMailInclusion.Excluded);

        // Assert
        Assert.Equal([junkFolder], scope.WithheldFolders);
    }

    /// <summary>Two readings of one configuration have to produce one predicate, whichever order the folders were read in.</summary>
    [Fact]
    public void ReadableScope_SeveralJunkFoldersWithheld_OrdersThemByAccountAndAlias()
    {
        // Arrange
        var resolver = ResolverServing(
            StubJunkMailFolderCatalog.Naming(
                new MailFolderIdentity(Private.Id, MailFolderAlias.Create("JUNK")),
                new MailFolderIdentity(Work.Id, MailFolderAlias.Create("SPAM")),
                new MailFolderIdentity(Work.Id, MailFolderAlias.Create("JUNK"))),
            Work,
            Private);

        // Act
        var scope = resolver.ReadableScope([], [], JunkMailInclusion.Excluded);

        // Assert
        Assert.Equal(
            [
                new MailFolderIdentity(Work.Id, MailFolderAlias.Create("JUNK")),
                new MailFolderIdentity(Work.Id, MailFolderAlias.Create("SPAM")),
                new MailFolderIdentity(Private.Id, MailFolderAlias.Create("JUNK")),
            ],
            scope.WithheldJunkFolders);
    }

    /// <summary>A deployment that maps no junk folder pays for nothing, and the caller's answer is still recorded.</summary>
    [Fact]
    public void ReadableScope_NoAccountMappingAJunkFolder_WithholdsNothingAndStillRecordsTheAnswer()
    {
        // Arrange
        var resolver = ResolverServing(Work);

        // Act
        var scope = resolver.ReadableScope([], [], JunkMailInclusion.Excluded);

        // Assert
        Assert.Empty(scope.WithheldJunkFolders);
        Assert.False(scope.IncludesJunkMail);
    }

    [Fact]
    public void ReadableScope_AnInclusionOutsideTheDeclaredSet_IsRefused()
    {
        // Arrange
        var resolver = ResolverServing(Work);

        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            resolver.ReadableScope([], [], (JunkMailInclusion)7));
    }

    private static MailboxScopeResolver ResolverServing(params ServedMailAccount[] servedAccounts) =>
        ResolverServing(StubMailFolderParticipation.Everything, StubJunkMailFolderCatalog.None, servedAccounts);

    private static MailboxScopeResolver ResolverServing(
        IMailFolderParticipationReader folderParticipation,
        params ServedMailAccount[] servedAccounts) =>
        ResolverServing(folderParticipation, StubJunkMailFolderCatalog.None, servedAccounts);

    private static MailboxScopeResolver ResolverServing(
        IJunkMailFolderCatalog junkFolders,
        params ServedMailAccount[] servedAccounts) =>
        ResolverServing(StubMailFolderParticipation.Everything, junkFolders, servedAccounts);

    private static MailboxScopeResolver ResolverServing(
        IMailFolderParticipationReader folderParticipation,
        IJunkMailFolderCatalog junkFolders,
        params ServedMailAccount[] servedAccounts)
    {
        var catalog = Substitute.For<IMailAccountCatalog>();
        catalog.ServedAccounts.Returns(
        [
            .. servedAccounts.OrderBy(account => account.Id.Value, StringComparer.Ordinal),
        ]);

        return new MailboxScopeResolver(catalog, folderParticipation, junkFolders);
    }
}
