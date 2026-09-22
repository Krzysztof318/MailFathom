// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Agent.Search;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Search;
using MailFathom.Domain.Access;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Agent.Search;

/// <summary>Covers how a person's Agent history is ranked: which conversations come first, and which mode says so.</summary>
public sealed class AgentConversationSearchTests
{
    private static readonly EmailSearchQueryText Query = EmailSearchQueryText.Create("indexation cap");

    private readonly IAgentConversationSearchIndex index = Substitute.For<IAgentConversationSearchIndex>();

    /// <summary>Agreement between words and meaning is what hybrid retrieval is wanted for.</summary>
    [Fact]
    public async Task SearchAsync_BothRankingsFoundAConversation_RanksItAboveOnesOnlyOneRankingFound()
    {
        // Arrange
        var byWords = Hit(minutesAgo: 1);
        var byBoth = Hit(minutesAgo: 30);
        var byMeaning = Hit(minutesAgo: 2);
        this.Lexical(byWords, byBoth);
        this.Semantic(byMeaning, byBoth);

        // Act
        var result = await this.SearchAsync(EmbeddingSpaceExample.Serving(EmbeddingSpaceExample.Generator()));

        // Assert
        Assert.Equal(EmailSearchRetrievalMode.Hybrid, result.RetrievalMode);
        Assert.Equal(SemanticSearchCapability.Available, result.SemanticSearch);
        Assert.Equal(byBoth.Conversation, result.Hits[0].Conversation);
        Assert.Equal(3, result.Hits.Count);
    }

    /// <summary>Two conversations at symmetric places score identically, and the more recent one is the answer's first.</summary>
    [Fact]
    public async Task SearchAsync_TwoConversationsScoreEqually_PutsTheMoreRecentFirst()
    {
        // Arrange
        var older = Hit(minutesAgo: 60);
        var newer = Hit(minutesAgo: 5);
        this.Lexical(older, newer);
        this.Semantic(newer, older);

        // Act
        var result = await this.SearchAsync(EmbeddingSpaceExample.Serving(EmbeddingSpaceExample.Generator()));

        // Assert
        Assert.Equal([newer.Conversation, older.Conversation], result.Hits.Select(hit => hit.Conversation));
    }

