// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Rules;
using MailFathom.Infrastructure.Rules;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Rules;

/// <summary>Covers what a declared rule set has to satisfy before it can act on mail.</summary>
public sealed class MailRuleDeclarationRulesTests
{
    /// <summary>The accounts the deployment declares, which is what a rule's scope is judged against.</summary>
    private static readonly string[] DeclaredAccounts = ["primary", "work"];

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

    private static MailRuleOptions CreateRule(
        string name,
        string conditionText,
        bool enabled = true,
        string[]? accounts = null) =>
        new()
        {
            Name = name,
            Condition = conditionText,
            Enabled = enabled,
            Accounts = accounts ?? [],
        };
}
