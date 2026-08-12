// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Rules;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Rules;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Rules;

/// <summary>Covers what a declared rule set becomes, and how often a published configuration is read to build it.</summary>
public sealed class MailRuleSetMappingTests
{
    private readonly NCalcMailRuleConditionCompiler compiler = new();

    [Fact]
    public void Map_DeclaredRules_KeepsThemInTheOrderTheyWereWritten()
    {
        // Arrange
        var settings = new MailRulesOptions
        {
            Rules =
            [
                CreateRule("file-invoices", "senderDomain == 'supplier.test'", stopWhenMatched: true),
                CreateRule("archive-old", "ageInDays > 365"),
                CreateRule("flag-large", "sizeInBytes > 10000000"),
            ],
        };

        // Act
        var ruleSet = MailRuleSetMapper.Map(settings, this.compiler);

        // Assert
        Assert.Equal(
            ["file-invoices", "archive-old", "flag-large"],
            ruleSet.Rules.Select(rule => rule.Name));
        Assert.True(ruleSet.Rules[0].StopWhenMatched);
        Assert.False(ruleSet.Rules[1].StopWhenMatched);
    }

    [Fact]
    public void Map_DisabledRule_IsLeftOutOfTheSetEntirely()
    {
        // Arrange
        var settings = new MailRulesOptions
        {
            Rules =
            [
                CreateRule("switched-off", "isSeen", enabled: false),
                CreateRule("running", "isDraft"),
            ],
        };

        // Act
        var ruleSet = MailRuleSetMapper.Map(settings, this.compiler);

        // Assert
        Assert.Equal(["running"], ruleSet.Rules.Select(rule => rule.Name));
    }

    /// <summary>Switching a rule off is a change to the rule set, so it moves the identity exactly as removing it would.</summary>
    [Fact]
    public void Map_DisablingARule_MovesTheRevisionToWhatRemovingItWouldProduce()
    {
        // Arrange
        var withDisabled = new MailRulesOptions
        {
            Rules = [CreateRule("switched-off", "isSeen", enabled: false), CreateRule("running", "isDraft")],
        };
        var withoutIt = new MailRulesOptions { Rules = [CreateRule("running", "isDraft")] };

        // Act
        var disabled = MailRuleSetMapper.Map(withDisabled, this.compiler);
        var removed = MailRuleSetMapper.Map(withoutIt, this.compiler);

        // Assert
        Assert.Equal(removed.Revision, disabled.Revision);
    }

    [Fact]
    public void Map_DeclaredLimits_TravelWithTheRuleSet()
    {
        // Arrange
        var settings = new MailRulesOptions
        {
            MaxConditionLength = 200,
            MaxConditionNestingDepth = 8,
            ConditionEvaluationTimeout = TimeSpan.FromMilliseconds(250),
            Rules = [CreateRule("running", "isDraft")],
        };

        // Act
        var ruleSet = MailRuleSetMapper.Map(settings, this.compiler);

        // Assert
        Assert.Equal(200, ruleSet.Bounds.MaxLength);
        Assert.Equal(8, ruleSet.Bounds.MaxNestingDepth);
        Assert.Equal(TimeSpan.FromMilliseconds(250), ruleSet.Bounds.EvaluationTimeout);
    }

    /// <summary>The pass bounds are a separate contract from the condition bounds, and neither may pick up the other's value.</summary>
    [Fact]
    public void ToEvaluationOptions_DeclaredPassBounds_ReachTheEvaluationContract()
    {
        // Arrange
        var settings = new MailRulesOptions
        {
            EvaluationBatchSize = 25,
            MaxEvaluationBatchesPerPass = 3,
        };

        // Act
        var evaluation = settings.ToEvaluationOptions();

        // Assert
        Assert.Equal(25, evaluation.BatchSize);
        Assert.Equal(3, evaluation.MaxBatchesPerPass);
    }

