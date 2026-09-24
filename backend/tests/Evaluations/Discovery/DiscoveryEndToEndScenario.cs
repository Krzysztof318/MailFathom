// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.AI.Chat;
using MailFathom.AI.Discovery;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Discovery.Runs;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Retrieval;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.Synchronization.Administration;
using MailFathom.Application.Synchronization.Checkpoints;
using MailFathom.Domain.Access;
using MailFathom.Evaluations.Answering;
using MailFathom.Evaluations.Corpus;
using MailFathom.Evaluations.Costing;
using MailFathom.Evaluations.Reporting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Reporting;

namespace MailFathom.Evaluations.Discovery;

/// <summary>One question put to a whole Discover run over the synthetic corpus, and what the result a person receives must rest on.</summary>
/// <remarks>
/// <para>
/// The planning agent reads the question into a plan, the plan's lookups run over the corpus through the retrieval a
/// deployment runs them through, and the composing agent writes the result from what came back — joined by
/// <see cref="DiscoveryRun" /> itself, with both agents on one model. So a plan that reads well and misses the message
/// holding the answer fails here even though it passes <see cref="DiscoveryPlanningScenario" />, and a composition that is
/// faithful to the wrong extracts fails here even though it passes <see cref="DiscoveryCompositionScenario" />. What the
/// run leaves out is what decides whether it happens at all: the grant, the capability, the ledgers, the egress guard,
/// and the fallback chain.
/// </para>
/// <para>
/// Every check is a structure and is asserted plainly, with no judge: that the plan was read, and that the result cites
/// an extract carrying each piece of evidence — or, for a question the corpus does not answer, that it was composed as
/// unanswered. Where the evidence is missing, the check names the stage that lost it, so a fix goes to the plan, the
/// retrieval, or the composition rather than to whichever was changed last.
/// </para>
/// </remarks>
/// <param name="Name">The name the scenario is filed and reported under.</param>
/// <param name="Question">The question, as the owner of the mailbox would ask it.</param>
/// <param name="Evidence">
/// Phrases the result must rest on, each carried by the extract of some source its opening block cites; empty for a
/// question the corpus does not answer, where the result must be composed as unanswered.
/// </param>
internal sealed record DiscoveryEndToEndScenario(string Name, string Question, IReadOnlyList<string> Evidence)
{
    /// <summary>The check that the planning answer was read as a plan rather than falling back to the question's own words.</summary>
    public const string PlanReadMetricName = "Plan was read";

    /// <summary>The check that the result cites the evidence, or is composed as unanswered where there is none.</summary>
    public const string CitesEvidenceMetricName = "Cites the evidence";

    /// <summary>When the corpus was last synchronized, fixed so a result's freshness is the same on every run.</summary>
    private static readonly DateTimeOffset SynchronizedAt = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Gets every question a run is measured on.</summary>
    public static IReadOnlyList<DiscoveryEndToEndScenario> All { get; } =
    [
        new(
            "DiscoveryEndToEnd.OneMessageAnswers",
            "What error message did LumenDesk show when the filtered project export failed?",
            ["permitted buffer size"]),
        new(
            "DiscoveryEndToEnd.SeveralMessagesAnswer",
            "Which LumenDesk build fixed the export failure, and which Atlas Importer build fixes the tide-reading import problem?",
            ["4.8.3", "2.8.4"]),
        new(
            "DiscoveryEndToEnd.TracksAChange",
            "How did the Atlas Importer tide-reading import problem get fixed over time?",
            ["2.8.4"]),
        new(
            "DiscoveryEndToEnd.KeywordMatchesTheWrongMessage",
            "What corrected billing address did Lumenfield Supplies' invoice INV-4827 need?",
            ["42 Lantern Way"]),
        new(
            "DiscoveryEndToEnd.AnotherInvoiceNeedsAnotherAddress",
            "What bill-to address should invoice 7842 from Northstar Ledgerworks show?",
            ["17 Willowmere Lane"]),
        new(
            "DiscoveryEndToEnd.NamesAPerson",
            "Who did Halina Pettersen book to pick us up at Solmere Airport, and when?",
            ["Silverline Cars"]),
        new(
            "DiscoveryEndToEnd.NamesADateRange",
            "Which trip itinerary was confirmed to me in the first week of September 2026?",
            ["Lydmere"]),
        new(
            "DiscoveryEndToEnd.QuotesAMessageWrittenInMarkup",
            "What message did the browser show when the QuillDesk CSV export of a filtered project failed?",
            ["timed out while preparing the task set"]),
        new(
            "DiscoveryEndToEnd.NamesTheFix",
            "What did the support team deploy to stop the SurveyDesk confirmation panel spinning after a submission?",
            ["queue-handling patch"]),
        new(
            "DiscoveryEndToEnd.NamesAPlace",
            "Which room is the Northglass Works berth inspection pilot meeting in?",
            ["Harbour Room 2"]),
        new(
            "DiscoveryEndToEnd.NamesAHotel",
            "Which hotel am I staying at on the Norvale trip?",
            ["Harbor Lantern Hotel"]),
        new(
            "DiscoveryEndToEnd.NothingAnswers",
            "What did my dentist say about moving my check-up?",
            []),
        new(
            "DiscoveryEndToEnd.NeighbouringMailDoesNotAnswer",
            "What cancellation fee does the hotel on the Solmere trip charge?",
            []),
    ];

