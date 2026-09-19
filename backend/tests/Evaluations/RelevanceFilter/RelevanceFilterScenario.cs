// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.AI.Chat;
using MailFathom.AI.Retrieval;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Retrieval;
using MailFathom.Domain.Accounts;
using MailFathom.Evaluations.Costing;
using MailFathom.Evaluations.Providers;
using MailFathom.Evaluations.Reporting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Reporting;
using Microsoft.Extensions.Logging.Abstractions;

namespace MailFathom.Evaluations.RelevanceFilter;

/// <summary>Runs the relevance filter over the labelled candidates under one model and every threshold, and files what it kept.</summary>
/// <remarks>
/// <para>
/// The filter is the one a deployment composes, <see cref="ModelJudgedKnowledgeSearch" />, over a ranking that hands it
/// the labelled candidates, so what is measured is its instruction, its reading of an answer, and its rule for what
/// survives — including that an answer it cannot read keeps the passage. Only the ranking and the chat port are the
/// scenario's own.
/// </para>
/// <para>
/// Every threshold runs the filter afresh, and only the first pays: a judgement is the same conversation whatever the
/// threshold is, so the run's response cache answers it from the second threshold on. A run therefore costs one call per
/// labelled candidate however many thresholds it reports, and those calls are the filter's own — no judge is opened.
/// </para>
/// </remarks>
internal static class RelevanceFilterScenario
{
    /// <summary>The name the scenario is filed and reported under.</summary>
    public const string Name = "RelevanceFilter.LabelledCandidates";

    private static readonly MailboxScope Scope = MailboxScope.Create([MailAccountId.Create("evaluation")], []);

    /// <summary>Gets what every run of this scenario is measured on.</summary>
    public static IReadOnlyList<IEvaluator> Evaluators => [new RelevanceFilterEvaluator()];

    /// <summary>Runs the scenario under one model and files the measurement in the run's store.</summary>
    /// <param name="reporting">The run's store, opened without a judge.</param>
    /// <param name="model">The model under test's client.</param>
    /// <param name="plan">The plan the model is measured with, whose routed name is what the result is filed under.</param>
    /// <param name="repetition">Which repetition of the case this is, counted from one, which the result and the cached answer are filed under.</param>
    /// <param name="modelSpend">What reaching that model has cost, which is the meter its client is opened over.</param>
    /// <param name="cancellationToken">Withdraws the run.</param>
    /// <returns>The metrics the measurement produced.</returns>
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Disposing the caching wrapper would dispose the caller's model client, which this scenario does not own.")]
    public static async Task<EvaluationResult> RunAsync(
        ReportingConfiguration reporting,
        IChatClient model,
        ChatGenerationPlan plan,
        int repetition,
        SpendMeter modelSpend,
        CancellationToken cancellationToken)
    {
        var modelName = plan.Endpoint.RoutedModelName;
        var iterationName = EvaluationStore.IterationNameFor(modelName, repetition);

        await using var scenarioRun = await reporting.CreateScenarioRunAsync(
            Name,
            iterationName,
            cancellationToken: cancellationToken);

        var cachedModel = await EvaluationStore.CacheOverAsync(reporting, model, plan, Name, iterationName, cancellationToken);
        var judge = new ScenarioChatModelClient(cachedModel, plan);
        var lookups = LabelledCandidates.Lookups
            .Select(static lookup => new ResolvedLookup(lookup, [.. lookup.Candidates.Select(static candidate => candidate.Resolve())]))
            .ToArray();

        List<RelevanceFilterTally> tallies = [];

        foreach (var threshold in RelevanceFilterEvaluator.Thresholds)
        {
            tallies.Add(await TallyAsync(judge, lookups, threshold, cancellationToken));
        }

        var candidates = LabelledCandidates.Lookups.SelectMany(static lookup => lookup.Candidates).ToArray();
        var measurement = new RelevanceFilterMeasurement(
            tallies,
            candidates.Count(static candidate => candidate.Answers),
            candidates.Count(static candidate => !candidate.Answers));

        var verdict = await scenarioRun.EvaluateAsync(
            [new ChatMessage(ChatRole.User, DescribeLookups())],
            new ChatResponse(new ChatMessage(ChatRole.Assistant, measurement.Summary)) { ModelId = modelName },
            [measurement],
            cancellationToken);

        EvaluationCost.Record(verdict, modelName, modelSpend.Take(), judgeSpend: default);

        return verdict;
    }

    /// <summary>Runs the filter over every labelled lookup under one threshold, and counts what it kept by label.</summary>
    /// <remarks>Lookups run one after another, as a deployment's retrievals within one run do, so a provider's rate limit sees one caller.</remarks>
    private static async Task<RelevanceFilterTally> TallyAsync(
        ScenarioChatModelClient judge,
        IReadOnlyList<ResolvedLookup> lookups,
        int threshold,
        CancellationToken cancellationToken)
    {
        var answeringKept = 0;
        var notAnsweringKept = 0;
        var lookupsFellBack = 0;

        foreach (var lookup in lookups)
        {
            var filter = new ModelJudgedKnowledgeSearch(
                new LabelledRanking(lookup.Passages),
                judge,
                ServingProvider.Instance,
                PassageRelevanceFilterPlan.Create(EmailKnowledgeBounds.Default, lookup.Passages.Count, threshold),
                NullLogger<ModelJudgedKnowledgeSearch>.Instance);

            var found = await filter.FindPassagesAsync(Scope, lookup.Labelled.Query, cancellationToken);
            var kept = lookup.Labelled.Candidates
                .Where((_, position) => found.Passages.Contains(lookup.Passages[position]))
                .ToArray();

            answeringKept += kept.Count(static candidate => candidate.Answers);
            notAnsweringKept += kept.Count(static candidate => !candidate.Answers);
            lookupsFellBack += found.RelevanceFilterFellBack ? 1 : 0;
        }

        return new RelevanceFilterTally(threshold, answeringKept, notAnsweringKept, lookupsFellBack);
    }

    private static string DescribeLookups() =>
        string.Join(
            '\n',
            LabelledCandidates.Lookups.Select(static lookup =>
                $"{lookup.QueryText} — {lookup.Candidates.Count(static candidate => candidate.Answers)} of {lookup.Candidates.Count} candidates answer it."));

    /// <summary>A labelled lookup beside the passages its candidates resolved to, in the same order.</summary>
    private sealed record ResolvedLookup(LabelledLookup Labelled, IReadOnlyList<EmailKnowledgePassage> Passages);

    /// <summary>The ranking the filter is given: the labelled candidates, in the order they were labelled.</summary>
    private sealed class LabelledRanking(IReadOnlyList<EmailKnowledgePassage> passages) : IEmailKnowledgeSearch
    {
        public Task<EmailKnowledgeLookup> FindPassagesAsync(
            MailboxScope scope,
            EmailKnowledgeQuery query,
            CancellationToken cancellationToken) =>
            Task.FromResult(EmailKnowledgeLookup.Unfiltered(passages, EmailSearchRetrievalMode.Hybrid));
    }

    /// <summary>A provider this run has no reason to doubt, so the filter always attempts its judgements.</summary>
    private sealed class ServingProvider : IAiProviderHealthReader
    {
        public static ServingProvider Instance { get; } = new();

        public AiProviderHealth Read(AiProviderRole role) =>
            new(role, AiProviderHealthState.Serving, ObservedAt: null);
    }
}
