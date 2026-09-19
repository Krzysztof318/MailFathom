// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.AI.Chat;
using MailFathom.AI.Discovery;
using MailFathom.AI.Orchestration;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Retrieval;
using MailFathom.Domain.Accounts;
using MailFathom.Evaluations.Costing;
using MailFathom.Evaluations.Reporting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;
using Microsoft.Extensions.AI.Evaluation.Reporting;
using Microsoft.Extensions.Logging.Abstractions;

namespace MailFathom.Evaluations.Discovery;

/// <summary>Puts one question to the Discover planning agent under one model, reads the plan, and holds it to what the question said.</summary>
/// <remarks>
/// <para>
/// The agent is composed by the composition a deployment uses, asked the turn a deployment composes, and answered through
/// the reading a deployment applies, so a plan here is the plan a Discover run would follow. What it leaves out is what
/// decides whether a derivation happens rather than what it says: the ledgers, the egress guard, and the fallback chain.
/// </para>
/// <para>
/// A plan is a structure, so almost all of it is asserted plainly: that the answer was read rather than fallen back from,
/// and whatever <see cref="DiscoveryPlanningCase.Expectation" /> states. Both land in the store as one
/// <see cref="ExpectationMetricName" /> metric, so the report shows each model's reading beside its cost. The judge is
/// asked about an ambiguous question alone, where no plan is the right one and a reading can only be reasonable.
/// </para>
/// </remarks>
internal static class DiscoveryPlanningScenario
{
    /// <summary>The name every case's scenario name begins with.</summary>
    public const string Name = "DiscoveryPlanning";

    /// <summary>The name the deterministic verdict is recorded under.</summary>
    public const string ExpectationMetricName = "Plan as expected";

    /// <summary>The lowest <c>Intent Resolution</c> score, out of five, an ambiguous question's plan passes at.</summary>
    /// <remarks>
    /// Three is the score the evaluator's rubric gives a response that resolves the intent only partly, and a plan for an
    /// ambiguous question commits to one reading, which a judge that does not know both readings are acceptable reads as
    /// resolving it only partly. It sits at the boundary between three and four: the same plan, word for word, was graded
    /// four on one run and three on the next, each time for choosing the reading the judge did not prefer. So four
    /// measured the judge's coin rather than the plan, while two is what a plan that misread or mangled the question
    /// scores — a plan the judge could not read as JSON scored two before the judge was shown the answer itself.
    /// </remarks>
    public const double IntentResolutionThreshold = 3;

    /// <summary>The scope every question is asked within: the whole of one mail account.</summary>
    /// <remarks>It reaches the model only as a count, so which account it names does not matter.</remarks>
    private static readonly MailboxScope WholeMailbox = MailboxScope.Create([MailAccountId.Create("primary")], []);

#pragma warning disable AIEVAL001 // The intent resolution evaluator is marked experimental, and is the judged assertion this suite asks for.

    /// <summary>Gets the name the judge's intent resolution score is recorded under.</summary>
    public static string IntentResolutionMetricName => IntentResolutionEvaluator.IntentResolutionMetricName;

    /// <summary>What one case is judged on: the judge on an ambiguous question, and nothing on any other.</summary>
    /// <param name="scenario">The case.</param>
    /// <returns>The evaluators.</returns>
    public static IReadOnlyList<IEvaluator> EvaluatorsFor(DiscoveryPlanningCase scenario) =>
        scenario.IsAmbiguous ? [new IntentResolutionEvaluator()] : [];
#pragma warning restore AIEVAL001

    /// <summary>Runs one case under one model and files the verdict in the run's store.</summary>
    /// <param name="reporting">The run's store, judge, and name, opened with <see cref="EvaluatorsFor" /> for this case.</param>
    /// <param name="model">The model under test's client.</param>
    /// <param name="plan">The plan the model is measured with, whose routed name is what the result is filed under.</param>
    /// <param name="scenario">The question to put to it.</param>
    /// <param name="modelSpend">What reaching that model has cost, which is the meter its client is opened over.</param>
    /// <param name="judgeSpend">What reaching the judge has cost, which is the meter the run's judge is opened over.</param>
    /// <param name="cancellationToken">Withdraws the run.</param>
    /// <returns>What the plan got wrong, if anything, and the verdict filed for it.</returns>
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Disposing the caching wrapper would dispose the caller's model client, which this scenario does not own.")]
    public static async Task<DiscoveryPlanningOutcome> RunAsync(
        ReportingConfiguration reporting,
        IChatClient model,
        ChatGenerationPlan plan,
        DiscoveryPlanningCase scenario,
        SpendMeter modelSpend,
        SpendMeter judgeSpend,
        CancellationToken cancellationToken)
    {
        var modelName = plan.Endpoint.RoutedModelName;
        var scenarioName = $"{Name}.{scenario.Name}";
        var iterationName = EvaluationStore.IterationNameFor(modelName);

        await using var scenarioRun = await reporting.CreateScenarioRunAsync(
            scenarioName,
            iterationName,
            cancellationToken: cancellationToken);

        var turn = DiscoveryPlanningInstructions.ComposePlanningTurn(
            scenario.Question,
            WholeMailbox,
            EmailKnowledgeBounds.Default);

        var cachedModel = await EvaluationStore.CacheOverAsync(
            reporting,
            model,
            plan,
            scenarioName,
            iterationName,
            cancellationToken);

        var agent = DiscoveryPlanningAgentComposition.Compose(
            cachedModel,
            plan,
            new EmptyAgentInstructionEnvelope(),
            NullLoggerFactory.Instance);

        var answer = await agent.RunAsync(turn, session: null, options: null, cancellationToken);
        var reading = DiscoveryPlanReading.Read(
            answer.Text,
            MailQuestionText.Create(scenario.Question),
            EmailKnowledgeBounds.Default);

        var shortfall = reading.WasRead
            ? scenario.Expectation(reading.Plan)
            : "the answer could not be read as a plan, so the run fell back to the question's own words.";

        // The instruction travels as the system turn so the judge grades a plan against the job of planning, rather
        // than faulting it for not answering the question it was told not to answer. The answer goes as the model wrote
        // it, because that instruction asks for one JSON object and a judge shown anything else grades the format.
        var verdict = await scenarioRun.EvaluateAsync(
            [new ChatMessage(ChatRole.System, DiscoveryPlanningInstructions.Text), new ChatMessage(ChatRole.User, turn)],
            new ChatResponse(new ChatMessage(ChatRole.Assistant, answer.Text)) { ModelId = modelName },
            cancellationToken: cancellationToken);

        RecordExpectation(verdict, shortfall);
        EvaluationCost.Record(verdict, modelName, modelSpend.Take(), judgeSpend.Take());

        return new DiscoveryPlanningOutcome(modelName, scenario, shortfall, verdict);
    }

    /// <summary>Adds the deterministic verdict to the result, before the run is written to the store.</summary>
    private static void RecordExpectation(EvaluationResult verdict, string? shortfall)
    {
        var metric = new BooleanMetric(
            ExpectationMetricName,
            shortfall is null,
            shortfall ?? "The plan was read from the answer and says what the question stated.")
        {
            Interpretation = new EvaluationMetricInterpretation(
                shortfall is null ? EvaluationRating.Exceptional : EvaluationRating.Unacceptable,
                failed: shortfall is not null,
                shortfall),
        };

        verdict.Metrics[metric.Name] = metric;
    }
}