    /// <summary>Gets what every scenario is measured on beside its own checks: nothing a model is asked for.</summary>
    public static IReadOnlyList<IEvaluator> Evaluators => [];

    /// <summary>Runs the question through both agents and the retrieval between them, checks the result, and files the verdict in the run's store.</summary>
    /// <param name="reporting">The run's store and name, opened with <see cref="Evaluators" />.</param>
    /// <param name="planningModel">The client the planning agent is asked through.</param>
    /// <param name="planningPlan">The plan the planning agent's model is measured with.</param>
    /// <param name="compositionModel">The client the composing agent is asked through, which is the planning one where both run on one model.</param>
    /// <param name="compositionPlan">The plan the composing agent's model is measured with.</param>
    /// <param name="repetition">Which repetition of the case this is, counted from one, which the result and the cached answer are filed under.</param>
    /// <param name="modelSpend">What reaching those models has cost, which is the meter their clients are opened over.</param>
    /// <param name="cancellationToken">Withdraws the run.</param>
    /// <returns>The verdict, carrying every check as a metric.</returns>
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Disposing the caching wrapper would dispose the caller's model client, which this scenario does not own.")]
    public async Task<EvaluationResult> RunAsync(
        ReportingConfiguration reporting,
        IChatClient planningModel,
        ChatGenerationPlan planningPlan,
        IChatClient compositionModel,
        ChatGenerationPlan compositionPlan,
        int repetition,
        SpendMeter modelSpend,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(planningPlan);
        ArgumentNullException.ThrowIfNull(compositionPlan);

        var modelName = NameOf(planningPlan, compositionPlan);
        var iterationName = EvaluationStore.IterationNameFor(modelName, repetition);

        await using var scenarioRun = await reporting.CreateScenarioRunAsync(
            this.Name,
            iterationName,
            cancellationToken: cancellationToken);

        var agents = new DiscoveryAgentsUnderTest(
            await EvaluationStore.CacheOverAsync(reporting, planningModel, planningPlan, this.Name, iterationName, cancellationToken),
            planningPlan,
            await EvaluationStore.CacheOverAsync(reporting, compositionModel, compositionPlan, this.Name, iterationName, cancellationToken),
            compositionPlan);
        var search = new CorpusKnowledgeSearch(CorpusMessage.All);

        var run = await DiscoveryRun.AnswerAsync(
            new MailQuestion(MailQuestionText.Create(this.Question), CorpusKnowledgeSearch.Scope, new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.FromHours(2))),
            agents,
            new PlannedMailRetrieval(search, new MailAnsweringRunLedger(MailAnsweringRunBounds.Default)),
            new DiscoveryCoverageReader(new SynchronizedCorpus(), new MailSynchronizationRunLedger(TimeProvider.System)),
            agents,
            UserLanguage.English,
            progress: null,
            cancellationToken);

        var verdict = await scenarioRun.EvaluateAsync(
            [new ChatMessage(ChatRole.User, this.Question)],
            new ChatResponse(new ChatMessage(ChatRole.Assistant, agents.Composition ?? string.Empty)) { ModelId = modelName },
            cancellationToken: cancellationToken);

        EvaluationMetrics.Record(
            verdict,
            PlanReadMetricName,
            agents.PlanWasRead is true,
            agents.PlanWasRead is true
                ? $"The plan was read: {Quoted(run.Plan.Retrieval.Lookups)}."
                : "The answer could not be read as a plan, so the run fell back to the question's own words.");

        await this.CheckEvidenceAsync(verdict, run, search, cancellationToken);
        EvaluationCost.Record(verdict, modelName, modelSpend.Take(), judgeSpend: default);

