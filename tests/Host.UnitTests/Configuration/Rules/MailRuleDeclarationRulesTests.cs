// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Rules.Actions;
using MailFathom.Domain.Folders;
using MailFathom.Host.Configuration.Rules;
using MailFathom.Infrastructure.Rules;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Rules;

/// <summary>Covers what a declared rule set has to satisfy before it can act on mail.</summary>
public sealed class MailRuleDeclarationRulesTests
{
    /// <summary>The characters a rule set's identity separates its own fields with, which no declared text may carry.</summary>
    private const char FieldSeparator = '\u001F';
    private const char AccountSeparator = '\u001D';

    /// <summary>The accounts the deployment declares, which is what a rule's scope, destinations, and actions are judged against.</summary>
    /// <remarks>Both mirror an archive folder and neither permits deletion, which is what an account says by saying nothing.</remarks>
    private static readonly DeclaredMailAccount[] DeclaredAccounts =
    [
        DeclaredAccount("primary"),
        DeclaredAccount("work"),
    ];

    private readonly NCalcMailRuleConditionCompiler compiler = new();

    [Fact]
    public void FindDeclarationErrors_NoSection_IsADeploymentThatAppliesNoRules()
    {
        // Act
        var errors = MailRuleDeclarationRules.FindDeclarationErrors(candidate: null, this.compiler, DeclaredAccounts);

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public void FindDeclarationErrors_UsableRuleSet_ReportsNothing()
    {
        // Arrange
        var candidate = new MailRulesOptions
        {
            Rules =
            [
                CreateRule("file-invoices", "senderDomain == 'supplier.test' and attachmentCount > 0"),
                CreateRule("archive-old", "ageInDays > 365"),
            ],
        };

        // Act
        var errors = MailRuleDeclarationRules.FindDeclarationErrors(candidate, this.compiler, DeclaredAccounts);

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>The binder's own validation does not descend into a collection, so a nameless rule is caught here.</summary>
    [Theory]
    [InlineData("", "isSeen")]
    [InlineData("   ", "isSeen")]
    [InlineData("file invoices@supplier.test", "isSeen")]
    [InlineData("file-invoices", "")]
    public void FindDeclarationErrors_RuleThatIsNotDeclaredProperly_IsRefusedByItsPosition(
        string name,
        string conditionText)
    {
        // Arrange
        var candidate = new MailRulesOptions { Rules = [CreateRule(name, conditionText)] };

        // Act
        var errors = MailRuleDeclarationRules.FindDeclarationErrors(candidate, this.compiler, DeclaredAccounts);

        // Assert
        Assert.Contains(errors, error => error.Contains("MailRules:Rules:0:", StringComparison.Ordinal));
    }

    [Fact]
    public void FindDeclarationErrors_ConditionNamingSomethingThatIsNotAFact_IsRefusedByItsRuleName()
    {
        // Arrange
        var candidate = new MailRulesOptions
        {
            Rules = [CreateRule("file-invoices", "senderMailbox == 'supplier.test'")],
        };

        // Act
        var errors = MailRuleDeclarationRules.FindDeclarationErrors(candidate, this.compiler, DeclaredAccounts);

        // Assert
        var error = Assert.Single(errors);
        Assert.Contains("MailRules:Rules", error, StringComparison.Ordinal);
        Assert.Contains("file-invoices", error, StringComparison.Ordinal);
        Assert.Contains("senderMailbox", error, StringComparison.Ordinal);
    }

    [Fact]
    public void FindDeclarationErrors_SeveralRulesWithBadConditions_ReportsEveryOneOfThem()
    {
        // Arrange
        var candidate = new MailRulesOptions
        {
            Rules =
            [
                CreateRule("first", "senderMailbox == 'a'"),
                CreateRule("second", "subject == 3"),
                CreateRule("third", "isSeen"),
            ],
        };

        // Act
        var errors = MailRuleDeclarationRules.FindDeclarationErrors(candidate, this.compiler, DeclaredAccounts);

        // Assert
        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, error => error.Contains("first", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("second", StringComparison.Ordinal));
    }

    /// <summary>A rule switched off is out of the set entirely, so nothing reads a condition it will never run.</summary>
    [Fact]
    public void FindDeclarationErrors_DisabledRuleWithABadCondition_IsNotRead()
    {
        // Arrange
        var candidate = new MailRulesOptions
        {
            Rules = [CreateRule("switched-off", "senderMailbox == 'a'", enabled: false)],
        };

        // Act
        var errors = MailRuleDeclarationRules.FindDeclarationErrors(candidate, this.compiler, DeclaredAccounts);

        // Assert
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("DUPLICATE")]
    public void FindDeclarationErrors_TwoRulesUnderOneName_IsRefused(string secondName)
    {
        // Arrange
        var candidate = new MailRulesOptions
        {
            Rules = [CreateRule("duplicate", "isSeen"), CreateRule(secondName, "isDraft")],
        };

        // Act
        var errors = MailRuleDeclarationRules.FindDeclarationErrors(candidate, this.compiler, DeclaredAccounts);

        // Assert
        Assert.Contains(errors, error => error.Contains("more than one rule named", StringComparison.Ordinal));
    }

    [Fact]
    public void FindDeclarationErrors_TimeoutThatCouldNeverElapse_IsRefusedBeforeAnyConditionIsRead()
    {
        // Arrange
        var candidate = new MailRulesOptions
        {
            ConditionEvaluationTimeout = TimeSpan.Zero,
            Rules = [CreateRule("file-invoices", "senderMailbox == 'supplier.test'")],
        };

        // Act
        var errors = MailRuleDeclarationRules.FindDeclarationErrors(candidate, this.compiler, DeclaredAccounts);

        // Assert
        var error = Assert.Single(errors);
        Assert.Contains("ConditionEvaluationTimeout", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, 16)]
    [InlineData(10_001, 16)]
    [InlineData(1_000, 0)]
    [InlineData(1_000, 65)]
    public void FindDeclarationErrors_LimitOutsideItsRange_IsRefused(int maxLength, int maxNestingDepth)
    {
        // Arrange
        var candidate = new MailRulesOptions
        {
            MaxConditionLength = maxLength,
            MaxConditionNestingDepth = maxNestingDepth,
            Rules = [CreateRule("file-invoices", "isSeen")],
        };

        // Act
        var errors = MailRuleDeclarationRules.FindDeclarationErrors(candidate, this.compiler, DeclaredAccounts);

        // Assert
        Assert.NotEmpty(errors);
    }

    /// <summary>The declared limits are what a condition is judged against, not the defaults behind them.</summary>
    [Fact]
    public void FindDeclarationErrors_ConditionLongerThanTheDeclaredLimit_IsRefused()
    {
        // Arrange
        var candidate = new MailRulesOptions
        {
            MaxConditionLength = 10,
            Rules = [CreateRule("file-invoices", "senderDomain == 'supplier.test'")],
        };

        // Act
        var errors = MailRuleDeclarationRules.FindDeclarationErrors(candidate, this.compiler, DeclaredAccounts);

        // Assert
        Assert.Contains(errors, error => error.Contains("at most 10", StringComparison.Ordinal));
    }

    /// <summary>One badly declared rule stops nothing: a rule set is fixed in one reading rather than one restart per defect.</summary>
    [Fact]
    public void FindDeclarationErrors_RuleThatIsNotDeclaredProperly_DoesNotHideAnotherRulesBadCondition()
    {
        // Arrange
        var candidate = new MailRulesOptions
        {
            Rules = [CreateRule(string.Empty, "isSeen"), CreateRule("second", "senderMailbox == 'a'")],
        };

        // Act
        var errors = MailRuleDeclarationRules.FindDeclarationErrors(candidate, this.compiler, DeclaredAccounts);

        // Assert
        Assert.Contains(errors, error => error.Contains("MailRules:Rules:0:Name", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("senderMailbox", StringComparison.Ordinal));
    }

    /// <summary>A limit nothing can read a condition under is where reading stops, because there is nothing to read against.</summary>
    [Fact]
    public void FindDeclarationErrors_UnusableLimit_IsReportedWithoutReadingAnyCondition()
    {
        // Arrange
        var candidate = new MailRulesOptions
        {
            MaxConditionNestingDepth = 0,
            Rules = [CreateRule("file-invoices", "senderMailbox == 'a'")],
        };

        // Act
        var errors = MailRuleDeclarationRules.FindDeclarationErrors(candidate, this.compiler, DeclaredAccounts);

        // Assert
        Assert.All(errors, error => Assert.DoesNotContain("senderMailbox", error, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("00:00:31")]
    [InlineData("01:00:00")]
    public void FindDeclarationErrors_TimeoutAboveTheCeiling_IsRefused(string timeout)
    {
        // Arrange
        var candidate = new MailRulesOptions
        {
            ConditionEvaluationTimeout = TimeSpan.Parse(timeout, CultureInfo.InvariantCulture),
            Rules = [CreateRule("file-invoices", "isSeen")],
        };

        // Act
        var errors = MailRuleDeclarationRules.FindDeclarationErrors(candidate, this.compiler, DeclaredAccounts);

        // Assert
        Assert.Contains(errors, error => error.Contains("ConditionEvaluationTimeout", StringComparison.Ordinal));
    }

    /// <summary>A retention nobody could justify holding is what a storage-limitation setting exists to prevent.</summary>
    [Fact]
    public void FindDeclarationErrors_HistoryRetentionAboveTheCeiling_IsRefused()
    {
        // Arrange
        var candidate = new MailRulesOptions
        {
            HistoryRetention = MailRulesOptions.LongestHistoryRetention + TimeSpan.FromDays(1),
            Rules = [CreateRule("file-invoices", "isSeen")],
        };

        // Act
        var errors = MailRuleDeclarationRules.FindDeclarationErrors(candidate, this.compiler, DeclaredAccounts);

        // Assert
        Assert.Contains(errors, error => error.Contains("HistoryRetention", StringComparison.Ordinal));
    }

    /// <summary>Zero names no window rather than an unreadable one, so it is the deployment declaring what it keeps.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void FindDeclarationErrors_HistoryRetentionOfZeroOrLess_IsAccepted(int days)
    {
        // Arrange
        var candidate = new MailRulesOptions
        {
            HistoryRetention = TimeSpan.FromDays(days),
            Rules = [CreateRule("file-invoices", "isSeen")],
        };

        // Act
        var errors = MailRuleDeclarationRules.FindDeclarationErrors(candidate, this.compiler, DeclaredAccounts);

        // Assert
        Assert.DoesNotContain(errors, error => error.Contains("HistoryRetention", StringComparison.Ordinal));
    }

    /// <summary>The identity is a digest over declared text, so no declared text may carry what separates its fields.</summary>
    [Fact]
    public void FindDeclarationErrors_ConditionCarryingADigestSeparator_IsRefused()
    {
        // Arrange
        var candidate = new MailRulesOptions
        {
            Rules = [CreateRule("file-invoices", $"subject == 'a{FieldSeparator}b'")],
        };

        // Act
        var errors = MailRuleDeclarationRules.FindDeclarationErrors(candidate, this.compiler, DeclaredAccounts);

        // Assert
        Assert.Contains(errors, error => error.Contains("MailRules:Rules:0:Condition", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("separator character", StringComparison.Ordinal));
    }

    [Fact]
    public void FindDeclarationErrors_ScopeCarryingADigestSeparator_IsRefused()
    {
        // Arrange
        var candidate = new MailRulesOptions
        {
            Rules = [CreateRule("file-invoices", "isSeen", accounts: [$"primary{AccountSeparator}work"])],
        };

        // Act
        var errors = MailRuleDeclarationRules.FindDeclarationErrors(candidate, this.compiler, DeclaredAccounts);

        // Assert
        Assert.Contains(errors, error => error.Contains("MailRules:Rules:0:Accounts", StringComparison.Ordinal));
    }

    [Fact]
    public void FindDeclarationErrors_RuleScopedToDeclaredAccounts_ReportsNothing()
    {
        // Arrange
        var candidate = new MailRulesOptions
        {
            Rules = [CreateRule("file-invoices", "isSeen", accounts: ["primary", "work"])],
        };

        // Act
        var errors = MailRuleDeclarationRules.FindDeclarationErrors(candidate, this.compiler, DeclaredAccounts);

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>A scope naming an account nobody declared reaches no mail, so it is refused rather than left silent.</summary>
    [Theory]
    [InlineData("archive")]
    [InlineData("Primary")]
    public void FindDeclarationErrors_RuleScopedToAnAccountNobodyDeclared_IsRefused(string account)
    {
        // Arrange
        var candidate = new MailRulesOptions { Rules = [CreateRule("file-invoices", "isSeen", accounts: [account])] };

        // Act
        var errors = MailRuleDeclarationRules.FindDeclarationErrors(candidate, this.compiler, DeclaredAccounts);

        // Assert
        var error = Assert.Single(errors);
        Assert.Contains("MailRules:Rules:0:Accounts", error, StringComparison.Ordinal);
        Assert.Contains(account, error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void FindDeclarationErrors_ScopeNamingNothing_IsRefused(string account)
    {
        // Arrange
        var candidate = new MailRulesOptions { Rules = [CreateRule("file-invoices", "isSeen", accounts: [account])] };

        // Act
        var errors = MailRuleDeclarationRules.FindDeclarationErrors(candidate, this.compiler, DeclaredAccounts);

        // Assert
        Assert.Contains(errors, error => error.Contains("named by nothing", StringComparison.Ordinal));
    }

    [Fact]
    public void FindDeclarationErrors_ScopeNamingOneAccountTwice_IsRefused()
    {
        // Arrange
        var candidate = new MailRulesOptions
        {
            Rules = [CreateRule("file-invoices", "isSeen", accounts: ["primary", "primary"])],
        };

        // Act
        var errors = MailRuleDeclarationRules.FindDeclarationErrors(candidate, this.compiler, DeclaredAccounts);

        // Assert
        Assert.Contains(errors, error => error.Contains("named more than once", StringComparison.Ordinal));
    }

    /// <summary>A rule naming no account is the general case rather than a rule with an empty scope to complain about.</summary>
    [Fact]
    public void FindDeclarationErrors_RuleThatNamesNoAccount_ReportsNothing()
    {
        // Arrange
        var candidate = new MailRulesOptions { Rules = [CreateRule("file-invoices", "isSeen")] };

        // Act
        var errors = MailRuleDeclarationRules.FindDeclarationErrors(candidate, this.compiler, DeclaredAccounts);

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>A deployment that declares no account has nothing a scope could name, and a general rule is still usable.</summary>
    [Fact]
    public void FindDeclarationErrors_ScopeWhereNoAccountIsDeclared_IsRefusedWhileAGeneralRuleIsNot()
    {
        // Arrange
        var scoped = new MailRulesOptions { Rules = [CreateRule("scoped", "isSeen", accounts: ["primary"])] };
        var general = new MailRulesOptions { Rules = [CreateRule("general", "isSeen")] };

        // Act
        var scopedErrors = MailRuleDeclarationRules.FindDeclarationErrors(scoped, this.compiler, []);
        var generalErrors = MailRuleDeclarationRules.FindDeclarationErrors(general, this.compiler, []);

        // Assert
        Assert.NotEmpty(scopedErrors);
        Assert.Empty(generalErrors);
    }

    /// <summary>The ordinary combinations are what an owner writes most, so accepting them is worth asserting outright.</summary>
    [Fact]
    public void FindDeclarationErrors_ARuleFilingMailAndMarkingItRead_ReportsNothing()
    {
        // Arrange
        var candidate = new MailRulesOptions
        {
            Rules =
            [
                CreateRule(
                    "file-invoices",
                    "isSeen",
                    actions: new MailRuleActionOptions { MoveTo = "archive", MarkAsRead = true }),
            ],
        };

        // Act
        var errors = MailRuleDeclarationRules.FindDeclarationErrors(candidate, this.compiler, DeclaredAccounts);

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>A combination naming two fates for one occurrence is refused where it is written, not resolved at run time.</summary>
    [Fact]
    public void FindDeclarationErrors_ARuleFilingAndDeletingOneEmail_IsRefusedNamingTheRule()
    {
        // Arrange
        var permitting = DeclaredAccount("primary", MailRuleActionPermissions.Default with { PermitsDelete = true });
        var candidate = new MailRulesOptions
        {
            Rules =
            [
                CreateRule(
                    "file-invoices",
                    "isSeen",
                    accounts: ["primary"],
                    actions: new MailRuleActionOptions { MoveTo = "archive", Delete = true }),
            ],
        };

        // Act
        var errors = MailRuleDeclarationRules.FindDeclarationErrors(candidate, this.compiler, [permitting]);

        // Assert
        var error = Assert.Single(errors);
        Assert.Contains("MailRules:Rules:0:Actions", error, StringComparison.Ordinal);
        Assert.Contains("file-invoices", error, StringComparison.Ordinal);
    }

    /// <summary>Deletion is opt-in, so a rule declaring it over an account that permits none is refused rather than skipped.</summary>
    [Fact]
    public void FindDeclarationErrors_ARuleDeletingMailOnAnAccountThatRefusesDeletion_IsRefused()
    {
        // Arrange
        var candidate = new MailRulesOptions
        {
            Rules = [CreateRule("drop-notifications", "isSeen", actions: new MailRuleActionOptions { Delete = true })],
        };

        // Act
        var errors = MailRuleDeclarationRules.FindDeclarationErrors(candidate, this.compiler, DeclaredAccounts);

        // Assert
        Assert.Equal(2, errors.Count);
        Assert.All(errors, error => Assert.Contains("does not permit", error, StringComparison.Ordinal));
    }

    /// <summary>An account that permits the action accepts the same rule, which is what makes the refusal a decision rather than a ban.</summary>
    [Fact]
    public void FindDeclarationErrors_ARuleDeletingMailOnAnAccountThatPermitsIt_ReportsNothing()
    {
        // Arrange
        var permitting = DeclaredAccount(
            "primary",
            MailRuleActionPermissions.Default with { PermitsDelete = true });
        var candidate = new MailRulesOptions
        {
            Rules =
            [
                CreateRule(
                    "drop-notifications",
                    "isSeen",
                    accounts: ["primary"],
                    actions: new MailRuleActionOptions { Delete = true }),
            ],
        };

        // Act
        var errors = MailRuleDeclarationRules.FindDeclarationErrors(candidate, this.compiler, [permitting]);

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>Nothing binds an unmirrored folder to a remote path, so a rule filing into one could never resolve a destination.</summary>
    [Fact]
    public void FindDeclarationErrors_ARuleFilingIntoAFolderTheAccountDoesNotMirror_IsRefused()
    {
        // Arrange
        var candidate = new MailRulesOptions
        {
            Rules =
            [
                CreateRule(
                    "file-invoices",
                    "isSeen",
                    accounts: ["primary"],
                    actions: new MailRuleActionOptions { MoveTo = "nowhere" }),
            ],
        };

        // Act
        var errors = MailRuleDeclarationRules.FindDeclarationErrors(candidate, this.compiler, DeclaredAccounts);

        // Assert
        var error = Assert.Single(errors);
        Assert.Contains("NOWHERE", error, StringComparison.Ordinal);
        Assert.Contains("primary", error, StringComparison.Ordinal);
    }

    /// <summary>An unscoped rule reaches every account, so a destination one of them does not mirror is refused for that one.</summary>
    [Fact]
    public void FindDeclarationErrors_AnUnscopedRuleFilingIntoAFolderOneAccountLacks_IsRefusedForThatAccount()
    {
        // Arrange
        var accounts = new[] { DeclaredAccount("primary"), NoFolderAccount("work") };
        var candidate = new MailRulesOptions
        {
            Rules = [CreateRule("file-invoices", "isSeen", actions: new MailRuleActionOptions { MoveTo = "archive" })],
        };

        // Act
        var errors = MailRuleDeclarationRules.FindDeclarationErrors(candidate, this.compiler, accounts);

        // Assert
        var error = Assert.Single(errors);
        Assert.Contains("work", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void FindDeclarationErrors_ADestinationNamedByNothing_IsRefused(string destination)
    {
        // Arrange
        var candidate = new MailRulesOptions
        {
            Rules =
            [
                CreateRule("file-invoices", "isSeen", actions: new MailRuleActionOptions { MoveTo = destination }),
            ],
        };

        // Act
        var errors = MailRuleDeclarationRules.FindDeclarationErrors(candidate, this.compiler, DeclaredAccounts);

        // Assert
        Assert.Contains(
            errors,
            error => error.Contains("MailRules:Rules:0:Actions", StringComparison.Ordinal));
    }

    /// <summary>A rule written for several deployments names what the folder is for, and the account's own alias for it stays configuration.</summary>
    [Fact]
    public void FindDeclarationErrors_ADestinationNamedByARoleTheAccountMaps_IsAccepted()
    {
        // Arrange
        var candidate = new MailRulesOptions
        {
            Rules =
            [
                CreateRule("file-invoices", "isSeen", actions: new MailRuleActionOptions { MoveTo = "role:Archive" }),
            ],
        };

        // Act
        var errors = MailRuleDeclarationRules.FindDeclarationErrors(candidate, this.compiler, DeclaredAccounts);

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>A role nothing carries is refused at binding, exactly as an alias nothing mirrors is, so neither reaches a mailbox.</summary>
    [Fact]
    public void FindDeclarationErrors_ADestinationNamedByARoleNoFolderPlays_IsRefusedForThatAccount()
    {
        // Arrange
        var candidate = new MailRulesOptions
        {
            Rules =
            [
                CreateRule(
                    "file-spam",
                    "isSeen",
                    accounts: ["primary"],
                    actions: new MailRuleActionOptions { MoveTo = "role:Junk" }),
            ],
        };

        // Act
        var errors = MailRuleDeclarationRules.FindDeclarationErrors(candidate, this.compiler, DeclaredAccounts);

        // Assert
        var error = Assert.Single(errors);
        Assert.Contains("role:Junk", error, StringComparison.Ordinal);
        Assert.Contains("primary", error, StringComparison.Ordinal);
    }

    /// <summary>A role misspelled is a rule that would file nowhere, and the refusal says which spellings exist.</summary>
    [Fact]
    public void FindDeclarationErrors_ADestinationNamingARoleThatDoesNotExist_IsRefusedNamingTheRolesThatDo()
    {
        // Arrange
        var candidate = new MailRulesOptions
        {
            Rules =
            [
                CreateRule("file-invoices", "isSeen", actions: new MailRuleActionOptions { MoveTo = "role:Spam" }),
            ],
        };

        // Act
        var errors = MailRuleDeclarationRules.FindDeclarationErrors(candidate, this.compiler, DeclaredAccounts);

        // Assert
        var error = Assert.Single(errors);
        Assert.Contains("role:", error, StringComparison.Ordinal);
        Assert.Contains(nameof(MailFolderSpecialUse.Junk), error, StringComparison.Ordinal);
    }

    /// <summary>The identity is a digest over the declarations, so a destination carrying a separator could blur two rule sets into one.</summary>
    [Fact]
    public void FindDeclarationErrors_ADestinationCarryingADigestSeparator_IsRefused()
    {
        // Arrange
        var candidate = new MailRulesOptions
        {
            Rules =
            [
                CreateRule(
                    "file-invoices",
                    "isSeen",
                    actions: new MailRuleActionOptions
                    {
                        MoveTo = string.Create(CultureInfo.InvariantCulture, $"arch{FieldSeparator}ive"),
                    }),
            ],
        };

        // Act
        var errors = MailRuleDeclarationRules.FindDeclarationErrors(candidate, this.compiler, DeclaredAccounts);

        // Assert
        Assert.Contains(
            errors,
            error => error.Contains("MailRules:Rules:0:Actions", StringComparison.Ordinal));
    }

    /// <summary>The name is read the way the binder reads every other closed vocabulary this configuration declares.</summary>
    [Fact]
    public void FindDeclarationErrors_TheTriggerNamedInEitherSpelling_ReportsNothing()
    {
        // Arrange
        var candidate = new MailRulesOptions
        {
            Rules =
            [
                CreateRule("as-documented", "isSeen", triggers: ["Arrival"]),
                CreateRule("in-another-case", "isDraft", triggers: ["arrival"]),
            ],
        };

        // Act
        var errors = MailRuleDeclarationRules.FindDeclarationErrors(candidate, this.compiler, DeclaredAccounts);

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>An empty list is a usable declaration rather than a rule that forgot to say something.</summary>
    [Fact]
    public void FindDeclarationErrors_ARuleDeclaringNoTriggerAtAll_ReportsNothing()
    {
        // Arrange
        var candidate = new MailRulesOptions { Rules = [CreateRule("housekeeping", "isSeen", triggers: [])] };

        // Act
        var errors = MailRuleDeclarationRules.FindDeclarationErrors(candidate, this.compiler, DeclaredAccounts);

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>A mistyped name dropped in silence would turn an automatic rule into one only a requested run applies.</summary>
    [Fact]
    public void FindDeclarationErrors_ATriggerNothingDeclares_IsRefusedNamingTheRuleAndTheValue()
    {
        // Arrange
        var candidate = new MailRulesOptions { Rules = [CreateRule("housekeeping", "isSeen", triggers: ["Schedule"])] };

        // Act
        var errors = MailRuleDeclarationRules.FindDeclarationErrors(candidate, this.compiler, DeclaredAccounts);

        // Assert
        var error = Assert.Single(errors);
        Assert.Contains("MailRules:Rules:0:Triggers", error, StringComparison.Ordinal);
        Assert.Contains("Schedule", error, StringComparison.Ordinal);
        Assert.Contains("'Arrival'", error, StringComparison.Ordinal);
    }

    /// <summary>The value is a set, so naming one trigger twice says nothing the rule does not already say.</summary>
    [Fact]
    public void FindDeclarationErrors_ATriggerNamedTwice_IsRefused()
    {
        // Arrange
        var candidate = new MailRulesOptions
        {
            Rules = [CreateRule("housekeeping", "isSeen", triggers: ["Arrival", "arrival"])],
        };

        // Act
        var errors = MailRuleDeclarationRules.FindDeclarationErrors(candidate, this.compiler, DeclaredAccounts);

        // Assert
        var error = Assert.Single(errors);
        Assert.Contains("MailRules:Rules:0:Triggers", error, StringComparison.Ordinal);
        Assert.Contains("more than once", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void FindDeclarationErrors_ATriggerNamedByNothing_IsRefused(string trigger)
    {
        // Arrange
        var candidate = new MailRulesOptions
        {
            Rules = [CreateRule("housekeeping", "isSeen", triggers: ["Arrival", trigger])],
        };

        // Act
        var errors = MailRuleDeclarationRules.FindDeclarationErrors(candidate, this.compiler, DeclaredAccounts);

        // Assert
        var error = Assert.Single(errors);
        Assert.Contains("MailRules:Rules:0:Triggers", error, StringComparison.Ordinal);
        Assert.Contains("named by nothing", error, StringComparison.Ordinal);
    }

    /// <summary>A rule switched off is judged like any other, exactly as its scope and its actions already are.</summary>
    [Fact]
    public void FindDeclarationErrors_ADisabledRuleDeclaringAnUnknownTrigger_IsStillRefused()
    {
        // Arrange
        var candidate = new MailRulesOptions
        {
            Rules = [CreateRule("switched-off", "isSeen", enabled: false, triggers: ["Whenever"])],
        };

        // Act
        var errors = MailRuleDeclarationRules.FindDeclarationErrors(candidate, this.compiler, DeclaredAccounts);

        // Assert
        Assert.Contains(errors, error => error.Contains("MailRules:Rules:0:Triggers", StringComparison.Ordinal));
    }

    private static MailRuleOptions CreateRule(
        string name,
        string conditionText,
        bool enabled = true,
        string[]? accounts = null,
        MailRuleActionOptions? actions = null,
        string[]? triggers = null) =>
        new()
        {
            Name = name,
            Condition = conditionText,
            Enabled = enabled,
            Accounts = accounts ?? [],
            Actions = actions ?? new MailRuleActionOptions(),
            Triggers = triggers ?? [],
        };

    /// <summary>One declared account, mirroring an archive folder and permitting whatever the caller says it does.</summary>
    private static DeclaredMailAccount DeclaredAccount(
        string accountId,
        MailRuleActionPermissions? permissions = null) =>
        new(
            accountId,
            [new DeclaredMailFolder(MailFolderAlias.Create("archive"), MailFolderSpecialUse.Archive)],
            permissions ?? MailRuleActionPermissions.Default);

    /// <summary>One declared account mirroring nothing a rule could file into.</summary>
    private static DeclaredMailAccount NoFolderAccount(string accountId) =>
        new(accountId, [], MailRuleActionPermissions.Default);
}
