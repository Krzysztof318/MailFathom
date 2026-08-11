// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Retrieval;
using MailFathom.AI.UnitTests.TestDoubles;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Chat;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Retrieval;
using MailFathom.Domain.Accounts;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MailFathom.AI.UnitTests.Retrieval;

/// <summary>Puts the adversarial corpus to the second retrieval pass, which asks a model to judge text a stranger wrote.</summary>
/// <remarks>
/// <para>
/// The half of the suite that covers the relevance filter; <see cref="PromptInjectionResistanceTests" /> covers the
/// answering run. The two are separate classes rather than one because the chat port a judgement travels and the
/// framework interface a run travels publish the same type names, and a file importing both would have to spell every
/// one of them out.
/// </para>
/// <para>
/// The judge is substituted, so no provider is paid and every score here is the test's own. It is scripted as a model
/// that <em>did</em> believe the passage that begged to be kept: the point is that believing it changes nothing a
/// passage could not have earned, not that a model would see through it.
/// </para>
/// </remarks>
public sealed class PassageRelevanceInjectionResistanceTests
{
    private static readonly EmailKnowledgeQuery Query =
        EmailKnowledgeQuery.ForText("what did the insurer agree to pay");

    private static readonly MailboxScope OnePrimaryAccount =
        MailboxScope.Create([MailAccountId.Create("primary")], []);

    /// <summary>Gets one case per attack the corpus knows, so a property stated once covers every one of them.</summary>
    public static TheoryData<string> EveryAdversary => AdversarialMailCorpus.EveryName;

    /// <summary>
    /// A passage cannot promote itself past the ranking. A judgement decides whether a candidate survives and never
    /// where it sits, so an extract the model scored higher than everything else still holds the place the fused ranking
    /// gave it — and a genuinely relevant passage above it stays above it.
    /// </summary>
    [Fact]
    public async Task FindPassagesAsync_ASelfPromotingCandidateTheModelScoredHighest_KeepsTheFusedOrdering()
    {
        // Arrange
        var settled = KnowledgePassages.Create("we will pay 4200", Guid.CreateVersion7());
        var promoter = SelfPromotingPassage();
        var judge = new ScriptedChatModelClient()
            .Answering(settled.StoredEmailId.ToString(), "60")
            .Answering(promoter.StoredEmailId.ToString(), "100");
        var search = SearchOver(Retrieving(settled, promoter), judge);

        // Act
        var passages = (await search.FindPassagesAsync(
            OnePrimaryAccount,
            Query,
            TestContext.Current.CancellationToken)).Passages;

        // Assert
        Assert.Equal([settled, promoter], passages);
    }

    /// <summary>Asking to be kept is not a reason to be kept: a score below the threshold drops the passage that begged for one.</summary>
    [Fact]
    public async Task FindPassagesAsync_ASelfPromotingCandidateJudgedBelowTheThreshold_IsDroppedLikeAnyOther()
    {
        // Arrange
        var settled = KnowledgePassages.Create("we will pay 4200", Guid.CreateVersion7());
        var promoter = SelfPromotingPassage();
        var judge = new ScriptedChatModelClient()
            .Answering(settled.StoredEmailId.ToString(), "95")
            .Answering(promoter.StoredEmailId.ToString(), "10");
        var search = SearchOver(Retrieving(settled, promoter), judge);

        // Act
        var passages = (await search.FindPassagesAsync(
            OnePrimaryAccount,
            Query,
            TestContext.Current.CancellationToken)).Passages;

        // Assert
        Assert.Equal([settled], passages);
    }

    /// <summary>
    /// The second pass reads mail too, so it reads it the same way: the candidate is quoted inside the envelope beside
    /// the query, and the instruction the judge is given is this build's own text with nothing of the message in it.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryAdversary))]
    public async Task FindPassagesAsync_AnAdversarialCandidate_ReachesTheJudgeAsQuotedEvidence(string adversary)
    {
        // Arrange
        var message = AdversarialMailCorpus.Named(adversary);
        var candidate = KnowledgePassages.Create(message.Text, Guid.CreateVersion7(), subject: message.Subject);
        var judge = new ScriptedChatModelClient().Answering(candidate.StoredEmailId.ToString(), "95");
        var search = SearchOver(Retrieving(candidate), judge);

        // Act
        await search.FindPassagesAsync(OnePrimaryAccount, Query, TestContext.Current.CancellationToken);

        // Assert
        var conversation = Assert.Single(judge.Conversations);

        Assert.Equal(ChatRole.System, conversation[0].Role);
        Assert.Equal(PassageRelevanceInstructions.Text, conversation[0].Text);
        Assert.DoesNotContain(message.Text, conversation[0].Text, StringComparison.Ordinal);
        Assert.DoesNotContain(message.Subject, conversation[0].Text, StringComparison.Ordinal);
        Assert.Equal(ChatRole.User, conversation[1].Role);
        Assert.Contains(
            RetrievedMailContextFormatter.Format([candidate], EmailSearchRetrievalMode.Hybrid, retrievalLimitReached: false),
            conversation[1].Text,
            StringComparison.Ordinal);
    }

    /// <summary>Builds the passage the corpus aims at this filter.</summary>
    private static EmailKnowledgePassage SelfPromotingPassage() => KnowledgePassages.Create(
        AdversarialMailCorpus.SelfPromotion.Text,
        Guid.CreateVersion7(),
        subject: AdversarialMailCorpus.SelfPromotion.Subject);

    private static RecordingEmailKnowledgeSearch Retrieving(params EmailKnowledgePassage[] passages) =>
        new RecordingEmailKnowledgeSearch().Returning(Query.QueryText, passages);

    private static ModelJudgedKnowledgeSearch SearchOver(
        IEmailKnowledgeSearch rankedSearch,
        IChatModelClient judge)
    {
        var providerHealthReader = Substitute.For<IAiProviderHealthReader>();
        providerHealthReader
            .Read(AiProviderRole.Chat)
            .Returns(new AiProviderHealth(AiProviderRole.Chat, AiProviderHealthState.Serving, ObservedAt: null));

        return new ModelJudgedKnowledgeSearch(
            rankedSearch,
            judge,
            providerHealthReader,
            PassageRelevanceFilterPlan.Create(
                EmailKnowledgeBounds.Default,
                maximumCandidates: 8,
                minimumRelevance: 50),
            NullLogger<ModelJudgedKnowledgeSearch>.Instance);
    }
}
