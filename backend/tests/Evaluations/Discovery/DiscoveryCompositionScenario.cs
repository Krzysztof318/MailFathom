// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using MailFathom.AI.Chat;
using MailFathom.AI.Discovery;
using MailFathom.AI.Orchestration;
using MailFathom.Application.Discovery.Planning;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Discovery.Presentation.Blocks;
using MailFathom.Application.Discovery.Runs;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Retrieval;
using MailFathom.Evaluations.Answering;
using MailFathom.Evaluations.Corpus;
using MailFathom.Evaluations.Costing;
using MailFathom.Evaluations.Reporting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;
using Microsoft.Extensions.AI.Evaluation.Reporting;
using Microsoft.Extensions.Logging.Abstractions;

namespace MailFathom.Evaluations.Discovery;

/// <summary>One question put to the Discover composing agent with the extracts a run would hand it, and what a result must rest on.</summary>
/// <remarks>
/// <para>
/// The extracts are what the corpus search answers the scenario's lookup with, declared as sources the way a run
/// declares them, and the agent is composed, asked, and read the way a deployment composes, asks, and reads it — so a
/// verdict is about the instruction and the model rather than a copy of either. What it leaves out is what decides
/// whether a composition happens rather than what it says: the ledgers, the egress guard, and the fallback chain.
/// </para>
/// <para>
/// What a result cites is a structure and is asserted plainly: that the answer read as the shape the agent was told to
/// answer in, that every source it named anywhere was one it was handed, and that it rests on the extracts holding the
/// evidence — or, for a question the extracts do not answer, that it was composed as unanswered. The judge grades only
/// the prose: whether the result answers the question, and whether it says only what the extracts say.
/// </para>
/// </remarks>
/// <param name="Name">The name the scenario is filed and reported under.</param>
/// <param name="Question">The question, as the owner of the mailbox would ask it.</param>
/// <param name="Intent">What the question was read as, which decides the block the result opens with.</param>
/// <param name="Lookup">The lookup a run's plan would have made for it, which is what the extracts are retrieved by.</param>
/// <param name="Evidence">
/// Phrases the result must rest on, each carried by the extract of some source the opening block cites; empty for a
/// question the extracts do not answer, where the result must be composed as unanswered.
/// </param>
/// <param name="MinimumRelevance">The lowest relevance rating, from one to five, a model may score, or <see langword="null" /> where the judge is not asked.</param>
/// <param name="MinimumGroundedness">The lowest groundedness rating, from one to five, a model may score, or <see langword="null" /> where the judge is not asked.</param>
internal sealed record DiscoveryCompositionScenario(
    string Name,
    string Question,
    DiscoveryIntent Intent,
    string Lookup,
    IReadOnlyList<string> Evidence,
    int? MinimumRelevance,
    int? MinimumGroundedness)
{
    /// <summary>The check that the answer read as the shape the agent was told to answer in.</summary>
    public const string ReadAsAResultMetricName = "Read as a result";

    /// <summary>The check that every source the answer named was one it was handed.</summary>
    public const string CitesOfferedSourcesMetricName = "Cites only offered sources";

    /// <summary>The check that the result opens with the block the question's intent calls for.</summary>
    public const string OpensAsTheIntentAsksMetricName = "Opens as the intent asks";

    /// <summary>The check that the result rests on the evidence, or is composed as unanswered where there is none.</summary>
    public const string RestsOnEvidenceMetricName = "Rests on the evidence";

    /// <summary>When the run read the mailbox, which is fixed so a result's freshness is the same on every run.</summary>
    private static readonly DateTimeOffset ObservedAt = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Gets every question the agent is measured on.</summary>
    public static IReadOnlyList<DiscoveryCompositionScenario> All { get; } =
    [
        new(
            "DiscoveryComposition.OneMessageAnswers",
            "What error message did LumenDesk show when the filtered project export failed?",
            DiscoveryIntent.FindFact,
            "LumenDesk export error",
            ["permitted buffer size"],
            MinimumRelevance: 4,
            MinimumGroundedness: 4),
        new(
            "DiscoveryComposition.KeywordMatchesTheWrongMessage",
            "What corrected billing address did Lumenfield Supplies' invoice INV-4827 need?",
            DiscoveryIntent.FindFact,
            "INV-4827 billing address",
            ["42 Lantern Way"],
            MinimumRelevance: 4,
            MinimumGroundedness: 4),
        new(
            "DiscoveryComposition.TracksAChange",
            "How did the Atlas Importer tide-reading import problem get fixed over time?",
            DiscoveryIntent.TrackChange,
            "Atlas Importer tide",
            ["2.8.4"],
            MinimumRelevance: 4,
            MinimumGroundedness: 4),
        new(
            "DiscoveryComposition.ExtractsDoNotAnswer",
            "What cancellation fee does the hotel on the Solmere trip charge?",
            DiscoveryIntent.FindFact,
            "Solmere hotel",
            [],

            // The one right result says the extracts do not answer, which the structural check settles on its own;
            // a judge grading that sentence for relevance would grade the honesty the check already required.
            MinimumRelevance: null,
            MinimumGroundedness: null),
    ];

    /// <summary>Gets what this scenario is judged on: a rating for each threshold it records.</summary>
    public IReadOnlyList<IEvaluator> Evaluators =>
    [
        .. this.MinimumRelevance is null ? [] : new IEvaluator[] { new RelevanceEvaluator() },
        .. this.MinimumGroundedness is null ? [] : new IEvaluator[] { new GroundednessEvaluator() },
    ];

    /// <summary>Reads the extracts a run would hand the agent for this question, declared as the sources it may cite.</summary>
    /// <param name="cancellationToken">Withdraws the lookup.</param>
    /// <returns>What the lookup found, and the sources declared over it.</returns>
    public async Task<(DiscoveryEvidence Evidence, IReadOnlyList<DiscoveryComposedSource> Sources)> RetrieveAsync(
        CancellationToken cancellationToken)
    {
        var lookup = await new CorpusKnowledgeSearch(CorpusMessage.All).FindPassagesAsync(
            CorpusKnowledgeSearch.Scope,
            EmailKnowledgeQuery.ForText(this.Lookup),
            cancellationToken);

        var evidence = new DiscoveryEvidence(
            lookup.Passages,
            EmailSearchRetrievalMode.Lexical,
            LookupsRun: 1,
            LookupsRefused: 0,
            RetrievalTruncated: false);

        return (evidence, DiscoveryComposedSources.Declare(lookup.Passages));
    }

    /// <summary>Puts the question and its extracts to one model, checks the result, has the judge grade it, and files the verdict in the run's store.</summary>
    /// <param name="reporting">The run's store, judge, and name, opened with <see cref="Evaluators" />.</param>
    /// <param name="model">The model under test's client.</param>
    /// <param name="plan">The plan the model is measured with, whose routed name is what the result is filed under.</param>
    /// <param name="repetition">Which repetition of the case this is, counted from one, which the result and the cached answer are filed under.</param>
    /// <param name="modelSpend">What reaching that model has cost, which is the meter its client is opened over.</param>
    /// <param name="judgeSpend">What reaching the judge has cost, which is the meter the run's judge is opened over.</param>
    /// <param name="cancellationToken">Withdraws the run.</param>
    /// <returns>The verdict, carrying every check and every rating as a metric.</returns>
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Disposing the caching wrapper would dispose the caller's model client, which this scenario does not own.")]
    public async Task<EvaluationResult> RunAsync(
        ReportingConfiguration reporting,
        IChatClient model,
        ChatGenerationPlan plan,
        int repetition,
        SpendMeter modelSpend,
        SpendMeter judgeSpend,
        CancellationToken cancellationToken)
    {
        var modelName = plan.Endpoint.RoutedModelName;
        var iterationName = EvaluationStore.IterationNameFor(modelName, repetition);

        await using var scenarioRun = await reporting.CreateScenarioRunAsync(
            this.Name,
            iterationName,
            cancellationToken: cancellationToken);

        var (evidence, sources) = await this.RetrieveAsync(cancellationToken);

        // A deployment guards each source before it composes the turn; the guard a scenario runs under is inactive
        // and would hand back the same text, so the turn is composed from the sources as they were declared.
        var turn = DiscoveryCompositionInstructions.ComposeCompositionTurn(
            this.Question,
            this.Intent,
            [.. sources.Select(static source => new DiscoveryTurnSource(source.Citation.Id.Value, source.Citation.Label.Value, source.Extract))]);

        var cachedModel = await EvaluationStore.CacheOverAsync(reporting, model, plan, this.Name, iterationName, cancellationToken);
        var agent = DiscoveryCompositionAgentComposition.Compose(
            cachedModel,
            plan,
            new EmptyAgentInstructionEnvelope(),
            NullLoggerFactory.Instance);

        var answer = await agent.RunAsync(turn, session: null, options: null, cancellationToken);
        var result = DiscoveryCompositionReading.Read(
            answer.Text,
            DiscoveryRunPlan.Compose(this.Intent, RetrievalPlan.Create(EmailKnowledgeBounds.Default, [EmailKnowledgeQuery.ForText(this.Lookup)], sufficientPassages: 5)),
            sources,
            evidence,
            [new AccountCoverage(PresentationText.Create(CorpusKnowledgeSearch.Account.Value), PresentationFreshness.CurrentAt(ObservedAt), earliestReceivedAt: null, latestReceivedAt: null)]);

        // The judge is shown the result a person would read, against the question they asked and the extracts it was
        // composed from, so it grades what the reading kept rather than a source name the reading dropped.
        var verdict = await scenarioRun.EvaluateAsync(
            [new ChatMessage(ChatRole.User, this.Question)],
            new ChatResponse(new ChatMessage(ChatRole.Assistant, Rendered(result.Blocks[0]))) { ModelId = modelName },
            [new GroundednessEvaluatorContext(string.Join("\n\n", sources.Select(static source => source.Extract)))],
            cancellationToken);

        this.HoldRatings(verdict);
        this.Check(verdict, answer.Text, sources, result);
        EvaluationCost.Record(verdict, modelName, modelSpend.Take(), judgeSpend.Take());

        return verdict;
    }

    /// <summary>Names every check and rating the verdict falls short on, in words a failed run can be read by.</summary>
    /// <param name="verdict">The verdict one model's run of this scenario produced.</param>
    /// <returns>One line per shortfall, naming the scenario and the metric.</returns>
    public IEnumerable<string> ShortfallsOf(EvaluationResult verdict) => EvaluationMetrics.ShortfallsOf(verdict);

    /// <summary>Writes the opening block as the text a person would read it as.</summary>
    private static string Rendered(PresentationBlock block) => block switch
    {
        AnswerBlock answer => answer.Text.Value,
        TimelineBlock timeline => string.Join(
            '\n',
            timeline.Entries.Select(static entry => string.Create(
                CultureInfo.InvariantCulture,
                $"{entry.OccurredAt:yyyy-MM-dd}: {entry.Summary.Value} ({entry.Subject.Value})"))),
        _ => throw new NotSupportedException($"No scenario asks a question whose result opens with {block.Type}."),
    };

    /// <summary>Every source name the model wrote anywhere in its answer: beside the answer, on a side, an event, or a cell.</summary>
    private static IEnumerable<string> NamedSources(DiscoveryResultDocument document) =>
    [
        .. document.Sources ?? [],
        .. (document.Conflict ?? []).SelectMany(static side => side?.Sources ?? []),
        .. (document.Events ?? []).SelectMany(static entry => entry?.Sources ?? []),
        .. (document.Rows ?? []).SelectMany(static row => row?.Cells ?? []).SelectMany(static cell => cell?.Sources ?? []),
    ];

    private void HoldRatings(EvaluationResult verdict)
    {
        if (this.MinimumRelevance is { } relevance)
        {
            EvaluationMetrics.HoldToThreshold(verdict, RelevanceEvaluator.RelevanceMetricName, relevance);
        }

        if (this.MinimumGroundedness is { } groundedness)
        {
            EvaluationMetrics.HoldToThreshold(verdict, GroundednessEvaluator.GroundednessMetricName, groundedness);
        }
    }

    /// <summary>Records every structural check as a metric beside the judge's.</summary>
    private void Check(
        EvaluationResult verdict,
        string? answerText,
        IReadOnlyList<DiscoveryComposedSource> sources,
        PresentationPlan result)
    {
        var document = ModelJsonAnswer.Read(answerText, DiscoveryResultJsonContext.Default.DiscoveryResultDocument);
        var offered = sources.Select(static source => source.Citation.Id.Value).ToHashSet(StringComparer.Ordinal);
        var unoffered = document is null
            ? []
            : NamedSources(document).Select(static name => name?.Trim() ?? string.Empty).Where(name => !offered.Contains(name)).Distinct().ToList();

        var opening = result.Blocks[0];
        var citedExtracts = sources
            .Where(source => opening.Evidence.Citations.Contains(source.Citation.Id))
            .Select(static source => source.Extract)
            .ToList();
        var unsupported = this.Evidence
            .Where(phrase => !citedExtracts.Any(extract => extract.Contains(phrase, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        EvaluationMetrics.Record(
            verdict,
            ReadAsAResultMetricName,
            document is not null,
            document is not null ? "The answer read as one result object." : "The answer could not be read as a result object.");
        EvaluationMetrics.Record(
            verdict,
            CitesOfferedSourcesMetricName,
            unoffered.Count is 0,
            unoffered.Count is 0 ? $"Every source named was one of the {sources.Count} offered." : $"Named sources nobody offered: {string.Join(", ", unoffered)}.");

        if (this.Evidence.Count is 0)
        {
            EvaluationMetrics.Record(
                verdict,
                RestsOnEvidenceMetricName,
                opening.Evidence.Support is PresentationSupport.Unsupported,
                opening.Evidence.Support is PresentationSupport.Unsupported
                    ? "The extracts do not answer the question, and the result says so."
                    : $"The extracts do not answer the question, yet the result rests on {string.Join(", ", opening.Evidence.Citations)}.");

            return;
        }

        EvaluationMetrics.Record(
            verdict,
            OpensAsTheIntentAsksMetricName,
            opening.Type == this.Intent.OpensWith,
            $"The result opens with {opening.Type}, and the intent asks for {this.Intent.OpensWith}.");
        EvaluationMetrics.Record(
            verdict,
            RestsOnEvidenceMetricName,
            unsupported.Count is 0,
            unsupported.Count is 0 ? "Every piece of evidence is in an extract the result cites." : $"No cited extract carries: {string.Join("; ", unsupported)}.");
    }
}
