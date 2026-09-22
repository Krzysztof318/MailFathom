// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Agent.Answering;
using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Agent.Search;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Discovery.Presentation.Blocks;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.UnitTests.Agent.Conversations;
using MailFathom.Application.UnitTests.Discovery.Presentation;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Agent.Search;

/// <summary>Covers what of a turn is embedded for the history search once its answer has ended, and what is left to the words.</summary>
public sealed class AgentConversationEmbeddingTests
{
    private static readonly AgentConversationId Conversation = AgentConversationExample.Conversation;

    private readonly IAgentConversationStore store = Substitute.For<IAgentConversationStore>();

    private readonly IAgentConversationSearchIndex index = Substitute.For<IAgentConversationSearchIndex>();

    private readonly AgentMessageId answer = AgentMessageId.New();

    /// <summary>A person's words and the agent's prose are placed; a block rendering an object that lives elsewhere is not.</summary>
    [Fact]
    public async Task EmbedTurnAsync_TurnEnded_EmbedsTheQuestionTheInstructionAndTheProseAlone()
    {
        // Arrange
        this.Holding(
            AgentConversationExample.Question(AgentMessageId.New(), "Where did we land on the indexation cap?"),
            new AgentAnswerStarted(this.answer),
            new AgentBlockComposed(this.answer, Rendering()),
            AgentConversationExample.Question(AgentMessageId.New(), "Only the last quarter."),
            new AgentBlockComposed(this.answer, Prose("The cap stays at five percent.")),
            new AgentAnswerEnded(this.answer, AgentAnswerOutcome.Completed));
        var generator = EmbeddingSpaceExample.Generator();

        // Act
        await this.Embedding(EmbeddingSpaceExample.Serving(generator)).EmbedTurnAsync(this.Question(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            ["Where did we land on the indexation cap?", "Only the last quarter.", "The cap stays at five percent."],
            Assert.Single(generator.RequestedBatches));
        Assert.Equal([1L, 4L, 5L], this.SavedPlaces());
    }

    /// <summary>What follows the answer's ending belongs to the next question and is embedded by that one's run.</summary>
    [Fact]
    public async Task EmbedTurnAsync_ConversationWentOnAfterTheAnswer_StopsAtTheAnswersEnding()
    {
        // Arrange
        this.Holding(
            AgentConversationExample.Question(AgentMessageId.New(), "First question."),
            new AgentAnswerStarted(this.answer),
            new AgentAnswerEnded(this.answer, AgentAnswerOutcome.Stopped),
            AgentConversationExample.Question(AgentMessageId.New(), "Next question."));
        var generator = EmbeddingSpaceExample.Generator();

        // Act
        await this.Embedding(EmbeddingSpaceExample.Serving(generator)).EmbedTurnAsync(this.Question(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["First question."], Assert.Single(generator.RequestedBatches));
        Assert.Equal([1L], this.SavedPlaces());
    }

    /// <summary>A provider that cannot be reached leaves the turn to the lexical index rather than failing the run.</summary>
    [Fact]
    public async Task EmbedTurnAsync_ProviderFails_RecordsNothing()
    {
        // Arrange
        this.Holding(
            AgentConversationExample.Question(AgentMessageId.New(), "A question."),
            new AgentAnswerStarted(this.answer),
            new AgentAnswerEnded(this.answer, AgentAnswerOutcome.Completed));
        var generator = EmbeddingSpaceExample.Generator();
        generator.Failure = EmbeddingGenerationFailure.RateLimited;

        // Act
        await this.Embedding(EmbeddingSpaceExample.Serving(generator)).EmbedTurnAsync(this.Question(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(this.SavedPlaces());
    }

    /// <summary>A deployment that activated no profile places nothing and asks no provider.</summary>
    [Fact]
    public async Task EmbedTurnAsync_NoActiveProfile_RecordsNothing()
    {
        // Arrange
        this.Holding(
            AgentConversationExample.Question(AgentMessageId.New(), "A question."),
            new AgentAnswerStarted(this.answer),
            new AgentAnswerEnded(this.answer, AgentAnswerOutcome.Completed));

        // Act
        await this.Embedding(EmbeddingSpaceExample.Inactive()).EmbedTurnAsync(this.Question(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(this.SavedPlaces());
    }

    private static PresentationBlock Rendering() =>
        PresentationPlanExample.EveryBlock().First(block => !block.Type.Actionable && block is not AnswerBlock);

    private static AnswerBlock Prose(string text)
    {
        var example = PresentationPlanExample.EveryBlock().OfType<AnswerBlock>().First();

        return new AnswerBlock(example.Evidence, PresentationText.Create(text), example.Confidence);
    }

    private AgentQuestion Question() => new(
        Conversation,
        SyntheticUser.Deployment,
        PresentationText.Create("A question."),
        AgentMessageScope.Mailbox(),
        this.answer,
        OpenedAt: 2,
        EmbeddingSpaceExample.Now);

    private AgentConversationEmbedding Embedding(ActiveEmbeddingSpace space) => new(this.store, space, this.index);

    private void Holding(params AgentConversationEntry[] entries) =>
        this.store
            .ReadAsync(Conversation, SyntheticUser.Deployment, AgentConversationHistory.Visible, 0, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new AgentConversationReading(
                Title: null,
                EmbeddingSpaceExample.Now,
                Composing: false,
                AgentConversationExample.Written(entries),
                MoreFollows: false));

    private long[] SavedPlaces() =>
    [
        .. this.index.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IAgentConversationSearchIndex.SaveEmbeddingAsync))
            .Select(call => (long)call.GetArguments()[2]!),
    ];
}
