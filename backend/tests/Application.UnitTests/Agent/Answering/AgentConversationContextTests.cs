// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Agent.Answering;
using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.UnitTests.Agent.Conversations;
using Xunit;

namespace MailFathom.Application.UnitTests.Agent.Answering;

/// <summary>Covers what one turn is composed from: the newest summary, what it carries, and every turn after it verbatim.</summary>
/// <remarks>
/// Every turn here is a hundred characters, so at the default rate of four characters to a token each costs twenty-five
/// tokens and a budget reads as a number of turns.
/// </remarks>
public sealed class AgentConversationContextTests
{
    private readonly AgentMessageId answer = AgentMessageId.New();

    [Fact]
    public void Compose_AConversationNeverCompacted_SendsEveryTurnInTheOrderItWasWritten()
    {
        // Arrange
        var context = AgentConversationContext.Read(AgentConversationExample.Written(Question("a"), Note("b"), Question("c")));

        // Act
        var history = context.Compose();

        // Assert
        Assert.Equal([Text("a"), Text("b"), Text("c")], history.Select(static turn => turn.Text));
    }

    [Fact]
    public void Compose_AfterACompaction_LeadsWithTheSummaryAndSendsOnlyWhatFollowsItsPart()
    {
        // Arrange
        var context = AgentConversationContext.Read(AgentConversationExample.Written(
            Question("a"),
            Note("b"),
            new AgentConversationCompacted(this.answer, Through: 2, "They asked a and heard b.", Carried: []),
            Question("c")));

        // Act
        var history = context.Compose();

        // Assert
        Assert.Collection(
            history,
            summary =>
            {
                Assert.Equal(AgentMessageAuthor.Agent, summary.Author);
                Assert.Equal(AgentConversationContext.SummaryPreamble + "They asked a and heard b.", summary.Text);
            },
            later => Assert.Equal(Text("c"), later.Text));
    }

    /// <summary>Two turns that no compaction separates send the same leading part, which is what a provider's prompt cache is keyed on.</summary>
    [Fact]
    public void Compose_ATurnAddedWithoutACompaction_LeavesEverythingBeforeItUnchanged()
    {
        // Arrange
        AgentConversationEntry[] before =
        [
            Question("a"),
            new AgentConversationCompacted(this.answer, Through: 1, "They asked a.", Carried: []),
            Note("b"),
        ];

        // Act
        var first = AgentConversationContext.Read(AgentConversationExample.Written(before)).Compose();
        var second = AgentConversationContext.Read(AgentConversationExample.Written([.. before, Question("c")])).Compose();

        // Assert
        Assert.Equal(first.Select(static turn => turn.Text), second.Take(first.Count).Select(static turn => turn.Text));
    }

    [Fact]
    public void PlanCompaction_AConversationOverTheBudget_KeepsTheNewestHalfVerbatimAndSummarisesTheRest()
    {
        // Arrange
        var context = AgentConversationContext.Read(AgentConversationExample.Written(
            Question("a"), Note("b"), Question("c"), Note("d"), Question("e")));

        // Act
        var plan = context.PlanCompaction(budgetTokens: 100);

        // Assert
        Assert.NotNull(plan);
        Assert.Null(plan.PreviousSummary);
        Assert.Equal([Text("a"), Text("b"), Text("c")], plan.Turns.Select(static turn => turn.Text));
        Assert.Equal(3, plan.Through);
        Assert.Empty(plan.Carried);
    }

    /// <summary>A proposal still pending is live work, so it rides beside the summary rather than inside it.</summary>
    [Fact]
    public void PlanCompaction_APendingProposalInThePartSummarised_IsCarriedRatherThanFolded()
    {
        // Arrange
        var context = AgentConversationContext.Read(AgentConversationExample.Written(
            Question("a"), this.Proposal(), Question("c"), Note("d"), Question("e")));

        // Act
        var plan = context.PlanCompaction(budgetTokens: 100);

        // Assert
        Assert.NotNull(plan);
        Assert.Equal([2L], plan.Carried);
        Assert.Equal([Text("a"), Text("c")], plan.Turns.Select(static turn => turn.Text));
    }

