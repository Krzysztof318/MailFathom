// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Rules;
using MailFathom.Application.Rules.Actions;
using MailFathom.Application.Rules.Conditions;
using MailFathom.Application.Rules.Facts;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Application.UnitTests.Rules;

/// <summary>Covers the order a pass runs in, where it stops, and how a rule that produced no answer is recorded.</summary>
public sealed class MailRuleSetEvaluatorTests
{
    private static readonly DateTimeOffset EvaluatedAt = new(2026, 3, 31, 9, 30, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider timeProvider = new(EvaluatedAt);

    [Fact]
    public async Task EvaluateAsync_RuleSet_ReachesEveryRuleInDeclaredOrder()
    {
        // Arrange
        var conditions = Enumerable
            .Range(0, 3)
            .Select(position => ScriptedMailRuleCondition.Answering(position == 1))
            .ToArray();
        var ruleSet = CreateRuleSet(conditions.Select((condition, position) =>
            MailRule.Create($"rule-{position}", condition, stopWhenMatched: false)));

        // Act
        var evaluation = await this.CreateEvaluator().EvaluateAsync(
            ruleSet,
            CreateFacts(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            ["rule-0", "rule-1", "rule-2"],
            evaluation.Evaluations.Select(result => result.RuleName));
        Assert.Equal(
            [MailRuleOutcome.NotMatched, MailRuleOutcome.Matched, MailRuleOutcome.NotMatched],
            evaluation.Evaluations.Select(result => result.Outcome));
        Assert.Equal(["rule-1"], evaluation.MatchedRuleNames);
        Assert.False(evaluation.StoppedEarly);
    }

    [Fact]
    public async Task EvaluateAsync_MatchingRuleThatStopsThePass_LeavesTheRulesBelowItUnreached()
    {
        // Arrange
        var stopping = ScriptedMailRuleCondition.Answering(matches: true);
        var below = ScriptedMailRuleCondition.Answering(matches: true);
        var ruleSet = CreateRuleSet(
        [
            MailRule.Create("stopping", stopping, stopWhenMatched: true),
            MailRule.Create("below", below, stopWhenMatched: false),
        ]);

        // Act
        var evaluation = await this.CreateEvaluator().EvaluateAsync(
            ruleSet,
            CreateFacts(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(evaluation.StoppedEarly);
        Assert.Equal(["stopping"], evaluation.Evaluations.Select(result => result.RuleName));
        Assert.Equal(0, below.EvaluationCount);
    }

    [Fact]
    public async Task EvaluateAsync_RuleThatStopsThePassButDoesNotMatch_LeavesThePassRunning()
    {
        // Arrange
        var ruleSet = CreateRuleSet(
        [
            MailRule.Create("stopping", ScriptedMailRuleCondition.Answering(matches: false), stopWhenMatched: true),
            MailRule.Create("below", ScriptedMailRuleCondition.Answering(matches: true), stopWhenMatched: false),
        ]);

        // Act
        var evaluation = await this.CreateEvaluator().EvaluateAsync(
            ruleSet,
            CreateFacts(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(evaluation.StoppedEarly);
        Assert.Equal(["below"], evaluation.MatchedRuleNames);
    }

    /// <summary>A condition that raises is a rule that failed, and it is never read as either answer.</summary>
    [Fact]
    public async Task EvaluateAsync_ConditionThatRaises_IsRecordedAsAFailedRuleAndThePassCarriesOn()
    {
        // Arrange
        var ruleSet = CreateRuleSet(
        [
            MailRule.Create(
                "raising",
                ScriptedMailRuleCondition.Raising(new InvalidOperationException("unusable operand")),
                stopWhenMatched: true),
            MailRule.Create("below", ScriptedMailRuleCondition.Answering(matches: true), stopWhenMatched: false),
        ]);

        // Act
        var evaluation = await this.CreateEvaluator().EvaluateAsync(
            ruleSet,
            CreateFacts(),
            TestContext.Current.CancellationToken);

        // Assert
        var failed = evaluation.Evaluations[0];
        Assert.Equal(MailRuleOutcome.Failed, failed.Outcome);
        Assert.Equal(MailRuleConditionFailure.EvaluationFaulted, failed.Failure);
        Assert.True(evaluation.HasFailures);
        Assert.Equal(["below"], evaluation.MatchedRuleNames);
        Assert.False(evaluation.StoppedEarly);
    }

    [Fact]
    public async Task EvaluateAsync_ConditionOutlastingItsTimeout_IsRecordedAsATimedOutRule()
    {
        // Arrange
        var stalling = ScriptedMailRuleCondition.NeverAnswering();
        var ruleSet = CreateRuleSet([MailRule.Create("stalling", stalling, stopWhenMatched: false)]);
        var evaluator = this.CreateEvaluator();

        // Act
        var pass = evaluator.EvaluateAsync(ruleSet, CreateFacts(), TestContext.Current.CancellationToken);

        await stalling.Started;
        this.timeProvider.Advance(MailRuleConditionBounds.Default.EvaluationTimeout);

        var evaluation = await pass;

        // Assert
        var timedOut = Assert.Single(evaluation.Evaluations);
        Assert.Equal(MailRuleOutcome.Failed, timedOut.Outcome);
        Assert.Equal(MailRuleConditionFailure.EvaluationTimedOut, timedOut.Failure);
    }

    /// <summary>Withdrawing the pass is the caller's act, so it stops the pass rather than blaming a rule for it.</summary>
    [Fact]
    public async Task EvaluateAsync_CancelledPass_StopsRatherThanRecordingAFailedRule()
    {
        // Arrange
        var ruleSet = CreateRuleSet(
            [MailRule.Create("stalling", ScriptedMailRuleCondition.NeverAnswering(), stopWhenMatched: false)]);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act, Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => this.CreateEvaluator().EvaluateAsync(ruleSet, CreateFacts(), cancellation.Token));
    }

    /// <summary>A rule scoped elsewhere is not this account's rule, so it is passed over rather than recorded as unmatched.</summary>
    [Fact]
    public async Task EvaluateAsync_RuleScopedToAnotherAccount_IsNotReachedAtAll()
    {
        // Arrange
        var elsewhere = ScriptedMailRuleCondition.Answering(matches: true);
        var ruleSet = CreateRuleSet(
        [
            MailRule.Create("other-account", elsewhere, stopWhenMatched: false, accounts: ["primary"]),
            MailRule.Create("this-account", ScriptedMailRuleCondition.Answering(matches: true), stopWhenMatched: false, accounts: ["work"]),
            MailRule.Create("every-account", ScriptedMailRuleCondition.Answering(matches: true), stopWhenMatched: false),
        ]);

        // Act
        var evaluation = await this.CreateEvaluator().EvaluateAsync(
            ruleSet,
            CreateFacts(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            ["this-account", "every-account"],
            evaluation.Evaluations.Select(result => result.RuleName));
        Assert.Equal(0, elsewhere.EvaluationCount);
    }

    /// <summary>Scoping a stopping rule narrows what it stops, so an account it does not name runs the rules below it.</summary>
    [Fact]
    public async Task EvaluateAsync_StoppingRuleScopedToAnotherAccount_DoesNotEndThePass()
    {
        // Arrange
        var ruleSet = CreateRuleSet(
        [
            MailRule.Create("stopping-elsewhere", ScriptedMailRuleCondition.Answering(matches: true), stopWhenMatched: true, accounts: ["primary"]),
            MailRule.Create("below", ScriptedMailRuleCondition.Answering(matches: true), stopWhenMatched: false),
        ]);

        // Act
        var evaluation = await this.CreateEvaluator().EvaluateAsync(
            ruleSet,
            CreateFacts(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(evaluation.StoppedEarly);
        Assert.Equal(["below"], evaluation.MatchedRuleNames);
    }

    /// <summary>Two accounts differing only in case are two accounts, which is how the synchronization section reads them.</summary>
    [Fact]
    public async Task EvaluateAsync_ScopeSpelledInADifferentCase_DoesNotReachThisAccount()
    {
        // Arrange
        var ruleSet = CreateRuleSet(
            [MailRule.Create("mistyped", ScriptedMailRuleCondition.Answering(matches: true), stopWhenMatched: false, accounts: ["Work"])]);

        // Act
        var evaluation = await this.CreateEvaluator().EvaluateAsync(
            ruleSet,
            CreateFacts(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(evaluation.Evaluations);
    }

    /// <summary>A rule that could not answer did not match, so what it declared is not something the mailbox is asked for.</summary>
    [Fact]
    public async Task EvaluateAsync_RuleThatFailedBesideOneThatMatched_PlansOnlyTheMatchingRulesActions()
    {
        // Arrange
        var filing = MailRuleActionSet.Create([MailRuleAction.Relocate(MailFolderAlias.Create("archive"))]);
        var ruleSet = CreateRuleSet(
        [
            MailRule.Create(
                "raising",
                ScriptedMailRuleCondition.Raising(new InvalidOperationException("unusable operand")),
                MailRuleActionSet.Create([MailRuleAction.Delete()])),
            MailRule.Create("below", ScriptedMailRuleCondition.Answering(matches: true), filing),
        ]);

        // Act
        var evaluation = await this.CreateEvaluator().EvaluateAsync(
            ruleSet,
            CreateFacts(),
            TestContext.Current.CancellationToken);

        // Assert
        var planned = Assert.Single(evaluation.ActionPlan.Actions);
        Assert.Equal("below", planned.RuleName);
        Assert.Equal(MailboxMutation.Relocate, planned.Action.Mutation);
        Assert.Empty(evaluation.ActionPlan.WithheldRuleNames);
    }

    [Fact]
    public async Task EvaluateAsync_EmptyRuleSet_ReportsThePassWithoutReachingAnything()
    {
        // Arrange
        var ruleSet = CreateRuleSet([]);

        // Act
        var evaluation = await this.CreateEvaluator().EvaluateAsync(
            ruleSet,
            CreateFacts(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(evaluation.Evaluations);
        Assert.Empty(evaluation.MatchedRuleNames);
        Assert.False(evaluation.HasFailures);
        Assert.Equal(ruleSet.Revision, evaluation.Revision);
    }

    private static MailRuleSet CreateRuleSet(IEnumerable<MailRule> rules)
    {
        var materialized = rules.ToArray();

        return MailRuleSet.Create(
            materialized,
            MailRuleSetRevision.Create(
                [.. materialized.Select(rule => new MailRuleDeclaration(rule.Name, "isSeen", [.. rule.Actions.Actions], rule.StopWhenMatched, [.. rule.Accounts]))]),
            MailRuleConditionBounds.Default);
    }

    private static MailRuleFacts CreateFacts() =>
        new(
            new MailRuleEmailFacts { Account = "work", Folder = "inbox" },
            new RecordingMailRuleBodyTextReader(),
            EvaluatedAt);

    private MailRuleSetEvaluator CreateEvaluator() => new(this.timeProvider);
}
