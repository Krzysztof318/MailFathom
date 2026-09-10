// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Rules.Actions;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.Rules;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Rules;

/// <summary>Covers what a rule set is allowed to name and to ask for, read off the mailboxes a deployment serves.</summary>
/// <remarks>
/// There is one reading rather than two: every mailbox is a user's own record, so a start and a reload both ask this of
/// bound declarations. The startup gate reads the roster it has just settled and a reload reads the roster the published
/// snapshot carries, and a rule set one accepts is one the other accepts because the reading is the same.
/// </remarks>
public sealed class DeclaredMailAccountsTests
{
    [Fact]
    public void ReadFrom_ARosterOfSeveralUsers_NamesEveryDeclaredAccountInDeclaredOrder()
    {
        // Act
        var accounts = DeclaredMailAccounts.ReadFrom(new MailSynchronizationOptions().WithServedUsers(
        [
            User(SyntheticMailUser.Deployment, Account("primary")),
            User(SyntheticMailUser.Another, Account("work")),
        ]));

        // Assert
        Assert.Equal(["primary", "work"], Identifiers(accounts));
    }

    /// <summary>A blank identifier is the record's own defect, so it is dropped rather than reported here under the wrong document.</summary>
    [Fact]
    public void ReadFrom_ADeclarationWithABlankIdentifier_LeavesItOut()
    {
        // Act
        var accounts = DeclaredMailAccounts.ReadFrom([Account("  primary  "), Account("   ")]);

        // Assert
        Assert.Equal(["primary"], Identifiers(accounts));
    }

    [Fact]
    public void ReadFrom_ADeploymentServingNobody_NamesNothing()
    {
        // Act
        var accounts = DeclaredMailAccounts.ReadFrom(new MailSynchronizationOptions());

        // Assert
        Assert.Empty(accounts);
    }

    /// <summary>An account that configures no folder is run with the inbox mapping, so that is the folder a rule may file into.</summary>
    [Fact]
    public void ReadFrom_AccountDeclaringNoFolder_MapsTheInbox()
    {
        // Act
        var account = Assert.Single(DeclaredMailAccounts.ReadFrom([Account("primary")]));

        // Assert
        Assert.Equal(["INBOX"], account.MappedFolders.Select(folder => folder.Alias.Value));
    }

    /// <summary>A folder is resolved when a change first files into it, so mapping one is all a destination needs.</summary>
    [Fact]
    public void ReadFrom_FolderTheAccountDoesNotMirror_IsStillADestination()
    {
        // Arrange
        var account = Account("primary");
        account.Folders =
        [
            new MailFolderMappingOptions { Alias = "inbox", SpecialUse = "Inbox" },
            new MailFolderMappingOptions { Alias = "spam", SpecialUse = "Junk", Synchronize = false },
        ];

        // Act
        var declared = Assert.Single(DeclaredMailAccounts.ReadFrom([account]));

        // Assert
        Assert.Equal(["INBOX", "SPAM"], declared.MappedFolders.Select(folder => folder.Alias.Value));
    }

    /// <summary>Deletion is opt-in on every account, and the three reversible actions are permitted until refused.</summary>
    [Fact]
    public void ReadFrom_AccountDeclaringNoRuleActions_PermitsEverythingButDeletion()
    {
        // Act
        var account = Assert.Single(DeclaredMailAccounts.ReadFrom([Account("primary")]));

        // Assert
        Assert.Equal(MailRuleActionPermissions.Default, account.PermittedRuleActions);
    }

    [Fact]
    public void ReadFrom_AccountNarrowingWhatRulesMayDo_ReadsEverySwitch()
    {
        // Arrange
        var account = Account("primary");
        account.RuleActions = new MailRuleActionPermissionOptions
        {
            Move = false,
            Copy = false,
            Delete = true,
            MarkAsRead = false,
            MarkAsFlagged = false,
            WriteKeywords = false,
        };

        // Act
        var declared = Assert.Single(DeclaredMailAccounts.ReadFrom([account]));

        // Assert
        Assert.Equal(
            new MailRuleActionPermissions(
                PermitsRelocate: false,
                PermitsCopy: false,
                PermitsDelete: true,
                PermitsSetSeen: false,
                PermitsSetFlagged: false,
                PermitsWriteKeywords: false),
            declared.PermittedRuleActions);
    }

    /// <summary>
    /// One user's own declarations are read exactly as the whole roster's are, which is what a claim inside their record
    /// is judged by: a scanned folder or a junk destination resolves within their own accounts and nowhere else.
    /// </summary>
    [Fact]
    public void ReadFrom_OneUsersOwnDeclarations_AnswersAsTheWholeRosterIsRead()
    {
        // Arrange
        var account = Account("  alex-work  ");
        account.Folders = [new MailFolderMappingOptions { Alias = "quarantine", RemotePath = "Quarantine" }];
        account.RuleActions = new MailRuleActionPermissionOptions { Delete = true };

        // Act
        var fromUser = DeclaredMailAccounts.ReadFrom([account, Account("   ")]);
        var fromRoster = DeclaredMailAccounts.ReadFrom(new MailSynchronizationOptions().WithServedUsers(
            [User(SyntheticMailUser.Deployment, account, Account("   "))]));

        // Assert
        Assert.Equal(Describe(fromRoster), Describe(fromUser));
        Assert.Equal(["alex-work"], Identifiers(fromUser));
    }

    [Fact]
    public void ReadFrom_NoDeclarations_Throws()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(
            () => DeclaredMailAccounts.ReadFrom((IEnumerable<MailSynchronizationAccountOptions>)null!));
    }

    [Fact]
    public void ReadFrom_NoSettings_Throws()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(
            () => DeclaredMailAccounts.ReadFrom((MailSynchronizationOptions)null!));
    }

    private static MailSynchronizationAccountOptions Account(string accountId) => new() { AccountId = accountId };

    private static ServedMailUser User(
        MailUserId user,
        params MailSynchronizationAccountOptions[] mailAccounts) =>
        new(user, $"user-{user.Value:D}", mailAccounts);

    private static IReadOnlyList<string> Identifiers(IEnumerable<DeclaredMailAccount> accounts) =>
        [.. accounts.Select(account => account.AccountId)];

    /// <summary>Renders each account as text, because the read model holds collections that compare by reference.</summary>
    private static IReadOnlyList<string> Describe(IEnumerable<DeclaredMailAccount> accounts) =>
    [
        .. accounts.Select(account =>
            $"{account.AccountId}|{string.Join(',', account.MappedFolders.Select(folder => folder.Alias.Value))}|{account.PermittedRuleActions}"),
    ];
}