    /// <summary>A provider's outage costs ranking quality, never the feature.</summary>
    [Fact]
    public async Task SearchAsync_EmbeddingProviderFails_ReturnsTheLexicalOrderingAndSaysSo()
    {
        // Arrange
        var first = Hit(minutesAgo: 90);
        var second = Hit(minutesAgo: 1);
        this.Lexical(first, second);
        var generator = EmbeddingSpaceExample.Generator();
        generator.Failure = EmbeddingGenerationFailure.RateLimited;

        // Act
        var result = await this.SearchAsync(EmbeddingSpaceExample.Serving(generator));

        // Assert
        Assert.Equal(EmailSearchRetrievalMode.Lexical, result.RetrievalMode);
        Assert.Equal(SemanticSearchCapability.Degraded, result.SemanticSearch);
        Assert.Equal([first, second], result.Hits);
        await this.index.DidNotReceive().ReadNearestRankingAsync(
            Arg.Any<UserId>(),
            Arg.Any<RegisteredEmbeddingProfile>(),
            Arg.Any<EmbeddingVector>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A deployment that never activated a profile searches by words and reports the ordinary lexical deployment.</summary>
    [Fact]
    public async Task SearchAsync_NoActiveProfile_IsLexicalAndInactive()
    {
        // Arrange
        this.Lexical(Hit(minutesAgo: 1));

        // Act
        var result = await this.SearchAsync(EmbeddingSpaceExample.Inactive());

        // Assert
        Assert.Equal(EmailSearchRetrievalMode.Lexical, result.RetrievalMode);
        Assert.Equal(SemanticSearchCapability.Inactive, result.SemanticSearch);
        Assert.Single(result.Hits);
    }

    /// <summary>A conversation written before its messages were embedded is still found, by its words.</summary>
    [Fact]
    public async Task SearchAsync_ConversationCarryingNoEmbedding_StillAppearsInAHybridAnswer()
    {
        // Arrange
        var unembedded = Hit(minutesAgo: 400);
        this.Lexical(unembedded);
        this.Semantic(Hit(minutesAgo: 1));

        // Act
        var result = await this.SearchAsync(EmbeddingSpaceExample.Serving(EmbeddingSpaceExample.Generator()));

        // Assert
        Assert.Contains(result.Hits, hit => hit.Conversation == unembedded.Conversation);
    }

    /// <summary>A result opens where the conversation placed higher, and at the words typed where it placed equally.</summary>
    [Fact]
    public async Task SearchAsync_ConversationFoundByBoth_PointsAtTheMessageOfTheRankingThatPlacedItHigher()
    {
        // Arrange
        var conversation = AgentConversationId.New();
        var byWords = Hit(minutesAgo: 3) with { Conversation = conversation, Sequence = 4 };
        var byMeaning = byWords with { Message = AgentMessageId.New(), Sequence = 9 };
        this.Lexical(Hit(minutesAgo: 1), byWords);
        this.Semantic(byMeaning);

        // Act
        var result = await this.SearchAsync(EmbeddingSpaceExample.Serving(EmbeddingSpaceExample.Generator()));

        // Assert
        Assert.Equal(byMeaning, Assert.Single(result.Hits, hit => hit.Conversation == conversation));
    }

    /// <summary>The answer is bounded by what the caller asked for, whatever the rankings returned.</summary>
    [Fact]
    public async Task SearchAsync_RankingsReturnMoreThanTheLimit_ReturnsNoMoreThanTheLimit()
    {
        // Arrange
        this.Lexical(Hit(minutesAgo: 1), Hit(minutesAgo: 2), Hit(minutesAgo: 3));
        this.Semantic(Hit(minutesAgo: 4), Hit(minutesAgo: 5));
        var search = new AgentConversationSearch(EmbeddingSpaceExample.Serving(EmbeddingSpaceExample.Generator()), this.index);

        // Act
        var result = await search.SearchAsync(SyntheticUser.Deployment, Query, limit: 2, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, result.Hits.Count);
    }

    /// <summary>Only the signed-in person's history is read, by both rankings.</summary>
    [Fact]
    public async Task SearchAsync_Always_ReadsTheCallersConversationsAlone()
    {
        // Arrange
        this.Lexical();
        this.Semantic();

        // Act
        await this.SearchAsync(EmbeddingSpaceExample.Serving(EmbeddingSpaceExample.Generator()));

        // Assert
        await this.index.Received(1).ReadLexicalRankingAsync(
            SyntheticUser.Deployment,
            Query,
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
        await this.index.Received(1).ReadNearestRankingAsync(
            SyntheticUser.Deployment,
            EmbeddingSpaceExample.Profile,
            Arg.Any<EmbeddingVector>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    private static AgentConversationSearchHit Hit(int minutesAgo) => new(
        AgentConversationId.New(),
        "A conversation",
        EmbeddingSpaceExample.Now.AddMinutes(-minutesAgo),
        AgentMessageId.New(),
        Sequence: 1);

    private void Lexical(params AgentConversationSearchHit[] hits) =>
        this.index.ReadLexicalRankingAsync(Arg.Any<UserId>(), Arg.Any<EmailSearchQueryText>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(hits);

    private void Semantic(params AgentConversationSearchHit[] hits) =>
        this.index.ReadNearestRankingAsync(
                Arg.Any<UserId>(),
                Arg.Any<RegisteredEmbeddingProfile>(),
                Arg.Any<EmbeddingVector>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(hits);

    private Task<AgentConversationSearchResult> SearchAsync(ActiveEmbeddingSpace space) =>
        new AgentConversationSearch(space, this.index).SearchAsync(
            SyntheticUser.Deployment,
            Query,
            AgentConversationSearch.MaximumResults,
            TestContext.Current.CancellationToken);
}
