// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using MailFathom.AI.Chat;
using MailFathom.AI.Orchestration;
using MailFathom.Evaluations.Costing;
using MailFathom.Evaluations.Judging;
using MailFathom.Evaluations.Providers;
using MailFathom.Evaluations.Reporting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;
using Microsoft.Extensions.AI.Evaluation.Reporting;

namespace MailFathom.Evaluations.StructuredAnswers;

/// <summary>Puts one case to an agent that answers with a structure, under one model, and holds the answer to the case.</summary>
/// <remarks>
/// <para>
/// Every agent measured this way makes one call with no tools, so what differs between them is the turn, the composition,
/// and the reading, and <see cref="StructuredAnswerRequest" /> carries exactly those three. The agent is composed by the
/// composition a deployment uses and its answer read through the reading a deployment applies, so what is measured is
/// what a deployment would have kept. What is left out is what decides whether the call happens at all rather than what
/// it answers: the ledgers, the egress guard, and the fallback chain.
/// </para>
/// <para>
/// A structure is asserted plainly: the reading's verdict lands in the store as one boolean metric, so the report shows
/// each model's result beside its cost. The judge is asked only about a case that reads two ways, where no answer is the
/// right one and a reading can only be reasonable.
/// </para>
/// </remarks>
internal static class StructuredAnswerScenario
{
    /// <summary>The lowest <c>Intent Resolution</c> score, out of five, an answer to a case that reads two ways passes at.</summary>
    /// <remarks>
    /// Three is the score the evaluator's rubric gives a response that resolves the intent only partly, and an answer to a
    /// case that reads two ways commits to one reading, which a judge that does not know both readings are acceptable
    /// reads as resolving it only partly. It sits at the boundary between three and four: the same Discover plan, word for
    /// word, was graded four on one run and three on the next, each time for choosing the reading the judge did not
    /// prefer. So four measured the judge's coin rather than the answer, while two is what an answer that misread or
    /// mangled the case scores — a plan the judge could not read as JSON scored two before the judge was shown the answer
    /// itself.
    /// </remarks>
    public const double IntentResolutionThreshold = 3;

    /// <summary>Gets the name the judge's intent resolution score is recorded under.</summary>
    public static string IntentResolutionMetricName => IntentResolutionEvaluator.IntentResolutionMetricName;

    /// <summary>What a case is judged on: intent resolution where it reads two ways, and nothing where it does not.</summary>
    /// <param name="readsTwoWays">Whether the case reads two ways, so that no single answer is the right one.</param>
    /// <returns>The evaluators.</returns>
    public static IReadOnlyList<IEvaluator> JudgedWhen(bool readsTwoWays) =>
        readsTwoWays ? [new IntentResolutionEvaluator()] : [];

    /// <summary>Whether an answer holds one JSON object where a deployment's reading looks for it.</summary>
    /// <param name="answer">What the model wrote.</param>
    /// <returns>Whether the object is there and parses.</returns>
    /// <remarks>
    /// A reading that answers an unreadable answer with an empty result cannot tell it from an answer that correctly said
    /// nothing, so a case whose right answer is nothing asks this first — otherwise prose would pass it.
    /// </remarks>
    public static bool IsReadableObject(string answer)
    {
        if (AgentJsonAnswer.Unfenced(answer) is not { } json)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);

            return document.RootElement.ValueKind is JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Measures one case under every declared model at once, each over clients, meters, and a store handle of its own.</summary>
    /// <param name="request">The case.</param>
    /// <param name="cancellationToken">Withdraws the run.</param>
    /// <returns>What each model fell short on, in the order the models were declared.</returns>
    public static async Task<IReadOnlyList<string>> MeasureEveryDeclaredModelAsync(
        StructuredAnswerRequest request,
        CancellationToken cancellationToken)
    {
        var judge = JudgeDeclaration.Read();
        var apiKey = EvaluationEndpoint.ApiKey();
        var repetitions = EvaluationRepetitions.Declared();

        var shortfalls = await Task.WhenAll(
            [.. ModelsUnderTest.Plans().Select(plan => MeasureAsync(judge, plan, apiKey, repetitions, request, cancellationToken))]);

        return [.. shortfalls.SelectMany(static modelShortfalls => modelShortfalls)];
    }

    /// <summary>Names what one model's answer falls short on, in words a failed run can be read by.</summary>
    /// <param name="answer">The model's answer.</param>
    /// <returns>Every shortfall, which is none where the answer holds.</returns>
    /// <remarks>A model that falls short is collected rather than failing the case, so one weak model never hides what the others answered.</remarks>
    public static IEnumerable<string> ShortfallsOf(StructuredAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);

        if (answer.Shortfall is { } shortfall)
        {
            yield return $"{answer.Model}: {shortfall}";
        }

        if (!answer.Verdict.Metrics.ContainsKey(IntentResolutionMetricName))
        {
            yield break;
        }