        return verdict;
    }

    /// <summary>Names the pairing a result is filed under: the one model where both agents ran on it, and both joined by a plus where they did not.</summary>
    /// <param name="planningPlan">The plan the planning agent's model is measured with.</param>
    /// <param name="compositionPlan">The plan the composing agent's model is measured with.</param>
    /// <returns>The name, which is the model's own where both agents ran on one, so a run naming no agent keeps the results it always filed.</returns>
    public static string NameOf(ChatGenerationPlan planningPlan, ChatGenerationPlan compositionPlan)
    {
        ArgumentNullException.ThrowIfNull(planningPlan);
        ArgumentNullException.ThrowIfNull(compositionPlan);

        return planningPlan.Endpoint.Alias == compositionPlan.Endpoint.Alias
            ? planningPlan.Endpoint.Alias
            : $"{planningPlan.Endpoint.Alias}+{compositionPlan.Endpoint.Alias}";
    }

    /// <summary>Names every check the verdict falls short on, in words a failed run can be read by.</summary>
    /// <param name="verdict">The verdict one model's run of this scenario produced.</param>
    /// <returns>One line per shortfall, naming the metric; the repetition header above them names the case and the model.</returns>
    public IEnumerable<string> ShortfallsOf(EvaluationResult verdict) => EvaluationMetrics.ShortfallsOf(verdict);

    private static string Quoted(IEnumerable<EmailKnowledgeQuery> lookups) =>
        string.Join(", ", lookups.Select(static lookup => $"\"{lookup.QueryText}\""));

    private static bool Carries(string extract, string phrase) => extract.Contains(phrase, StringComparison.OrdinalIgnoreCase);

    /// <summary>Records whether the result rests on the evidence, naming for each missing piece the stage that lost it.</summary>
    private async Task CheckEvidenceAsync(
        EvaluationResult verdict,
        DiscoveryRunResult run,
        CorpusKnowledgeSearch search,
        CancellationToken cancellationToken)
    {
        // Declared again rather than read back from the composition, because the declaration is a function of what the
        // retrieval returned and is what the composing agent was handed.
        var handed = DiscoveryComposedSources.Declare(run.Evidence.Passages);
        var opening = run.Presentation.Blocks[0];

        if (this.Evidence.Count is 0)
        {
            var unanswered = opening.Evidence.Support is PresentationSupport.Unsupported;

            EvaluationMetrics.Record(
                verdict,
                CitesEvidenceMetricName,
                unanswered,
                unanswered
                    ? "The corpus does not answer the question, and the result says so."
                    : $"The corpus does not answer the question, yet the result rests on {string.Join(", ", opening.Evidence.Citations)}.");

            return;
        }

        var cited = handed.Where(source => opening.Evidence.Citations.Contains(source.Citation.Id)).ToList();
        List<string> lost = [];

        foreach (var phrase in this.Evidence.Where(phrase => !cited.Any(source => Carries(source.Extract, phrase))))
        {
            lost.Add(await LostAtAsync(phrase));
        }

        EvaluationMetrics.Record(
            verdict,
            CitesEvidenceMetricName,
            lost.Count is 0,
            lost.Count is 0 ? "Every piece of evidence is in an extract the result cites." : string.Join(" ", lost));

        // Walked backwards from the result: an extract the composition was handed and did not cite is the composition's,
        // one the run retrieved and did not hand on is the declaration's, one a lookup of the plan reaches on its own
        // and the run did not retrieve is the retrieval's — it stopped early or ran out of its allowance — and one no
        // lookup reaches is the plan's.
        async Task<string> LostAtAsync(string phrase)
        {
            if (handed.FirstOrDefault(source => Carries(source.Extract, phrase)) is { } uncited)
            {
                return $"\"{phrase}\" was lost by the composition: it was handed {uncited.Citation.Id} and did not cite it.";
            }

            if (run.Evidence.Passages.Any(passage => Carries(passage.Text, phrase)))
            {
                return $"\"{phrase}\" was lost by the declaration: the run retrieved it from a message past the {PresentationEvidence.MaxCitations} a result may cite.";
            }

            foreach (var lookup in run.Plan.Retrieval.Lookups)
            {
                var reached = await search.FindPassagesAsync(CorpusKnowledgeSearch.Scope, lookup, cancellationToken);

                if (reached.Passages.Any(passage => Carries(passage.Text, phrase)))
                {
                    return $"\"{phrase}\" was lost by the retrieval: the lookup \"{lookup.QueryText}\" reaches it, and the run did not hand it to the composition.";
                }
            }

            return $"\"{phrase}\" was lost by the plan: no lookup reaches it — {Quoted(run.Plan.Retrieval.Lookups)}.";
        }
    }

    /// <summary>Reports the corpus's one folder as synchronized, which is what a run's coverage reads.</summary>
    private sealed class SynchronizedCorpus : ISynchronizationFreshnessReader
    {
        public Task<IReadOnlyList<MailboxFolderFreshness>> ReadAsync(MailboxScope scope, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MailboxFolderFreshness>>(
                [new MailboxFolderFreshness(CorpusKnowledgeSearch.Account, CorpusKnowledgeSearch.Inbox, SynchronizedAt)]);
    }
}
