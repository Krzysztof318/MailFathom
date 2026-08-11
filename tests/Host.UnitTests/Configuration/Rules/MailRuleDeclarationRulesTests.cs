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
    private readonly NCalcMailRuleConditionCompiler compiler = new();

    [Fact]
    public void FindDeclarationErrors_NoSection_IsADeploymentThatAppliesNoRules()
    {
        // Act
        var errors = MailRuleDeclarationRules.FindDeclarationErrors(candidate: null, this.compiler);

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
        var errors = MailRuleDeclarationRules.FindDeclarationErrors(candidate, this.compiler);

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
        var errors = MailRuleDeclarationRules.FindDeclarationErrors(candidate, this.compiler);

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
        var errors = MailRuleDeclarationRules.FindDeclarationErrors(candidate, this.compiler);

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
        var errors = MailRuleDeclarationRules.FindDeclarationErrors(candidate, this.compiler);

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
        var errors = MailRuleDeclarationRules.FindDeclarationErrors(candidate, this.compiler);

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
        var errors = MailRuleDeclarationRules.FindDeclarationErrors(candidate, this.compiler);

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
        var errors = MailRuleDeclarationRules.FindDeclarationErrors(candidate, this.compiler);

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
        var errors = MailRuleDeclarationRules.FindDeclarationErrors(candidate, this.compiler);

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
        var errors = MailRuleDeclarationRules.FindDeclarationErrors(candidate, this.compiler);

        // Assert
        Assert.Contains(errors, error => error.Contains("at most 10", StringComparison.Ordinal));
    }

    private static MailRuleOptions CreateRule(string name, string conditionText, bool enabled = true) =>
        new() { Name = name, Condition = conditionText, Enabled = enabled };
}