        var resolution = answer.Verdict.Get<NumericMetric>(IntentResolutionMetricName);

        if (resolution.Value is not >= IntentResolutionThreshold)
        {
            yield return $"{answer.Model}: intent resolution {resolution.Value?.ToString("0.#", CultureInfo.InvariantCulture) ?? "was not rated"}, below {IntentResolutionThreshold.ToString(CultureInfo.InvariantCulture)} — {resolution.Reason}";
        }
    }

    /// <summary>Runs one case under one model and files the verdict in the run's store.</summary>
    /// <param name="reporting">The run's store, judge, and name, opened with the request's evaluators.</param>
    /// <param name="model">The model under test's client.</param>
    /// <param name="plan">The plan the model is measured with, whose routed name is what the result is filed under.</param>
    /// <param name="repetition">Which repetition of the case this is, counted from one, which the result and the cached answer are filed under.</param>
    /// <param name="request">The case.</param>
    /// <param name="modelSpend">What reaching that model has cost, which is the meter its client is opened over.</param>
    /// <param name="judgeSpend">What reaching the judge has cost, which is the meter the run's judge is opened over.</param>
    /// <param name="cancellationToken">Withdraws the run.</param>
    /// <returns>What the answer got wrong, if anything, and the verdict filed for it.</returns>
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Disposing the caching wrapper would dispose the caller's model client, which this scenario does not own.")]
    public static async Task<StructuredAnswer> RunAsync(
        ReportingConfiguration reporting,
        IChatClient model,
        ChatGenerationPlan plan,
        int repetition,
        StructuredAnswerRequest request,
        SpendMeter modelSpend,
        SpendMeter judgeSpend,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reporting);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(request);

        var modelName = plan.Endpoint.RoutedModelName;
        var iterationName = EvaluationStore.IterationNameFor(modelName, repetition);

        await using var scenarioRun = await reporting.CreateScenarioRunAsync(
            request.ScenarioName,
            iterationName,
            cancellationToken: cancellationToken);

        var cachedModel = await EvaluationStore.CacheOverAsync(
            reporting,
            model,
            plan,
            request.ScenarioName,
            iterationName,
            cancellationToken);

        var agent = request.Compose(cachedModel, plan);
        var answer = await agent.RunAsync(request.Turn, session: null, options: null, cancellationToken);
        var shortfall = request.Shortfall(answer.Text);

        // The instruction travels as the system turn so the judge grades an answer against the job the agent was given,
        // rather than faulting it for not doing what it was told not to. The answer goes as the model wrote it, because
        // every instruction here asks for one JSON object and a judge shown anything else grades the format.
        var verdict = await scenarioRun.EvaluateAsync(
            [new ChatMessage(ChatRole.System, request.Instruction), new ChatMessage(ChatRole.User, request.Turn)],
            new ChatResponse(new ChatMessage(ChatRole.Assistant, answer.Text)) { ModelId = modelName },
            cancellationToken: cancellationToken);

        RecordExpectation(verdict, request.ExpectationMetricName, shortfall);
        EvaluationCost.Record(verdict, modelName, modelSpend.Take(), judgeSpend.Take());

        return new StructuredAnswer(modelName, shortfall, verdict);
    }

    /// <summary>Measures one model over clients, meters, and a store handle of its own, which is what lets the models run at once.</summary>
    private static async Task<IReadOnlyList<string>> MeasureAsync(
        JudgeDeclaration judge,
        ChatGenerationPlan plan,
        string apiKey,
        int repetitions,
        StructuredAnswerRequest request,
        CancellationToken cancellationToken)
    {
        var modelSpend = new SpendMeter();
        var judgeSpend = new SpendMeter();

        using var model = ProviderChatClient.Open(plan.Endpoint, apiKey, plan.RequestTimeout, modelSpend);
        using var judgeClient = judge.Open(judgeSpend);

        var reporting = EvaluationStore.Open(judgeClient, judge.CachingKey, request.Evaluators);

        return await EvaluationRepetitions.MeasureAsync(
            reporting,
            request.ScenarioName,
            plan.Endpoint.RoutedModelName,
            repetitions,
            async repetition => [.. ShortfallsOf(await RunAsync(reporting, model, plan, repetition, request, modelSpend, judgeSpend, cancellationToken))],
            cancellationToken);
    }

    /// <summary>Adds the deterministic verdict to the result, before the run is written to the store.</summary>
    private static void RecordExpectation(EvaluationResult verdict, string metricName, string? shortfall)
    {
        var metric = new BooleanMetric(
            metricName,
            shortfall is null,
            shortfall ?? "The answer was read the way a deployment reads it and says what the case stated.")
        {
            Interpretation = new EvaluationMetricInterpretation(
                shortfall is null ? EvaluationRating.Exceptional : EvaluationRating.Unacceptable,
                failed: shortfall is not null,
                shortfall),
        };

        verdict.Metrics[metric.Name] = metric;
    }
}
