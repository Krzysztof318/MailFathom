// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Rules;
using MailFathom.Application.Rules.Conditions;
using MailFathom.Application.UnitTests.TestDoubles;
using Xunit;

namespace MailFathom.Application.UnitTests.Rules;

/// <summary>Covers the bounds a condition runs under and the two shapes reading one can produce.</summary>
public sealed class MailRuleConditionContractTests
{
    [Fact]
    public void Default_Bounds_AreAllPositive()
    {
        // Assert
        Assert.True(MailRuleConditionBounds.Default.MaxLength > 0);
        Assert.True(MailRuleConditionBounds.Default.MaxNestingDepth > 0);
        Assert.True(MailRuleConditionBounds.Default.EvaluationTimeout > TimeSpan.Zero);
    }

    [Theory]
    [InlineData(0, 16, 1)]
    [InlineData(-1, 16, 1)]
    [InlineData(1_000, 0, 1)]
    [InlineData(1_000, -1, 1)]
    [InlineData(1_000, 16, 0)]
    [InlineData(1_000, 16, -1)]
    public void Create_BoundThatWouldLeaveAConditionUnboundedOrUnwritable_IsRefused(
        int maxLength,
        int maxNestingDepth,
        int timeoutSeconds)
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MailRuleConditionBounds.Create(maxLength, maxNestingDepth, TimeSpan.FromSeconds(timeoutSeconds)));
    }

    [Fact]
    public void Compiled_Condition_IsReportedAsUsable()
    {
        // Arrange
        var condition = ScriptedMailRuleCondition.Answering(matches: true);

        // Act
        var compilation = MailRuleConditionCompilation.Compiled(condition);

        // Assert
        Assert.True(compilation.IsCompiled);
        Assert.Same(condition, compilation.Condition);
        Assert.Empty(compilation.Errors);
    }

    [Fact]
    public void Refused_Condition_CarriesEveryReasonAndNoCondition()
    {
        // Act
        var compilation = MailRuleConditionCompilation.Refused(["first reason", "second reason"]);

        // Assert
        Assert.False(compilation.IsCompiled);
        Assert.Null(compilation.Condition);
        Assert.Equal(["first reason", "second reason"], compilation.Errors);
    }

    /// <summary>A refusal nobody could act on is a defect here rather than a state to report.</summary>
    [Fact]
    public void Refused_WithoutAReason_IsItselfRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => MailRuleConditionCompilation.Refused([]));
    }

    [Fact]
    public void Matched_Evaluation_NamesNoFailure()
    {
        // Act
        var evaluation = MailRuleEvaluation.Matched("rule");

        // Assert
        Assert.Equal(MailRuleOutcome.Matched, evaluation.Outcome);
        Assert.Null(evaluation.Failure);
    }

    [Fact]
    public void Failed_Evaluation_NamesWhyNoAnswerWasProduced()
    {
        // Act
        var evaluation = MailRuleEvaluation.Failed("rule", MailRuleConditionFailure.EvaluationTimedOut);

        // Assert
        Assert.Equal(MailRuleOutcome.Failed, evaluation.Outcome);
        Assert.Equal(MailRuleConditionFailure.EvaluationTimedOut, evaluation.Failure);
    }

    [Fact]
    public void Create_PassWithoutARevision_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(
            () => MailRuleSetEvaluation.Create(default, [MailRuleEvaluation.Matched("rule")], stoppedEarly: false));
    }
}