    /// <summary>A proposal the person already answered is no longer live, so the next summary folds it in.</summary>
    [Fact]
    public void PlanCompaction_ACarriedProposalSinceAnswered_IsFoldedIntoTheNextSummary()
    {
        // Arrange
        var context = AgentConversationContext.Read(AgentConversationExample.Written(
            Question("a"),
            this.Proposal(),
            new AgentConversationCompacted(this.answer, Through: 2, "They asked a.", Carried: [2]),
            new AgentProposalResolved(2, AgentProposalState.Declined),
            Question("c"),
            Note("d"),
            Question("e"),
            Note("f")));

        // Act
        var plan = context.PlanCompaction(budgetTokens: 100);

        // Assert
        Assert.NotNull(plan);
        Assert.Equal("They asked a.", plan.PreviousSummary);
        Assert.Empty(plan.Carried);
        Assert.StartsWith("[Proposed a message", plan.Turns[0].Text, StringComparison.Ordinal);
        Assert.Equal([Text("c"), Text("d")], plan.Turns.Skip(1).Select(static turn => turn.Text));
    }

    [Fact]
    public void PlanCompaction_EveryTurnSinceTheSummaryFits_PlansNothing()
    {
        // Arrange
        var context = AgentConversationContext.Read(AgentConversationExample.Written(Question("a"), Note("b")));

        // Act
        var plan = context.PlanCompaction(budgetTokens: 100);

        // Assert
        Assert.Null(plan);
    }

    /// <summary>A summariser that failed still leaves a turn that fits: the newest turns, and the oldest dropped.</summary>
    [Fact]
    public void ComposeWithin_ATurnWhoseCompactionFailed_SendsTheNewestTurnsThatFit()
    {
        // Arrange
        var context = AgentConversationContext.Read(AgentConversationExample.Written(
            Question("a"), Note("b"), Question("c"), Note("d")));

        // Act
        var history = context.ComposeWithin(budgetTokens: 60, Text("q"));

        // Assert
        Assert.Equal([Text("d")], history.Select(static turn => turn.Text));
    }

    /// <summary>The estimate is corrected by what the provider charged, so a language that costs more per character is counted as it is.</summary>
    [Fact]
    public void EstimateTokens_AfterATurnWasCharged_ConvertsAtTheRateItWasCharged()
    {
        // Arrange
        var context = AgentConversationContext.Read(AgentConversationExample.Written(
            Question("a"),
            new AgentModelCharged(this.answer, SentCharacters: 1_000, InputTokens: 500)));

        // Act
        var tokens = context.EstimateTokens(context.Compose(), Text("q"));

        // Assert
        Assert.Equal(100, tokens);
    }

    [Fact]
    public void EstimateTokens_BeforeAnyTurnWasCharged_ConvertsAtFourCharactersToAToken()
    {
        // Arrange
        var context = AgentConversationContext.Read(AgentConversationExample.Written(Question("a")));

        // Act
        var tokens = context.EstimateTokens(context.Compose(), Text("q"));

        // Assert
        Assert.Equal(50, tokens);
    }

    /// <summary>The scope a conversation stands under is the last one a question stated, whatever was summarised since.</summary>
    [Fact]
    public void ScopeInForce_AQuestionAfterTheOneThatStatedAScope_KeepsThatScope()
    {
        // Arrange
        var context = AgentConversationContext.Read(AgentConversationExample.Written(
            Question("a"),
            new AgentMessageWritten(AgentMessageId.New(), AgentMessageAuthor.Person, PresentationText.Create(Text("b")), Scope: null)));

        // Act
        var scope = context.ScopeInForce;

        // Assert
        Assert.Equal(AgentScopeKind.Mailbox, scope?.Kind);
    }

    private static string Text(string letter) => new(letter[0], 100);

    private static AgentMessageWritten Question(string letter) =>
        AgentConversationExample.Question(AgentMessageId.New(), Text(letter));

    private static AgentMessageWritten Note(string letter) =>
        AgentConversationExample.Note(AgentMessageId.New(), Text(letter));

    private AgentActionProposed Proposal() =>
        new(this.answer, AgentConversationExample.Actionable(), AgentConversationExample.Act());
}
