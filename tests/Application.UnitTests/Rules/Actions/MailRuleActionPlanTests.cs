// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Rules;
using MailFathom.Application.Rules.Actions;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using Xunit;

namespace MailFathom.Application.UnitTests.Rules.Actions;

/// <summary>Covers how the actions of several rules matching one email are resolved into the changes it actually gets.</summary>
public sealed class MailRuleActionPlanTests
{
    private static readonly MailFolderAlias Archive = MailFolderAlias.Create("archive");
    private static readonly MailFolderAlias Backup = MailFolderAlias.Create("backup");

    /// <summary>Declared order decides, so two rules filing one email into different folders answer the same way every run.</summary>
    [Fact]
    public void Compose_TwoRulesFilingIntoDifferentFolders_HonorsTheOneDeclaredFirst()
    {
        // Arrange
        var first = RuleNamed("file-invoices", MailRuleAction.Relocate(Archive));
        var second = RuleNamed("file-everything", MailRuleAction.Relocate(Backup));

        // Act
        var plan = MailRuleActionPlan.Compose([first, second]);

        // Assert
        var honored = Assert.Single(plan.Actions);
        Assert.Equal("file-invoices", honored.RuleName);
        Assert.Equal(Archive, honored.Action.DestinationAlias);
        Assert.Equal(["file-everything"], plan.WithheldRuleNames);
    }

    /// <summary>The same two rules the other way round settle the other way, which is what "by declared order" means.</summary>
    [Fact]
    public void Compose_TheSameTwoRulesReordered_HonorsTheOtherOne()
    {
        // Arrange
        var filing = RuleNamed("file-invoices", MailRuleAction.Relocate(Archive));
        var everything = RuleNamed("file-everything", MailRuleAction.Relocate(Backup));

        // Act
        var plan = MailRuleActionPlan.Compose([everything, filing]);

        // Assert
        var honored = Assert.Single(plan.Actions);
        Assert.Equal("file-everything", honored.RuleName);
        Assert.Equal(["file-invoices"], plan.WithheldRuleNames);
    }

    /// <summary>A rule naming a deletion leaves no room for anything a later rule asks for on the same message.</summary>
    [Fact]
    public void Compose_ADeletionFollowedByAFiling_WithholdsTheFiling()
    {
        // Arrange
        var deleting = RuleNamed("drop-notifications", MailRuleAction.Delete());
        var filing = RuleNamed("file-invoices", MailRuleAction.Relocate(Archive));

        // Act
        var plan = MailRuleActionPlan.Compose([deleting, filing]);

        // Assert
        Assert.Equal([MailboxMutation.Delete], plan.Actions.Select(planned => planned.Action.Mutation));
        Assert.Equal(["file-invoices"], plan.WithheldRuleNames);
    }

    /// <summary>A flag declared by a later rule is still written before an earlier rule moves the occurrence.</summary>
    [Fact]
    public void Compose_AFilingRuleAndAFlaggingRuleBelowIt_AppliesTheFlagFirst()
    {
        // Arrange
        var filing = RuleNamed("file-invoices", MailRuleAction.Relocate(Archive));
        var flagging = RuleNamed("mark-them-read", MailRuleAction.SetSeen(isSeen: true));

        // Act
        var plan = MailRuleActionPlan.Compose([filing, flagging]);

        // Assert
        Assert.Equal(
            [MailboxMutation.SetSeen, MailboxMutation.Relocate],
            plan.Actions.Select(planned => planned.Action.Mutation));
        Assert.Empty(plan.WithheldRuleNames);
    }

    /// <summary>Each action carries the rule that asked, because that is half of the request's idempotency identity.</summary>
    [Fact]
    public void Compose_ActionsFromTwoRules_EachNamesTheRuleThatAskedForIt()
    {
        // Arrange
        var filing = RuleNamed("file-invoices", MailRuleAction.Relocate(Archive));
        var flagging = RuleNamed("mark-them-read", MailRuleAction.SetSeen(isSeen: true));

        // Act
        var plan = MailRuleActionPlan.Compose([filing, flagging]);

        // Assert
        Assert.Equal(["mark-them-read", "file-invoices"], plan.Actions.Select(planned => planned.RuleName));
    }

    [Fact]
    public void Compose_RulesThatChangeNothing_IsAnEmptyPlan()
    {
        // Act
        var plan = MailRuleActionPlan.Compose([RuleNamed("select-only")]);

        // Assert
        Assert.True(plan.IsEmpty);
        Assert.Empty(plan.WithheldRuleNames);
    }

    [Fact]
    public void Compose_NoMatchingRules_IsTheEmptyPlan() =>
        Assert.Same(MailRuleActionPlan.Nothing, MailRuleActionPlan.Compose([]));

    private static MailRule RuleNamed(string name, params MailRuleAction[] actions) => MailRule.Create(
        name,
        ScriptedMailRuleCondition.Answering(matches: true),
        MailRuleActionSet.Create(actions));
}
