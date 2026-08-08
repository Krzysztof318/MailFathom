// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Retrieval;
using MailFathom.AI.UnitTests.TestDoubles;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Chat;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Retrieval;
using MailFathom.Domain.Accounts;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MailFathom.AI.UnitTests.Retrieval;

/// <summary>Covers the second retrieval pass: what it drops, what it keeps, and what it does when the model cannot tell it.</summary>
/// <remarks>
/// Every test here substitutes the chat port, so nothing reaches a provider and the judgements are the test's own. What
/// is under test is the filtering and the degradation around it, never a model's opinion of relevance.
/// </remarks>
public sealed class ModelJudgedKnowledgeSearchTests
{
    private const string Query = "what did the insurer agree to pay";

    private static readonly MailboxScope Scope = MailboxScope.Create([MailAccountId.Create("primary")], []);

    [Fact]
    public async Task FindPassagesAsync_CandidatesJudgedAboveTheThreshold_HandsThemOverInTheOrderRetrievalRankedThem()
    {
        // Arrange
        var settled = KnowledgePassages.Create("we will pay 4200", Guid.CreateVersion7());
        var discussed = KnowledgePassages.Create("still waiting on the assessor", Guid.CreateVersion7());
        var judge = new ScriptedChatModelClient()
            .Answering(settled.StoredEmailId.ToString(), "95")
            .Answering(discussed.StoredEmailId.ToString(), "70");
        var search = SearchOver(Retrieving(settled, discussed), judge, minimumRelevance: 50);

        // Act
        var passages = await search.FindPassagesAsync(Scope, Query, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([settled, discussed], passages);
    }

    /// <summary>The whole point of the pass: a message that mentions the subject without answering it is not handed over.</summary>
    [Fact]
    public async Task FindPassagesAsync_ACandidateJudgedBelowTheThreshold_DropsIt()
    {
        // Arrange
        var settled = KnowledgePassages.Create("we will pay 4200", Guid.CreateVersion7());
        var mentioned = KnowledgePassages.Create("the claim came up at lunch", Guid.CreateVersion7());
        var judge = new ScriptedChatModelClient()
            .Answering(settled.StoredEmailId.ToString(), "95")
            .Answering(mentioned.StoredEmailId.ToString(), "20");
        var search = SearchOver(Retrieving(settled, mentioned), judge, minimumRelevance: 50);

        // Act
        var passages = await search.FindPassagesAsync(Scope, Query, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([settled], passages);
    }

    /// <summary>A candidate exactly at the threshold is relevant enough, so the setting reads as the least score that survives.</summary>
    [Fact]
    public async Task FindPassagesAsync_ACandidateJudgedAtTheThreshold_KeepsIt()
    {
        // Arrange
        var passage = KnowledgePassages.Create("we will pay 4200", Guid.CreateVersion7());
        var judge = new ScriptedChatModelClient().Answering(passage.StoredEmailId.ToString(), "50");
        var search = SearchOver(Retrieving(passage), judge, minimumRelevance: 50);

        // Act
        var passages = await search.FindPassagesAsync(Scope, Query, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([passage], passages);
    }

    /// <summary>A judged answer of "none of this answers you" is the filter working, and is not the failure the degradation rule is about.</summary>
    [Fact]
    public async Task FindPassagesAsync_EveryCandidateJudgedBelowTheThreshold_HandsOverNothing()
    {
        // Arrange
        var judge = new ScriptedChatModelClient().AnsweringEverythingElse("5");
        var search = SearchOver(
            Retrieving(KnowledgePassages.Create("one"), KnowledgePassages.Create("two")),
            judge,
            minimumRelevance: 50);

        // Act
        var passages = await search.FindPassagesAsync(Scope, Query, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(passages);
    }

    /// <summary>
    /// A provider that failed costs this retrieval its ranking quality and nothing else, which is what keeps one outage
    /// from reading as an empty mailbox. The pass stops there rather than asking a refusing endpoint once per remaining
    /// candidate, so everything after it is kept unjudged as well.
    /// </summary>
    [Theory]
    [InlineData(ChatGenerationFailure.RequestTimedOut)]
    [InlineData(ChatGenerationFailure.RateLimited)]
    [InlineData(ChatGenerationFailure.RequestRefused)]
    [InlineData(ChatGenerationFailure.TransportFaulted)]
    [InlineData(ChatGenerationFailure.CredentialRejected)]
    [InlineData(ChatGenerationFailure.AnswerEmpty)]
    public async Task FindPassagesAsync_AJudgementTheProviderFailed_KeepsThatCandidateAndStopsJudging(
        ChatGenerationFailure failure)
    {
        // Arrange
        var refused = KnowledgePassages.Create("we will pay 4200", Guid.CreateVersion7());
        var mentioned = KnowledgePassages.Create("the claim came up at lunch", Guid.CreateVersion7());
        var judge = new ScriptedChatModelClient()
            .Failing(refused.StoredEmailId.ToString(), failure)
            .Answering(mentioned.StoredEmailId.ToString(), "10");
        var search = SearchOver(Retrieving(refused, mentioned), judge, minimumRelevance: 50);

        // Act
        var passages = await search.FindPassagesAsync(Scope, Query, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([refused, mentioned], passages);
        Assert.Equal(1, judge.CallCount);
    }

    /// <summary>
    /// A provider that answered something other than a score is answering, so the candidates after it are still worth
    /// asking about. Only a failed call ends the pass.
    /// </summary>
    [Fact]
    public async Task FindPassagesAsync_AMalformedJudgement_GoesOnJudgingTheCandidatesAfterIt()
    {
        // Arrange
        var unreadable = KnowledgePassages.Create("we will pay 4200", Guid.CreateVersion7());
        var mentioned = KnowledgePassages.Create("the claim came up at lunch", Guid.CreateVersion7());
        var judge = new ScriptedChatModelClient()
            .Answering(unreadable.StoredEmailId.ToString(), "quite relevant")
            .Answering(mentioned.StoredEmailId.ToString(), "10");
        var search = SearchOver(Retrieving(unreadable, mentioned), judge, minimumRelevance: 50);

        // Act
        var passages = await search.FindPassagesAsync(Scope, Query, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([unreadable], passages);
        Assert.Equal(2, judge.CallCount);
    }

    /// <summary>
    /// The endpoint's concurrency limiter admits a few invocations and rejects the rest outright, so a lookup that sent
    /// its candidates together would have most of them refused by this deployment's own bulkhead — and each refusal
    /// recorded against a provider that is working.
    /// </summary>
    [Fact]
    public async Task FindPassagesAsync_SeveralCandidates_PutsThemToTheProviderOneAtATime()
    {
        // Arrange
        var judge = new ConcurrencyObservingChatModelClient();
        var search = SearchOver(
            Retrieving(
                KnowledgePassages.Create("one"),
                KnowledgePassages.Create("two"),
                KnowledgePassages.Create("three"),
                KnowledgePassages.Create("four")),
            judge,
            minimumRelevance: 50);

        // Act
        await search.FindPassagesAsync(Scope, Query, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(4, judge.CallCount);
        Assert.Equal(1, judge.GreatestCallsInFlight);
    }

    /// <summary>An answer that is not a score is refused rather than mined for one, and refusing costs the candidate its filtering rather than its place.</summary>
    [Theory]
    [InlineData("relevance: 95")]
    [InlineData("```\n95\n```")]
    [InlineData("I would say about 95 out of 100.")]
    [InlineData("ninety-five")]
    public async Task FindPassagesAsync_AJudgementThatIsNotAScore_KeepsTheCandidate(string answerText)
    {
        // Arrange
        var passage = KnowledgePassages.Create("we will pay 4200", Guid.CreateVersion7());
        var judge = new ScriptedChatModelClient().Answering(passage.StoredEmailId.ToString(), answerText);
        var search = SearchOver(Retrieving(passage), judge, minimumRelevance: 50);

        // Act
        var passages = await search.FindPassagesAsync(Scope, Query, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([passage], passages);
    }

    /// <summary>A provider already known to be failing is not asked once per candidate to establish it again.</summary>
    [Theory]
    [InlineData(AiProviderHealthState.Unavailable)]
    [InlineData(AiProviderHealthState.Misconfigured)]
    public async Task FindPassagesAsync_AnUnusableChatProvider_JudgesNothingAndHandsOverTheFusedRanking(
        AiProviderHealthState state)
    {
        // Arrange
        var first = KnowledgePassages.Create("one");
        var second = KnowledgePassages.Create("two");
        var judge = new ScriptedChatModelClient().AnsweringEverythingElse("0");
        var search = SearchOver(Retrieving(first, second), judge, minimumRelevance: 50, providerState: state);

        // Act
        var passages = await search.FindPassagesAsync(Scope, Query, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([first, second], passages);
        Assert.Equal(0, judge.CallCount);
    }

    /// <summary>The candidate count bounds what one question spends. Spending less must not drop mail nobody looked at.</summary>
    [Fact]
    public async Task FindPassagesAsync_MoreRetrievedThanTheCandidateBound_JudgesTheBoundAndKeepsTheRestUnjudged()
    {
        // Arrange
        var judged = KnowledgePassages.Create("we will pay 4200", Guid.CreateVersion7());
        var beyond = KnowledgePassages.Create("the claim came up at lunch", Guid.CreateVersion7());
        var judge = new ScriptedChatModelClient()
            .Answering(judged.StoredEmailId.ToString(), "10")
            .Answering(beyond.StoredEmailId.ToString(), "10");
        var search = SearchOver(Retrieving(judged, beyond), judge, minimumRelevance: 50, maximumCandidates: 1);

        // Act
        var passages = await search.FindPassagesAsync(Scope, Query, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([beyond], passages);
        Assert.Equal(1, judge.CallCount);
    }

    /// <summary>A lookup that found nothing has nothing to judge, and paying a provider to confirm it would be spend for no decision.</summary>
    [Fact]
    public async Task FindPassagesAsync_ARetrievalThatFoundNothing_ReachesNoProvider()
    {
        // Arrange
        var judge = new ScriptedChatModelClient().AnsweringEverythingElse("100");
        var search = SearchOver(new RecordingEmailKnowledgeSearch(), judge, minimumRelevance: 50);

        // Act
        var passages = await search.FindPassagesAsync(Scope, Query, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(passages);
        Assert.Equal(0, judge.CallCount);
    }

    /// <summary>Mail reaching a model is quoted evidence wherever it is read, and a judgement is one of the places it is read.</summary>
    [Fact]
    public async Task FindPassagesAsync_ACandidate_PutsItToTheModelAsQuotedEvidenceBesideItsQuery()
    {
        // Arrange
        var passage = KnowledgePassages.Create("we will pay 4200", Guid.CreateVersion7(), subject: "Claim 41");
        var judge = new ScriptedChatModelClient().Answering(passage.StoredEmailId.ToString(), "95");
        var search = SearchOver(Retrieving(passage), judge, minimumRelevance: 50);

        // Act
        await search.FindPassagesAsync(Scope, Query, TestContext.Current.CancellationToken);

        // Assert
        var conversation = Assert.Single(judge.Conversations);

        Assert.Equal(ChatRole.System, conversation[0].Role);
        Assert.Equal(PassageRelevanceInstructions.Text, conversation[0].Text);
        Assert.Equal(ChatRole.User, conversation[1].Role);
        Assert.Contains(
            $"<{PassageRelevanceInstructions.QueryElementName}>{Query}</{PassageRelevanceInstructions.QueryElementName}>",
            conversation[1].Text,
            StringComparison.Ordinal);
        Assert.Contains(
            RetrievedMailContextFormatter.Format([passage]),
            conversation[1].Text,
            StringComparison.Ordinal);
    }

    /// <summary>A cancelled question stops: cancellation is the caller's decision, never a judgement that could not be made.</summary>
    [Fact]
    public async Task FindPassagesAsync_ACancelledCaller_PropagatesTheCancellation()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        var passage = KnowledgePassages.Create("we will pay 4200", Guid.CreateVersion7());
        var judge = new ScriptedChatModelClient().Answering(passage.StoredEmailId.ToString(), "95");
        var search = SearchOver(Retrieving(passage), judge, minimumRelevance: 50);

        await cancellation.CancelAsync();

        // Act, Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => search.FindPassagesAsync(Scope, Query, cancellation.Token));
    }

    [Fact]
    public void Constructor_WithoutACollaborator_IsRefused()
    {
        // Arrange
        var plan = PassageRelevanceFilterPlan.Create(maximumCandidates: 4, minimumRelevance: 50);
        var health = Substitute.For<IAiProviderHealthReader>();

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => new ModelJudgedKnowledgeSearch(
            null!,
            new ScriptedChatModelClient(),
            health,
            plan,
            NullLogger<ModelJudgedKnowledgeSearch>.Instance));
        Assert.Throws<ArgumentNullException>(() => new ModelJudgedKnowledgeSearch(
            new RecordingEmailKnowledgeSearch(),
            null!,
            health,
            plan,
            NullLogger<ModelJudgedKnowledgeSearch>.Instance));
        Assert.Throws<ArgumentNullException>(() => new ModelJudgedKnowledgeSearch(
            new RecordingEmailKnowledgeSearch(),
            new ScriptedChatModelClient(),
            null!,
            plan,
            NullLogger<ModelJudgedKnowledgeSearch>.Instance));
        Assert.Throws<ArgumentNullException>(() => new ModelJudgedKnowledgeSearch(
            new RecordingEmailKnowledgeSearch(),
            new ScriptedChatModelClient(),
            health,
            null!,
            NullLogger<ModelJudgedKnowledgeSearch>.Instance));
        Assert.Throws<ArgumentNullException>(() => new ModelJudgedKnowledgeSearch(
            new RecordingEmailKnowledgeSearch(),
            new ScriptedChatModelClient(),
            health,
            plan,
            null!));
    }

    private static RecordingEmailKnowledgeSearch Retrieving(params EmailKnowledgePassage[] passages) =>
        new RecordingEmailKnowledgeSearch().Returning(Query, passages);

    private static ModelJudgedKnowledgeSearch SearchOver(
        IEmailKnowledgeSearch rankedSearch,
        IChatModelClient judge,
        int minimumRelevance,
        int maximumCandidates = 8,
        AiProviderHealthState providerState = AiProviderHealthState.Serving)
    {
        var providerHealthReader = Substitute.For<IAiProviderHealthReader>();
        providerHealthReader
            .Read(AiProviderRole.Chat)
            .Returns(new AiProviderHealth(AiProviderRole.Chat, providerState, ObservedAt: null));

        return new ModelJudgedKnowledgeSearch(
            rankedSearch,
            judge,
            providerHealthReader,
            PassageRelevanceFilterPlan.Create(maximumCandidates, minimumRelevance),
            NullLogger<ModelJudgedKnowledgeSearch>.Instance);
    }

    /// <summary>Answers every judgement and records how many calls were ever in flight at once.</summary>
    /// <remarks>
    /// It yields before answering, which is what makes the observation mean anything: a caller that dispatched its
    /// candidates together would have every call past the yield before the first one resumed, while one that awaits
    /// each in turn can never hold two.
    /// </remarks>
    private sealed class ConcurrencyObservingChatModelClient : IChatModelClient
    {
        private readonly Lock gate = new();
        private int callsInFlight;

        public int CallCount { get; private set; }

        public int GreatestCallsInFlight { get; private set; }

        public async Task<ChatAnswer> AnswerAsync(
            IReadOnlyList<ChatMessage> conversation,
            CancellationToken cancellationToken)
        {
            lock (this.gate)
            {
                this.callsInFlight++;
                this.CallCount++;
                this.GreatestCallsInFlight = Math.Max(this.GreatestCallsInFlight, this.callsInFlight);
            }

            await Task.Yield();

            lock (this.gate)
            {
                this.callsInFlight--;
            }

            return new ChatAnswer("100", ChatGenerationStop.Completed, Usage: null);
        }
    }
}
