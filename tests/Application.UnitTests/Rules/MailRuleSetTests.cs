// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Rules;
using MailFathom.Application.Rules.Conditions;
using MailFathom.Application.UnitTests.TestDoubles;
using Xunit;

namespace MailFathom.Application.UnitTests.Rules;

/// <summary>Covers what a bound rule set refuses to be, and the ordering it promises to keep.</summary>
public sealed class MailRuleSetTests
{
    private static readonly MailRuleSetRevision Revision =
        MailRuleSetRevision.Create([new MailRuleDeclaration("rule", "isSeen", Actions: [], StopWhenMatched: false, Accounts: [])]);

    [Fact]
    public void Create_Rules_KeepsThemInTheOrderTheyWereDeclared()
    {
        // Arrange
        var rules = Enumerable
            .Range(0, 4)
            .Select(position => CreateRule($"rule-{position}"))
            .ToArray();

        // Act
        var ruleSet = MailRuleSet.Create(rules, Revision, MailRuleConditionBounds.Default);

        // Assert
        Assert.Equal(
            ["rule-0", "rule-1", "rule-2", "rule-3"],
            ruleSet.Rules.Select(rule => rule.Name));
        Assert.False(ruleSet.IsEmpty);
    }

    [Fact]
    public void Create_NoRules_IsAnEmptySetRatherThanARefusal()
    {
        // Act
        var ruleSet = MailRuleSet.Create([], Revision, MailRuleConditionBounds.Default);

        // Assert
        Assert.True(ruleSet.IsEmpty);
        Assert.Equal(Revision, ruleSet.Revision);
    }

    /// <summary>A rule is reported by its name, so two rules answering to one name could not be told apart afterwards.</summary>
    [Theory]
    [InlineData("duplicate")]
    [InlineData("DUPLICATE")]
    public void Create_TwoRulesUnderOneName_IsRefused(string secondName)
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(
            () => MailRuleSet.Create([CreateRule("duplicate"), CreateRule(secondName)], Revision, MailRuleConditionBounds.Default));
    }

    [Fact]
    public void Create_WithoutADerivedRevision_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => MailRuleSet.Create([CreateRule("rule")], default, MailRuleConditionBounds.Default));
    }

    [Fact]
    public void Create_Rules_CannotBeChangedThroughTheListItWasGiven()
    {
        // Arrange
        var rules = new List<MailRule> { CreateRule("rule") };
        var ruleSet = MailRuleSet.Create(rules, Revision, MailRuleConditionBounds.Default);

        // Act
        rules.Add(CreateRule("added-afterwards"));

        // Assert
        Assert.Single(ruleSet.Rules);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_RuleWithoutAName_IsRefused(string? name)
    {
        // Act, Assert
        Assert.ThrowsAny<ArgumentException>(
            () => MailRule.Create(name!, ScriptedMailRuleCondition.Answering(matches: true), stopWhenMatched: false));
    }

    [Fact]
    public void Create_RuleWithoutACondition_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => MailRule.Create("rule", null!, stopWhenMatched: false));
    }

    private static MailRule CreateRule(string name) =>
        MailRule.Create(name, ScriptedMailRuleCondition.Answering(matches: true), stopWhenMatched: false);
}