    [Fact]
    public void Map_NoRules_IsAnEmptySetUnderAnIdentityOfItsOwn()
    {
        // Act
        var ruleSet = MailRuleSetMapper.Map(new MailRulesOptions(), this.compiler);

        // Assert
        Assert.True(ruleSet.IsEmpty);
        Assert.True(ruleSet.Revision.IsSpecified);
    }

    /// <summary>Mapping a set nothing validated is a defect in the composition, and it says so rather than half-running.</summary>
    [Fact]
    public void Map_RuleSetThatWasNeverValidated_IsRefused()
    {
        // Arrange
        var settings = new MailRulesOptions { Rules = [CreateRule("broken", "senderMailbox == 'a'")] };

        // Act, Assert
        var failure = Assert.Throws<InvalidOperationException>(() => MailRuleSetMapper.Map(settings, this.compiler));
        Assert.Contains("senderMailbox", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Current_PublishedConfiguration_IsReadOnceHoweverOftenAPassAsksForIt()
    {
        // Arrange
        var countingCompiler = new CountingMailRuleConditionCompiler();
        var published = new StubSettingsSnapshot<MailRulesOptions>(
            new MailRulesOptions { Rules = [CreateRule("running", "isDraft")] });
        var source = new ConfiguredMailRuleSetSource(published, countingCompiler);

        // Act
        var first = source.Current;
        var second = source.Current;

        // Assert
        Assert.Same(first, second);
        Assert.Equal(1, countingCompiler.CompileCount);
    }

    [Fact]
    public void Current_ConfigurationThatWasReloaded_IsReadAgain()
    {
        // Arrange
        var countingCompiler = new CountingMailRuleConditionCompiler();
        var published = new StubSettingsSnapshot<MailRulesOptions>(
            new MailRulesOptions { Rules = [CreateRule("running", "isDraft")] });
        var source = new ConfiguredMailRuleSetSource(published, countingCompiler);
        var before = source.Current;

        // Act
        published.Current = new MailRulesOptions { Rules = [CreateRule("running", "isSeen")] };

        var after = source.Current;

        // Assert
        Assert.NotSame(before, after);
        Assert.NotEqual(before.Revision, after.Revision);
        Assert.Equal(2, countingCompiler.CompileCount);
    }

    [Fact]
    public void Map_ScopedRule_CarriesTheAccountsItAppliesTo()
    {
        // Arrange
        var settings = new MailRulesOptions
        {
            Rules =
            [
                CreateRule("scoped", "isSeen", accounts: ["primary", "work"]),
                CreateRule("general", "isDraft"),
            ],
        };

        // Act
        var ruleSet = MailRuleSetMapper.Map(settings, this.compiler);

        // Assert
        Assert.True(ruleSet.Rules[0].AppliesTo("primary"));
        Assert.True(ruleSet.Rules[0].AppliesTo("work"));
        Assert.False(ruleSet.Rules[0].AppliesTo("archive"));
        Assert.True(ruleSet.Rules[1].AppliesTo("archive"));
    }

    /// <summary>Narrowing a rule to one account changes which mail it reaches, so it is a different rule set.</summary>
    [Fact]
    public void Map_ScopingARule_MovesTheRevision()
    {
        // Arrange
        var general = new MailRulesOptions { Rules = [CreateRule("running", "isDraft")] };
        var scoped = new MailRulesOptions { Rules = [CreateRule("running", "isDraft", accounts: ["primary"])] };

        // Act
        var beforeScoping = MailRuleSetMapper.Map(general, this.compiler);
        var afterScoping = MailRuleSetMapper.Map(scoped, this.compiler);

        // Assert
        Assert.NotEqual(beforeScoping.Revision, afterScoping.Revision);
    }

    private static MailRuleOptions CreateRule(
        string name,
        string conditionText,
        bool stopWhenMatched = false,
        bool enabled = true,
        string[]? accounts = null) =>
        new()
        {
            Name = name,
            Condition = conditionText,
            StopWhenMatched = stopWhenMatched,
            Enabled = enabled,
            Accounts = accounts ?? [],
        };
}
