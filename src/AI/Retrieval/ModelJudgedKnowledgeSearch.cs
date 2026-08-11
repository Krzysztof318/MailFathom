// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.AiProviders;
using MailFathom.Application.Chat;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Retrieval;
using Microsoft.Extensions.Logging;

namespace MailFathom.AI.Retrieval;

/// <summary>Puts the fused ranking's own candidates to the model, one question each, and hands over the ones that answer the query.</summary>
/// <remarks>
/// <para>
/// The second pass over a retrieval, and a decorator rather than a step inside one, because the first pass is a ranking
/// this deployment already serves: hybrid search fuses lexical and vector similarity, which decides what *resembles* a
/// query. Resemblance is cheap, deterministic, and shallow — a thread that discusses a claim ranks beside the one
/// message that settles it. This decides which of them answers.
/// </para>
/// <para>
/// What a candidate is judged against is the whole lookup rather than its text, filters included. A lookup that is
/// mostly a narrowing leaves a query text of a word or two, and judging a candidate against those words alone would
/// drop exactly the mail the narrowing was written to find — the filters had already selected it, and the pass would be
/// second-guessing a selection the database made precisely.
/// </para>
/// <para>
/// It filters and never reorders. The fused ranking is computed across every candidate at once, while a judgement is
/// made about one candidate in isolation, so sorting by judgement would replace a ranking with a set of unrelated
/// opinions. What survives is a subsequence of the ordering retrieval produced, which is also what makes the degraded
/// path the undegraded one minus nothing.
/// </para>
/// <para>
/// Every way this can fail keeps a passage rather than dropping it. A provider that is unreachable, refusing, throttled,
/// slow, or answering something other than a score costs this retrieval its ranking quality and nothing else: the
/// fused ordering is already a usable answer, and a filter that failed closed would turn one degraded provider into a
/// mailbox that appears to hold nothing. A candidate the model *did* judge and score below the threshold is a different
/// thing and is dropped, including when that leaves nothing — a question whose mail does not answer it is answered by
/// saying so.
/// </para>
/// <para>
/// Judgements are made one after another, and the first provider failure ends the pass. Both follow from the resilience
/// budget these calls travel: the endpoint's concurrency limiter admits a few invocations at a time and rejects the rest
/// outright rather than queueing them, so a lookup that dispatched its whole candidate list at once would have most of
/// it refused by this deployment's own bulkhead — and each of those refusals would be recorded against the provider as
/// an outage it is not having. Stopping at the first failure is that reasoning applied in time rather than in width:
/// once the endpoint has refused, asking it again per remaining candidate buys the same answer while a question waits.
/// </para>
/// <para>
/// What it costs is stated rather than hidden: a lookup takes as long as its judgements do, one after the other, and a
/// run makes several lookups. The candidate count is the setting that bounds it, in latency exactly as in spend.
/// </para>
/// </remarks>
internal sealed class ModelJudgedKnowledgeSearch : IEmailKnowledgeSearch
{
    private readonly IEmailKnowledgeSearch rankedSearch;
    private readonly IChatModelClient chatModelClient;
    private readonly IAiProviderHealthReader providerHealthReader;
    private readonly PassageRelevanceFilterPlan plan;
    private readonly ILogger<ModelJudgedKnowledgeSearch> logger;

