// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Rules.Actions;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.Rules;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Persistence.Users;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Rules;

/// <summary>Covers what a rule set is allowed to name and to ask for, read off the mailboxes a deployment serves.</summary>
/// <remarks>
/// There is one reading rather than two: every mailbox is an account record, so a reload and a write both ask this of
/// the declarations the served records bind to, and a claim inside one user's record is asked of that user's own
/// declarations — a rule set one accepts is one the other accepts because the reading is the same.
/// </remarks>
public sealed class DeclaredMailAccountsTests
{
    private static readonly Guid Primary = new("0199a0c0-0000-7000-8000-00000000000a");

    private static readonly Guid Work = new("0199a0c0-0000-7000-8000-00000000000b");

    /// <summary>An account record names its mailbox by the identifier the deployment generated, which is what a rule is scoped to.</summary>
    [Fact]
    public void ReadFrom_SeveralServedRecords_NamesEachUnderItsIdentifierInTheOrderGiven()
    {
        // Act
        var accounts = DeclaredMailAccounts.ReadFrom([Record(Work, "{}"), Record(Primary, "{}")]);

        // Assert
        Assert.Equal([Work.ToString("D"), Primary.ToString("D")], Identifiers(accounts));
    }

    /// <summary>A document this process could not bind, or would refuse to serve, is an account nobody is served, so it declares nothing a rule may name.</summary>
    [Theory]
    [InlineData("""{"NoSuchSetting":true}""")]
    [InlineData("[]")]
    [InlineData("""{"Port":0}""")]
    public void ReadFrom_ARecordWhoseDocumentDoesNotBind_DeclaresNothing(string document)
    {
        // Act
        var accounts = DeclaredMailAccounts.ReadFrom([Record(Primary, "{}"), Record(Work, document)]);

        // Assert
        Assert.Equal([Primary.ToString("D")], Identifiers(accounts));
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
        var accounts = DeclaredMailAccounts.ReadFrom(Array.Empty<MailAccountRecord>());

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
    /// A record is read as the declaration its document binds to, which is what a claim inside one user's record is
    /// judged by as well: a rule set the records accept is one that user's own declarations accept.
    /// </summary>
    [Fact]
    public void ReadFrom_ARecord_AnswersAsTheDeclarationItsDocumentBindsTo()
    {
        // Arrange
        var account = Account(Primary.ToString("D"));
        account.Folders = [new MailFolderMappingOptions { Alias = "quarantine", RemotePath = "Quarantine" }];
        account.RuleActions = new MailRuleActionPermissionOptions { Delete = true };
        var record = Record(
            Primary,
            """{"Folders":[{"Alias":"quarantine","RemotePath":"Quarantine"}],"RuleActions":{"Delete":true}}""");

        // Act
        var fromRecords = DeclaredMailAccounts.ReadFrom([record]);
        var fromDeclarations = DeclaredMailAccounts.ReadFrom([account]);

        // Assert
        Assert.Equal(Describe(fromDeclarations), Describe(fromRecords));
    }

    [Fact]
    public void ReadFrom_NoDeclarations_Throws()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(
            () => DeclaredMailAccounts.ReadFrom((IEnumerable<MailSynchronizationAccountOptions>)null!));
    }

    [Fact]
    public void ReadFrom_NoRecords_Throws()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(
            () => DeclaredMailAccounts.ReadFrom((IEnumerable<MailAccountRecord>)null!));
    }

    private static MailSynchronizationAccountOptions Account(string accountId) => new() { AccountId = accountId };

    private static MailAccountRecord Record(Guid accountId, string document) =>
        new(accountId, "alex@example.test", "Alex at work", ServableMailAccountDocuments.Completing(document), Version: 1);

    private static IReadOnlyList<string> Identifiers(IEnumerable<DeclaredMailAccount> accounts) =>
        [.. accounts.Select(account => account.AccountId)];

    /// <summary>Renders each account as text, because the read model holds collections that compare by reference.</summary>
    private static IReadOnlyList<string> Describe(IEnumerable<DeclaredMailAccount> accounts) =>
    [
        .. accounts.Select(account =>
            $"{account.AccountId}|{string.Join(',', account.MappedFolders.Select(folder => folder.Alias.Value))}|{account.PermittedRuleActions}"),
    ];
}
