// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Folders;
using MailFathom.Domain.Access;
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
        SyntheticMailOwner.Deployment,
        MailAccountId.Create("acct-1"),
        MailAccountDisplayName.Create("Work mail"),
        MailSynchronizationMode.Polling);

    private static readonly ServedMailAccount Private = new(
        SyntheticMailOwner.Deployment,
        MailAccountId.Create("acct-2"),
        MailAccountDisplayName.Create("Private mail"),
        MailSynchronizationMode.Push);

    /// <summary>The identifier two owners each declare, which is what makes both spellings ambiguous outside an owner.</summary>
    private static readonly MailAccountId SharedAccountId = MailAccountId.Create("shared");

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
        var scope = resolver.ReadableScope(
            [],
            [MailFolderReference.ToAlias(MailFolderAlias.Create("INBOX"))],
            JunkMailInclusion.Excluded);

        // Assert
        Assert.Equal([Work.Id, Private.Id], scope.AccountIds);
        Assert.Equal(
            [
                new MailFolderIdentity(Work.Id, MailFolderAlias.Create("INBOX")),
                new MailFolderIdentity(Private.Id, MailFolderAlias.Create("INBOX")),
            ],
            scope.SelectedFolders);
    }

    /// <summary>
    /// A deployment serving nothing resolves to nothing readable rather than to an unrestricted scope. The folders are
    /// asserted beside the accounts because an empty account list is what the persistence predicate reads as "every
    /// account": a scope that named no account and still admitted folders would publish the whole store to the one
    /// caller entitled to none of it.
    /// </summary>
    [Fact]
    public void ReadableScope_ADeploymentServingNoAccount_ReadsNothingRatherThanEverything()
    {
        // Arrange
        var resolver = ResolverServing(MappingAnInboxOnEachServedAccount());

        // Act
        var scope = resolver.ReadableScope([], [], JunkMailInclusion.Excluded);

        // Assert
        AssertNothingIsReadable(scope);
    }

    /// <summary>
    /// The same claim about the owner axis rather than about configuration: what an owner owns is resolved before any
    /// folder decision is applied, so a caller who owns no account reads nothing whatever the deployment serves.
    /// </summary>
    /// <remarks>
    /// The mapping is what makes this an assertion. The deployment's folder decisions are the deployment's rather than
    /// an owner's, so a resolver that dropped the empty-account guard would go on to apply them and return a scope
    /// naming no account beside every mapped folder — which the persistence predicate reads as every account. The
    /// control below resolves the same mapping for the owner who does own the accounts, so what the first assertion
    /// records is the owner axis rather than a mapping that admitted nothing to begin with.
    /// </remarks>
    [Fact]
    public void ReadableScope_AnOwnerWhoOwnsNoAccount_ReadsNothingRatherThanEverything()
    {
        // Arrange
        var resolver = ResolverFor(SyntheticMailOwner.Another, MappingAnInboxOnEachServedAccount(), Work, Private);

        // Act
        var scope = resolver.ReadableScope([], [], JunkMailInclusion.Excluded);

        // Assert
        AssertNothingIsReadable(scope);

        var forTheOwningOwner = ResolverFor(
                SyntheticMailOwner.Deployment,
                MappingAnInboxOnEachServedAccount(),
                Work,
                Private)
            .ReadableScope([], [], JunkMailInclusion.Excluded);

        Assert.NotEmpty(forTheOwningOwner.AccountIds);
        Assert.NotEmpty(forTheOwningOwner.ReadableFolders);
    }

    /// <summary>
    /// The caller's junk-mail answer is recorded on the empty scope too, because it is part of what a continuation
    /// cursor was issued for: a page whose fingerprint disagreed with the request that asked for it would refuse the
    /// caller's own cursor the moment they came to own an account.
    /// </summary>
    [Theory]
    [InlineData(JunkMailInclusion.Included, true)]
    [InlineData(JunkMailInclusion.Excluded, false)]
    public void ReadableScope_AnOwnerWhoOwnsNoAccount_StillRecordsWhatTheCallerAskedAboutJunkMail(
        JunkMailInclusion junkMail,
        bool recorded)
    {
        // Arrange
        var resolver = ResolverFor(SyntheticMailOwner.Another, MappingAnInboxOnEachServedAccount(), Work, Private);

        // Act
        var scope = resolver.ReadableScope([], [], junkMail);

        // Assert
        Assert.Equal(recorded, scope.IncludesJunkMail);
        AssertNothingIsReadable(scope);
    }

    /// <summary>
    /// An account another owner owns is refused exactly as one nobody serves is — the same exception carrying the same
    /// message. That is the whole of what such a caller learns, and it must not include that the account exists and
    /// belongs to somebody else.
    /// </summary>
    [Fact]
    public void ReadableScope_AnAccountAnotherOwnerOwns_IsRefusedTheSameWayAsOneNobodyServes()
    {
        // Arrange
        var resolver = ResolverFor(SyntheticMailOwner.Another, Work);
        var accountOfAnotherOwner = MailAccountSelector.Create(Work.Id.Value);

        // Act
        var refusedForAnotherOwnersAccount = Assert.Throws<MailAccountNotAccessibleException>(
            () => resolver.ReadableScope([accountOfAnotherOwner], [], JunkMailInclusion.Excluded));
        var refusedForNoSuchAccount = Assert.Throws<MailAccountNotAccessibleException>(
            () => ResolverFor(SyntheticMailOwner.Deployment, Work)
                .ReadableScope([MailAccountSelector.Create("no-such-account")], [], JunkMailInclusion.Excluded));

        // Assert
        Assert.Equal(accountOfAnotherOwner, refusedForAnotherOwnersAccount.RequestedAccount);
        Assert.Equal(
            refusedForNoSuchAccount.Message.Replace("no-such-account", Work.Id.Value, StringComparison.Ordinal),
            refusedForAnotherOwnersAccount.Message);
    }

    /// <summary>
    /// Both spellings are unique within their owner and nowhere wider, so two owners may each call an account the same
    /// thing. Each of them resolves their own, and the scope says whose by naming the owner beside the identifier the
    /// two share.
    /// </summary>
    [Theory]
    [InlineData("shared")]
    [InlineData("The shared mailbox")]
    public void ReadableScope_TwoOwnersHoldingIdenticallyNamedAccounts_EachResolvesTheirOwn(string named)
    {
        // Arrange
        var ofOneOwner = ResolverOwning(SyntheticMailOwner.Deployment, SharedlyNamedAccountOf(SyntheticMailOwner.Deployment));
        var ofAnotherOwner = ResolverOwning(SyntheticMailOwner.Another, SharedlyNamedAccountOf(SyntheticMailOwner.Another));

        // Act
        var forOneOwner = ofOneOwner.ReadableScope([MailAccountSelector.Create(named)], [], JunkMailInclusion.Excluded);
        var forAnotherOwner = ofAnotherOwner.ReadableScope([MailAccountSelector.Create(named)], [], JunkMailInclusion.Excluded);

        // Assert
        Assert.Equal([SharedAccountId], forOneOwner.AccountIds);
        Assert.Equal([SharedAccountId], forAnotherOwner.AccountIds);
        Assert.Equal(SyntheticMailOwner.Deployment, forOneOwner.Owner);
        Assert.Equal(SyntheticMailOwner.Another, forAnotherOwner.Owner);
    }

    /// <summary>
    /// An account only the other owner carries is not a name this owner can reach, and the refusal is the one text that
    /// names nothing meets — so a caller cannot use a name to learn what somebody else was given.
    /// </summary>
    [Fact]
    public void ReadableScope_AnAccountOnlyAnotherOwnerCarries_IsRefusedAsTextNamingNothingIs()
    {
        // Arrange
        var studio = SyntheticServedAccount.Of("studio", SyntheticMailOwner.Another);
        var resolver = ResolverOwning(SyntheticMailOwner.Deployment, SharedlyNamedAccountOf(SyntheticMailOwner.Deployment));
        var onlyTheOtherOwnerCarries = MailAccountSelector.Create(studio.Id.Value);

        // Act
        var refusedForTheirName = Assert.Throws<MailAccountNotAccessibleException>(
            () => resolver.ReadableScope([onlyTheOtherOwnerCarries], [], JunkMailInclusion.Excluded));
        var refusedForNoName = Assert.Throws<MailAccountNotAccessibleException>(
            () => resolver.ReadableScope([MailAccountSelector.Create("no-such-account")], [], JunkMailInclusion.Excluded));

        // Assert
        Assert.Equal(
            refusedForNoName.Message.Replace("no-such-account", onlyTheOtherOwnerCarries.Value, StringComparison.Ordinal),
            refusedForTheirName.Message);
        Assert.Equal(refusedForNoName.ErrorCode, refusedForTheirName.ErrorCode);

        // The control: the owner who does carry that name reaches their own account by it, so the refusal above is the
        // owner axis rather than a name nothing in this deployment is called.
        Assert.Equal(
            [studio.Id],
            ResolverOwning(SyntheticMailOwner.Another, studio)
                .ReadableScope([onlyTheOtherOwnerCarries], [], JunkMailInclusion.Excluded)
                .AccountIds);
    }

    /// <summary>The identity question a tool asks about one folder answers on the same owner scope as the listing does.</summary>
    [Fact]
    public void IsReadableByTools_AnAccountAnotherOwnerOwns_IsNotReadable()
    {
        // Arrange
        var inbox = new MailFolderIdentity(Work.Id, MailFolderAlias.Create("INBOX"));

        // Act, Assert
        Assert.False(
            ResolverFor(SyntheticMailOwner.Another, StubMailFolderParticipation.Mapping(inbox), Work)
                .IsReadableByTools(Work.Id, MailFolderAlias.Create("INBOX")));

        // The control: the same folder under the same mapping is readable for the owner who owns the account, so the
        // refusal above is the owner axis rather than a mapping that admitted nothing.
        Assert.True(
            ResolverFor(SyntheticMailOwner.Deployment, StubMailFolderParticipation.Mapping(inbox), Work)
                .IsReadableByTools(Work.Id, MailFolderAlias.Create("INBOX")));
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
        Assert.Throws<MailboxQueryFilterInvalidException>(
            () => resolver.ReadableScope(tooMany, [], JunkMailInclusion.Excluded));
    }

    /// <summary>A withheld folder reaches every read model through the scope, which is what makes one decision cover four tools.</summary>
    [Fact]
    public void ReadableScope_AFolderWithheldFromTools_LeavesItOutOfTheReadableFoldersWhateverTheRequestNamed()
    {
        // Arrange
        var privateFolder = new MailFolderIdentity(Work.Id, MailFolderAlias.Create("PRIVATE"));
        var inbox = new MailFolderIdentity(Work.Id, MailFolderAlias.Create("INBOX"));
        var resolver = ResolverServing(
            StubMailFolderParticipation.Mapping(inbox, privateFolder).Hiding(privateFolder),
            Work,
            Private);

        // Act
        var scope = resolver.ReadableScope(
            [],
            [MailFolderReference.ToAlias(MailFolderAlias.Create("PRIVATE"))],
            JunkMailInclusion.Excluded);

        // Assert
        Assert.Equal([inbox], scope.ReadableFolders);
        Assert.Contains(privateFolder, scope.SelectedFolders);
    }

    /// <summary>A folder no mapping names is not a folder this deployment has, so nothing admits it and no read reaches its stored mail.</summary>
    [Fact]
    public void ReadableScope_AnAliasNoMappingNames_IsNotAmongTheReadableFolders()
    {
        // Arrange
        var inbox = new MailFolderIdentity(Work.Id, MailFolderAlias.Create("INBOX"));
        var removed = new MailFolderIdentity(Work.Id, MailFolderAlias.Create("ARCHIVE"));
        var resolver = ResolverServing(StubMailFolderParticipation.Mapping(inbox), Work);

        // Act
        var scope = resolver.ReadableScope(
            [],
            [MailFolderReference.ToAlias(MailFolderAlias.Create("ARCHIVE"))],
            JunkMailInclusion.Excluded);

        // Assert
        Assert.Equal([inbox], scope.ReadableFolders);
        Assert.DoesNotContain(removed, scope.ReadableFolders);
        Assert.Contains(removed, scope.SelectedFolders);
    }

    /// <summary>A deployment whose configuration maps nothing has no folder to read, which is the opposite of reading every folder.</summary>
    [Fact]
    public void ReadableScope_ConfigurationMappingNoFolder_ReadsNoFolderAtAll()
    {
        // Arrange
        var resolver = ResolverServing(StubMailFolderParticipation.Nothing, Work);

        // Act
        var scope = resolver.ReadableScope([], [], JunkMailInclusion.Excluded);

        // Assert
        Assert.Empty(scope.ReadableFolders);
    }

    /// <summary>Two readings of one configuration must produce one predicate, so the readable folders are ordered rather than left as read.</summary>
    [Fact]
    public void ReadableScope_SeveralFoldersAdmitted_OrdersThemByAccountAndAlias()
    {
        // Arrange
        var resolver = ResolverServing(
            StubMailFolderParticipation.Mapping(
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
            scope.ReadableFolders);
    }

    /// <summary>The reads that reach an email by its identifier ask the same question, so a withheld folder is unreadable through them too.</summary>
    [Fact]
    public void IsReadableByTools_AFolderWithheldFromTools_IsNotReadable()
    {
        // Arrange
        var inbox = new MailFolderIdentity(Work.Id, MailFolderAlias.Create("INBOX"));
        var privateFolder = new MailFolderIdentity(Work.Id, MailFolderAlias.Create("PRIVATE"));
        var resolver = ResolverServing(
            StubMailFolderParticipation.Mapping(inbox, privateFolder).Hiding(privateFolder),
            Work);

        // Act, Assert
        Assert.False(resolver.IsReadableByTools(Work.Id, MailFolderAlias.Create("PRIVATE")));
        Assert.True(resolver.IsReadableByTools(Work.Id, MailFolderAlias.Create("INBOX")));
    }

    /// <summary>Stored mail under an alias no mapping names is unreachable through the two reads that name an email by its identifier.</summary>
    [Fact]
    public void IsReadableByTools_AnAliasNoMappingNames_IsNotReadable()
    {
        // Arrange
        var resolver = ResolverServing(
            StubMailFolderParticipation.Mapping(new MailFolderIdentity(Work.Id, MailFolderAlias.Create("INBOX"))),
            Work);

        // Act, Assert
        Assert.False(resolver.IsReadableByTools(Work.Id, MailFolderAlias.Create("ARCHIVE")));
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
        Assert.True(scope.IncludesJunkMail);
    }

    /// <summary>The caller's answer may add mail back, never a folder the operator withheld from every tool.</summary>
    [Fact]
    public void ReadableScope_AJunkFolderAlsoWithheldFromTools_StaysUnreadableWhenTheCallerAsksForJunk()
    {
        // Arrange
        var junkFolder = new MailFolderIdentity(Work.Id, MailFolderAlias.Create("JUNK"));
        var resolver = ResolverServing(
            StubMailFolderParticipation.Mapping(junkFolder).Hiding(junkFolder),
            StubJunkMailFolderCatalog.Naming(junkFolder),
            Work);

        // Act
        var scope = resolver.ReadableScope([], [], JunkMailInclusion.Included);

        // Assert
        Assert.DoesNotContain(junkFolder, scope.ReadableFolders);
        Assert.True(scope.IncludesJunkMail);
    }

    /// <summary>The two decisions narrow one query from opposite directions, and a read has to apply both.</summary>
    [Fact]
    public void ReadableScope_AWithheldFolderBesideAJunkFolder_AdmitsNeitherThroughTheOthersDecision()
    {
        // Arrange
        var inbox = new MailFolderIdentity(Work.Id, MailFolderAlias.Create("INBOX"));
        var privateFolder = new MailFolderIdentity(Work.Id, MailFolderAlias.Create("PRIVATE"));
        var junkFolder = new MailFolderIdentity(Work.Id, MailFolderAlias.Create("JUNK"));
        var resolver = ResolverServing(
            StubMailFolderParticipation.Mapping(inbox, privateFolder, junkFolder).Hiding(privateFolder),
            StubJunkMailFolderCatalog.Naming(junkFolder),
            Work);

        // Act
        var scope = resolver.ReadableScope([], [], JunkMailInclusion.Excluded);

        // Assert
        Assert.Equal([inbox, junkFolder], scope.ReadableFolders);
        Assert.Equal([junkFolder], scope.WithheldJunkFolders);
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

    /// <summary>A role names a different folder in each account, so a read across two of them narrows to both rather than to one name.</summary>
    [Fact]
    public void ReadableScope_AFolderNamedByItsRole_ResolvesToEachAccountsOwnFolder()
    {
        // Arrange
        var resolver = ResolverMapping(
            StubMailFolderMappings.Nothing
                .With(Work.Id, MailFolderMapping.ToRemotePath(
                    MailFolderAlias.Create("spam"),
                    RemoteFolderPath.Create("INBOX.Spam"),
                    specialUse: MailFolderSpecialUse.Junk))
                .With(Private.Id, MailFolderMapping.ToSpecialUse(
                    MailFolderAlias.Create("junk"),
                    MailFolderSpecialUse.Junk)),
            Work,
            Private);

        // Act
        var scope = resolver.ReadableScope(
            [],
            [MailFolderReference.ToRole(MailFolderSpecialUse.Junk)],
            JunkMailInclusion.Excluded);

        // Assert
        Assert.Equal(
            [
                new MailFolderIdentity(Work.Id, MailFolderAlias.Create("SPAM")),
                new MailFolderIdentity(Private.Id, MailFolderAlias.Create("JUNK")),
            ],
            scope.SelectedFolders);
    }

    /// <summary>An account without the folder contributes nothing rather than refusing the read for the accounts that have it.</summary>
    [Fact]
    public void ReadableScope_ARoleOnlyOneAccountInScopeMaps_ResolvesToThatAccountsFolderAlone()
    {
        // Arrange
        var resolver = ResolverMapping(
            StubMailFolderMappings.Nothing.With(Work.Id, MailFolderMapping.ToSpecialUse(
                MailFolderAlias.Create("archive"),
                MailFolderSpecialUse.Archive)),
            Work,
            Private);

        // Act
        var scope = resolver.ReadableScope(
            [],
            [MailFolderReference.ToRole(MailFolderSpecialUse.Archive)],
            JunkMailInclusion.Excluded);

        // Assert
        Assert.Equal(
            [new MailFolderIdentity(Work.Id, MailFolderAlias.Create("ARCHIVE"))],
            scope.SelectedFolders);
    }

    /// <summary>A role naming no folder anywhere in scope is a request nothing could satisfy, so it is refused rather than read as no filter.</summary>
    [Fact]
    public void ReadableScope_ARoleNoAccountInScopeMaps_IsRefusedNamingTheRole()
    {
        // Arrange
        var resolver = ResolverServing(Work, Private);

        // Act
        var failure = Assert.Throws<MailFolderRoleUnmappedException>(
            () => resolver.ReadableScope(
                [],
                [MailFolderReference.ToRole(MailFolderSpecialUse.Junk)],
                JunkMailInclusion.Excluded));

        // Assert
        Assert.Equal(MailFolderSpecialUse.Junk, failure.Role);
        Assert.Null(failure.AccountId);
        Assert.Contains("Junk", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>The ceiling counts what the caller wrote, so expanding one role over many accounts can never trip it.</summary>
    [Fact]
    public void ReadableScope_MoreFoldersNamedThanTheLimitPermits_IsRefusedAsAFilter()
    {
        // Arrange
        var resolver = ResolverServing(Work);
        var tooMany = Enumerable
            .Range(0, MailboxScope.MaximumFolders + 1)
            .Select(position => MailFolderReference.ToAlias(MailFolderAlias.Create($"folder-{position}")))
            .ToArray();

        // Act, Assert
        Assert.Throws<MailboxQueryFilterInvalidException>(
            () => resolver.ReadableScope([], tooMany, JunkMailInclusion.Excluded));
    }

    /// <summary>Asserts a scope admits no mail at all, which is three statements rather than one.</summary>
    /// <remarks>
    /// It is written out rather than compared against <see cref="MailboxScope.NothingReadable" />, because the scope's
    /// generated equality compares its collections by reference: two scopes that both admit nothing are unequal when
    /// one of them was built by a <c>with</c> expression. What matters here is that no account is named, that no folder
    /// is admitted, and that the two are true together — an empty account list alone is read as every account by the
    /// persistence predicate.
    /// </remarks>
    private static void AssertNothingIsReadable(MailboxScope scope)
    {
        Assert.Empty(scope.AccountIds);
        Assert.Empty(scope.ReadableFolders);
        Assert.Empty(scope.SelectedFolders);
    }

    /// <summary>Builds a mapping that admits a folder on both served accounts, so a scope admitting none is visible as one.</summary>
    /// <remarks>
    /// The folder decisions a deployment holds are the deployment's rather than any owner's, so a resolver that
    /// resolved a caller to no account and carried on would return this mapping's folders beside an empty account list.
    /// Arranging a mapping that admits something is what makes an assertion about the empty scope an assertion at all.
    /// A method rather than a field, because the stub mutates in place and returns itself: one instance shared between
    /// tests would carry the arrangement of whichever test first reached for <c>Hiding</c> into the others.
    /// </remarks>
    private static StubMailFolderParticipation MappingAnInboxOnEachServedAccount() =>
        StubMailFolderParticipation.Mapping(
            new MailFolderIdentity(Work.Id, MailFolderAlias.Create("INBOX")),
            new MailFolderIdentity(Private.Id, MailFolderAlias.Create("INBOX")));

    private static MailboxScopeResolver ResolverServing(params ServedMailAccount[] servedAccounts) =>
        Resolver(
            StubMailFolderParticipation.Nothing,
            StubJunkMailFolderCatalog.None,
            StubMailFolderMappings.Nothing,
            servedAccounts);

    private static MailboxScopeResolver ResolverServing(
        IMailFolderParticipationReader folderParticipation,
        params ServedMailAccount[] servedAccounts) =>
        Resolver(
            folderParticipation,
            StubJunkMailFolderCatalog.None,
            StubMailFolderMappings.Nothing,
            servedAccounts);

    private static MailboxScopeResolver ResolverServing(
        IJunkMailFolderCatalog junkFolders,
        params ServedMailAccount[] servedAccounts) =>
        Resolver(
            StubMailFolderParticipation.Nothing,
            junkFolders,
            StubMailFolderMappings.Nothing,
            servedAccounts);

    private static MailboxScopeResolver ResolverServing(
        IMailFolderParticipationReader folderParticipation,
        IJunkMailFolderCatalog junkFolders,
        params ServedMailAccount[] servedAccounts) =>
        Resolver(folderParticipation, junkFolders, StubMailFolderMappings.Nothing, servedAccounts);

    private static MailboxScopeResolver ResolverMapping(
        StubMailFolderMappings folderMappings,
        params ServedMailAccount[] servedAccounts) =>
        Resolver(
            StubMailFolderParticipation.Nothing,
            StubJunkMailFolderCatalog.None,
            folderMappings,
            servedAccounts);

    /// <summary>Composes the resolver over the real caller-scoped catalog, so what an owner owns is resolved rather than stated.</summary>
    /// <remarks>
    /// The other helpers substitute that catalog, because most of what this class covers is folder resolution and the
    /// owner is not part of it. These four claims are about the owner axis itself, so a substitute standing in for the
    /// decision under test would prove nothing.
    /// </remarks>
    private static MailboxScopeResolver ResolverFor(MailOwnerId owner, params ServedMailAccount[] servedAccounts) =>
        ResolverFor(owner, StubMailFolderParticipation.Nothing, servedAccounts);

    private static MailboxScopeResolver ResolverFor(
        MailOwnerId owner,
        IMailFolderParticipationReader folderParticipation,
        params ServedMailAccount[] servedAccounts) =>
        new(
            OwnedMailAccountCatalogs.For(AccessAuthorizations.ForOwnerGranted(owner), servedAccounts),
            folderParticipation,
            StubJunkMailFolderCatalog.None,
            StubMailFolderMappings.Nothing.Resolver);

    /// <summary>Builds the account both owners declare under one identifier and one display name.</summary>
    private static ServedMailAccount SharedlyNamedAccountOf(MailOwnerId owner) => new(
        owner,
        SharedAccountId,
        MailAccountDisplayName.Create("The shared mailbox"),
        MailSynchronizationMode.Polling);

    /// <summary>Composes the resolver over the accounts one owner owns, stated rather than derived from configuration.</summary>
    /// <remarks>
    /// The catalog is substituted here where <see cref="ResolverFor(MailOwnerId, ServedMailAccount[])" /> composes the
    /// real one, because these claims are about two owners each owning something and configuration holds one owner —
    /// <see cref="OwnedMailAccountCatalog" /> answers every configured account to that owner and none to anybody else,
    /// so the arrangement they need cannot be reached through it while accounts are declared in a file. What is under
    /// test is the naming decision the resolver takes over whatever set that port answered with, which is exactly what
    /// stating the set leaves in place.
    /// </remarks>
    private static MailboxScopeResolver ResolverOwning(MailOwnerId owner, params ServedMailAccount[] ownedAccounts)
    {
        var catalog = Substitute.For<ICallerMailAccountCatalog>();
        catalog.Owner.Returns(owner);
        catalog.OwnedAccounts.Returns([.. ownedAccounts.OrderBy(account => account.Id.Value, StringComparer.Ordinal)]);

        return new MailboxScopeResolver(
            catalog,
            StubMailFolderParticipation.Nothing,
            StubJunkMailFolderCatalog.None,
            StubMailFolderMappings.Nothing.Resolver);
    }

    private static MailboxScopeResolver Resolver(
        IMailFolderParticipationReader folderParticipation,
        IJunkMailFolderCatalog junkFolders,
        StubMailFolderMappings folderMappings,
        params ServedMailAccount[] servedAccounts)
    {
        var catalog = Substitute.For<ICallerMailAccountCatalog>();
        catalog.OwnedAccounts.Returns(
        [
            .. servedAccounts.OrderBy(account => account.Id.Value, StringComparer.Ordinal),
        ]);

        return new MailboxScopeResolver(catalog, folderParticipation, junkFolders, folderMappings.Resolver);
    }
}