    /// <summary>Initializes the second pass over the retrieval it filters.</summary>
    /// <param name="rankedSearch">Produces the fused ranking every judgement is drawn from.</param>
    /// <param name="chatModelClient">Answers what one candidate is worth against the query.</param>
    /// <param name="providerHealthReader">Answers what the last call to the chat provider established about it.</param>
    /// <param name="plan">How many candidates one retrieval may judge, and how relevant one has to be.</param>
    /// <param name="logger">Records what the pass decided, without recording any query, extract, or judgement.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public ModelJudgedKnowledgeSearch(
        IEmailKnowledgeSearch rankedSearch,
        IChatModelClient chatModelClient,
        IAiProviderHealthReader providerHealthReader,
        PassageRelevanceFilterPlan plan,
        ILogger<ModelJudgedKnowledgeSearch> logger)
    {
        ArgumentNullException.ThrowIfNull(rankedSearch);
        ArgumentNullException.ThrowIfNull(chatModelClient);
        ArgumentNullException.ThrowIfNull(providerHealthReader);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(logger);

        this.rankedSearch = rankedSearch;
        this.chatModelClient = chatModelClient;
        this.providerHealthReader = providerHealthReader;
        this.plan = plan;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<EmailKnowledgeLookup> FindPassagesAsync(
        MailboxScope scope,
        EmailKnowledgeQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var ranked = await this.rankedSearch.FindPassagesAsync(scope, query, cancellationToken);
        var rankedPassages = ranked.Passages;

        if (rankedPassages.Count is 0)
        {
            return ranked;
        }

        var health = this.providerHealthReader.Read(AiProviderRole.Chat);

        if (health.State is AiProviderHealthState.Unavailable or AiProviderHealthState.Misconfigured)
        {
            // No staleness window is needed here, which is the difference from the semantic search gate. A retrieval
            // happens inside an answering run, and that run reached this point by making a chat call of its own, so the
            // state read here was established moments ago by the very conversation being answered.
            PassageRelevanceEvents.LogProviderUnusable(this.logger, health.State, rankedPassages.Count);

            // Reported as a fallback for the same reason a failure part way through the pass is: what the caller
            // received is the ranking nobody judged, and a run recorded as filtered when it was not would say a
            // deployment's second pass decided something it never saw.
            return ranked with { RelevanceFilterFellBack = true };
        }

        IReadOnlyList<EmailKnowledgePassage> candidates = rankedPassages.Count <= this.plan.MaximumCandidates
            ? rankedPassages
            : [.. rankedPassages.Take(this.plan.MaximumCandidates)];

        var judged = await this.JudgeInTurnAsync(query, ranked.RetrievalMode, candidates, cancellationToken);

        // The unjudged remainder keeps the position the fused ranking gave it. A candidate count below what the search
        // returned bounds what a question spends, and spending less must not silently drop mail nobody looked at.
        IReadOnlyList<EmailKnowledgePassage> kept = [.. judged.Kept, .. rankedPassages.Skip(candidates.Count)];

        // The count the pass reached rather than the pool it was given, which differ whenever a provider failure ended
        // it early: a record saying it judged the whole pool in the same breath as one saying it stopped part way
        // through would leave neither believable.
        PassageRelevanceEvents.LogJudged(
            this.logger,
            rankedPassages.Count,
            judged.JudgedCount,
            rankedPassages.Count - kept.Count);

        // The candidate count is the ranking's own, unchanged: this pass narrows what is handed over and never what was
        // considered, so the pair says how much of a lookup the filter dropped. The mode is the ranking's own for the
        // same reason — judging decides what survives a ranking and never how the ranking was produced.
        return new EmailKnowledgeLookup(
            kept,
            ranked.RetrievalMode,
            ranked.CandidateCount,
            judged.JudgedCount < candidates.Count);
    }

    /// <summary>Judges the candidates in the order retrieval ranked them, and stops the moment the provider refuses.</summary>
    /// <remarks>
    /// A candidate reached after the provider failed is kept rather than skipped over, so ending the pass early narrows
    /// what was filtered and never what is handed over. That is the same rule the per-candidate one follows: nothing the
    /// model did not judge is dropped. It reports how far it got beside what it kept, because the two stop agreeing the
    /// moment a failure ends the pass and only the caller records either.
    /// </remarks>
    private async Task<JudgedCandidates> JudgeInTurnAsync(
        EmailKnowledgeQuery query,
        EmailSearchRetrievalMode retrievalMode,
        IReadOnlyList<EmailKnowledgePassage> candidates,
        CancellationToken cancellationToken)
    {
        List<EmailKnowledgePassage> kept = [];

        for (var position = 0; position < candidates.Count; position++)
        {
            var judgement = await this.JudgeAsync(
                query,
                candidates[position],
                retrievalMode,
                cancellationToken);

            if (judgement.ProviderFailure is { } failure)
            {
                // The candidate this call was about received no determination either, so it is the first of the
                // unjudged rather than the last of the judged.
                PassageRelevanceEvents.LogJudgingStopped(this.logger, failure, candidates.Count - position);
                kept.AddRange(candidates.Skip(position));

                return new JudgedCandidates(kept, position);
            }

            if (judgement.Score is not { } score || score >= this.plan.MinimumRelevance)
            {
                kept.Add(candidates[position]);
            }
        }

        return new JudgedCandidates(kept, candidates.Count);
    }

    /// <summary>Asks the model what one candidate is worth against the query, or reports why it did not say.</summary>
    /// <remarks>
    /// A malformed answer and a failed call are separated here because they say different things about what to do next:
    /// a provider that answered something other than a score is answering, so the candidates after this one are still
    /// worth asking about, while one that failed is not. Cancellation is deliberately not caught: the caller stopping
    /// the run is not a judgement that could not be made, and swallowing it would leave a cancelled question still
    /// spending provider calls.
    /// </remarks>
    private async Task<PassageJudgement> JudgeAsync(
        EmailKnowledgeQuery query,
        EmailKnowledgePassage candidate,
        EmailSearchRetrievalMode retrievalMode,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ChatMessage> conversation =
        [
            new ChatMessage(ChatRole.System, PassageRelevanceInstructions.Text),
            new ChatMessage(
                ChatRole.User,
                PassageRelevanceInstructions.ComposeJudgementTurn(query, candidate, retrievalMode)),
        ];

        try
        {
            var answer = await this.chatModelClient.AnswerAsync(conversation, cancellationToken);

            if (PassageRelevanceJudgement.Read(answer.Text) is { } score)
            {
                return new PassageJudgement(score, ProviderFailure: null);
            }

            PassageRelevanceEvents.LogJudgementMalformed(this.logger);

            return new PassageJudgement(Score: null, ProviderFailure: null);
        }
        catch (ChatGenerationFailedException failure)
        {
            return new PassageJudgement(Score: null, failure.Failure);
        }
    }

    /// <summary>What one judgement established: a score, an answer that was not one, or a provider that did not answer.</summary>
    /// <param name="Score">What the model judged the candidate worth, or <see langword="null" /> where no score was read.</param>
    /// <param name="ProviderFailure">What ended the call, or <see langword="null" /> where the provider answered.</param>
    /// <remarks>
    /// The two absences are separate members rather than one, because a null score alone cannot say whether the pass may
    /// go on: an unreadable answer and an unreachable endpoint both produce no score and only one of them is a reason to
    /// stop asking.
    /// </remarks>
    private readonly record struct PassageJudgement(int? Score, ChatGenerationFailure? ProviderFailure);

    /// <summary>What the pass handed back: the passages it kept, and how many candidates it reached before it ended.</summary>
    /// <param name="Kept">The candidates that survived, in the order retrieval ranked them.</param>
    /// <param name="JudgedCount">How many candidates received a determination, which is fewer than the pool wherever a provider failure ended the pass.</param>
    private readonly record struct JudgedCandidates(IReadOnlyList<EmailKnowledgePassage> Kept, int JudgedCount);
}
