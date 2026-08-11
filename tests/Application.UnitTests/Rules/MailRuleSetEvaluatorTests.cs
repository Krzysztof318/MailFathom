// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Rules;
using MailFathom.Application.Rules.Conditions;
using MailFathom.Application.Rules.Facts;
using MailFathom.Application.UnitTests.TestDoubles;
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
                [.. materialized.Select(rule => new MailRuleDeclaration(rule.Name, "isSeen", rule.StopWhenMatched))]),
            MailRuleConditionBounds.Default);
    }

    private static MailRuleFacts CreateFacts() =>
        new(
            new MailRuleEmailFacts { Account = "work", Folder = "inbox" },
            new RecordingMailRuleBodyTextReader(),
            EvaluatedAt);

    private MailRuleSetEvaluator CreateEvaluator() => new(this.timeProvider);
}
