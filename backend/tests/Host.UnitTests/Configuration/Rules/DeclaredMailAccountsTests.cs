// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Rules.Actions;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.Rules;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Rules;

/// <summary>Covers the two readings of one question: what a rule set is allowed to name and to ask for.</summary>
/// <remarks>
/// Composition reads keys and a reload reads a bound snapshot, so the two have to agree. A rule set startup accepted and
/// the first reload refused would be the failure, and it would arrive on an edit that changed nothing about the rules.
/// </remarks>
public sealed class DeclaredMailAccountsTests
{
    [Fact]
    public void ReadFrom_Configuration_NamesEveryDeclaredAccountInDeclaredOrder()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["MailSynchronization:Accounts:0:AccountId"] = "primary",
            ["MailSynchronization:Accounts:1:AccountId"] = "work",
        });

        // Act
        var accounts = DeclaredMailAccounts.ReadFrom(configuration);

        // Assert
        Assert.Equal(["primary", "work"], Identifiers(accounts));
    }

    /// <summary>A blank identifier is the synchronization section's own defect, so it is dropped rather than reported here.</summary>
    [Fact]
    public void ReadFrom_ConfigurationWithABlankIdentifier_LeavesItOut()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["MailSynchronization:Accounts:0:AccountId"] = "  primary  ",
            ["MailSynchronization:Accounts:1:AccountId"] = "   ",
        });

        // Act
        var accounts = DeclaredMailAccounts.ReadFrom(configuration);

        // Assert
        Assert.Equal(["primary"], Identifiers(accounts));
    }

    [Fact]
    public void ReadFrom_ConfigurationWithNoAccounts_NamesNothing()
    {
        // Arrange
        var configuration = new ConfigurationBuilder().Build();

        // Act
        var accounts = DeclaredMailAccounts.ReadFrom(configuration);

        // Assert
        Assert.Empty(accounts);
    }

    /// <summary>An account that configures no folder is run with the inbox mapping, so that is the folder a rule may file into.</summary>
    [Fact]
    public void ReadFrom_AccountDeclaringNoFolder_MapsTheInbox()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["MailSynchronization:Accounts:0:AccountId"] = "primary",
        });

        // Act
        var account = Assert.Single(DeclaredMailAccounts.ReadFrom(configuration));

        // Assert
        Assert.Equal(["INBOX"], account.MappedFolders.Select(folder => folder.Alias.Value));
    }

    /// <summary>A folder is resolved when a change first files into it, so mapping one is all a destination needs.</summary>
    [Fact]
    public void ReadFrom_FolderTheAccountDoesNotMirror_IsStillADestination()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["MailSynchronization:Accounts:0:AccountId"] = "primary",
            ["MailSynchronization:Accounts:0:Folders:0:Alias"] = "inbox",
            ["MailSynchronization:Accounts:0:Folders:0:SpecialUse"] = "Inbox",
            ["MailSynchronization:Accounts:0:Folders:1:Alias"] = "spam",
            ["MailSynchronization:Accounts:0:Folders:1:SpecialUse"] = "Junk",
            ["MailSynchronization:Accounts:0:Folders:1:Synchronize"] = "false",
        });

        // Act
        var account = Assert.Single(DeclaredMailAccounts.ReadFrom(configuration));

        // Assert
        Assert.Equal(["INBOX", "SPAM"], account.MappedFolders.Select(folder => folder.Alias.Value));
    }

    /// <summary>Deletion is opt-in on every account, and the three reversible actions are permitted until refused.</summary>
    [Fact]
    public void ReadFrom_AccountDeclaringNoRuleActions_PermitsEverythingButDeletion()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["MailSynchronization:Accounts:0:AccountId"] = "primary",
        });

        // Act
        var account = Assert.Single(DeclaredMailAccounts.ReadFrom(configuration));

        // Assert
        Assert.Equal(MailRuleActionPermissions.Default, account.PermittedRuleActions);
    }

    [Fact]
    public void ReadFrom_AccountNarrowingWhatRulesMayDo_ReadsEverySwitch()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["MailSynchronization:Accounts:0:AccountId"] = "primary",
            ["MailSynchronization:Accounts:0:RuleActions:Move"] = "false",
            ["MailSynchronization:Accounts:0:RuleActions:Copy"] = "false",
            ["MailSynchronization:Accounts:0:RuleActions:Delete"] = "true",
            ["MailSynchronization:Accounts:0:RuleActions:MarkAsRead"] = "false",
            ["MailSynchronization:Accounts:0:RuleActions:MarkAsFlagged"] = "false",
            ["MailSynchronization:Accounts:0:RuleActions:WriteKeywords"] = "false",
        });

        // Act
        var account = Assert.Single(DeclaredMailAccounts.ReadFrom(configuration));

        // Assert
        Assert.Equal(
            new MailRuleActionPermissions(
                PermitsRelocate: false,
                PermitsCopy: false,
                PermitsDelete: true,
                PermitsSetSeen: false,
                PermitsSetFlagged: false,
                PermitsWriteKeywords: false),
            account.PermittedRuleActions);
    }

    /// <summary>The bound reading is the one a reload uses, and it has to answer exactly as the key reading does.</summary>
    [Fact]
    public void ReadFrom_BoundSettings_AnswersAsTheConfigurationReadingDoes()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["MailSynchronization:Accounts:0:AccountId"] = "  primary  ",
            ["MailSynchronization:Accounts:0:Folders:0:Alias"] = "archive",
            ["MailSynchronization:Accounts:0:Folders:0:RemotePath"] = "Archive",
            ["MailSynchronization:Accounts:0:RuleActions:Delete"] = "true",
            ["MailSynchronization:Accounts:1:AccountId"] = "work",
        });
        var settings = new MailSynchronizationOptions
        {
            Accounts =
            [
                new MailSynchronizationAccountOptions
                {
                    AccountId = "  primary  ",
                    Folders = [new MailFolderMappingOptions { Alias = "archive", RemotePath = "Archive" }],
                    RuleActions = new MailRuleActionPermissionOptions { Delete = true },
                },
                new MailSynchronizationAccountOptions { AccountId = "work" },
            ],
        };

        // Act
        var fromConfiguration = DeclaredMailAccounts.ReadFrom(configuration);
        var fromSettings = DeclaredMailAccounts.ReadFrom(settings);

        // Assert
        Assert.Equal(Describe(fromConfiguration), Describe(fromSettings));
        Assert.Equal(["primary", "work"], Identifiers(fromSettings));
    }

    /// <summary>One owner's own declarations are read exactly as the deployment's are, which is what a claim in their record is judged by.</summary>
    /// <remarks>
    /// The overload exists so that a scanned folder or a junk destination in somebody's record resolves within their own
    /// accounts and nowhere else. Reading it differently from the deployment's would let a record be accepted for a
    /// folder the same mapping refuses in a file, or the reverse — so the comparison is against the key reading, which
    /// is a separate implementation, rather than against the bound overload this one is what implements.
    /// </remarks>
    [Fact]
    public void ReadFrom_OneOwnersOwnDeclarations_AnswersAsTheDeploymentsAreRead()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["MailSynchronization:Accounts:0:AccountId"] = "  alex-work  ",
            ["MailSynchronization:Accounts:0:Folders:0:Alias"] = "quarantine",
            ["MailSynchronization:Accounts:0:Folders:0:RemotePath"] = "Quarantine",
            ["MailSynchronization:Accounts:0:RuleActions:Delete"] = "true",
            ["MailSynchronization:Accounts:1:AccountId"] = "   ",
        });
        List<MailSynchronizationAccountOptions> accounts =
        [
            new MailSynchronizationAccountOptions
            {
                AccountId = "  alex-work  ",
                Folders = [new MailFolderMappingOptions { Alias = "quarantine", RemotePath = "Quarantine" }],
                RuleActions = new MailRuleActionPermissionOptions { Delete = true },
            },
            new MailSynchronizationAccountOptions { AccountId = "   " },
        ];

        // Act
        var fromOwner = DeclaredMailAccounts.ReadFrom(accounts);
        var fromConfiguration = DeclaredMailAccounts.ReadFrom(configuration);

        // Assert
        Assert.Equal(Describe(fromConfiguration), Describe(fromOwner));
        Assert.Equal(["alex-work"], Identifiers(fromOwner));
    }

    [Fact]
    public void ReadFrom_NoDeclarations_Throws()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(
            () => DeclaredMailAccounts.ReadFrom((IEnumerable<MailSynchronizationAccountOptions>)null!));
    }

    private static IConfiguration Configuration(Dictionary<string, string?> keys) =>
        new ConfigurationBuilder().AddInMemoryCollection(keys).Build();

    private static IReadOnlyList<string> Identifiers(IEnumerable<DeclaredMailAccount> accounts) =>
        [.. accounts.Select(account => account.AccountId)];

    /// <summary>Renders each account as text, because the read model holds collections that compare by reference.</summary>
    private static IReadOnlyList<string> Describe(IEnumerable<DeclaredMailAccount> accounts) =>
    [
        .. accounts.Select(account =>
            $"{account.AccountId}|{string.Join(',', account.MappedFolders.Select(folder => folder.Alias.Value))}|{account.PermittedRuleActions}"),
    ];
}
