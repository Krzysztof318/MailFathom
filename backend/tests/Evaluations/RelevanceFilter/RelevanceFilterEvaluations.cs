// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.AI.Chat;
using MailFathom.Evaluations.Costing;
using MailFathom.Evaluations.Providers;
using MailFathom.Evaluations.Reporting;
using Microsoft.Extensions.AI.Evaluation;
using Xunit;

namespace MailFathom.Evaluations.RelevanceFilter;

/// <summary>Measures the model-judged relevance filter under every declared model, against the labelled candidates.</summary>
/// <remarks>
/// <para>
/// Shaped like <see cref="ReplyDrafts.ReplyDraftEvaluations" /> — one test over the whole model list, the models
/// measured at the same time, every shortfall collected before the test fails — with the one difference the filter's
/// shape makes: no judge is declared, read, or opened, so the run's only provider calls are the filter's own.
/// </para>
/// <para>
/// A model fails where it falls under the floor at the default threshold. What the other thresholds kept is reported
/// and fails nothing, and neither does a default that separates worse than another threshold: that is a finding to
/// write as an issue, read off the report.
/// </para>
/// </remarks>
public sealed class RelevanceFilterEvaluations
{
    /// <summary>Gets whether an evaluation run was explicitly asked for.</summary>
    /// <remarks>Public and static because that is the shape xUnit reads a skip condition from.</remarks>
    public static bool EvaluationsRequested => AiEvaluationRun.Requested;

    [Fact(Skip = AiEvaluationRun.SkipReason, SkipUnless = nameof(EvaluationsRequested))]
    public async Task FindPassagesAsync_LabelledCandidates_EveryDeclaredModelKeepsWhatAnswersAndDropsWhatDoesNot()
    {
        // Arrange
        var apiKey = EvaluationEndpoint.ApiKey();
        var repetitions = EvaluationRepetitions.Declared();

        // Act
        var shortfalls = await Task.WhenAll([.. ModelsUnderTest.Plans().Select(plan => MeasureAsync(plan, apiKey, repetitions))]);

        // Assert
        AiEvaluationRun.AssertNoShortfalls(shortfalls.SelectMany(static modelShortfalls => modelShortfalls));
    }

    /// <summary>Measures one model over a client, a meter, and a store handle of its own, which is what lets the models run at once.</summary>
    private static async Task<IReadOnlyList<string>> MeasureAsync(ChatGenerationPlan plan, string apiKey, int repetitions)
    {
        var modelSpend = new SpendMeter();

        using var model = ProviderChatClient.Open(plan.Endpoint, apiKey, plan.RequestTimeout, modelSpend);

        var reporting = EvaluationStore.OpenUnjudged(RelevanceFilterScenario.Evaluators);

        return await EvaluationRepetitions.MeasureAsync(
            reporting,
            RelevanceFilterScenario.Name,
            plan.Endpoint.RoutedModelName,
            repetitions,
            async repetition => [.. ShortfallsOf(await RelevanceFilterScenario.RunAsync(
                reporting,
                model,
                plan,
                repetition,
                modelSpend,
                TestContext.Current.CancellationToken))],
            TestContext.Current.CancellationToken);
    }

    /// <summary>Names every metric one measurement failed on, in words a failed run can be read by.</summary>
    private static IEnumerable<string> ShortfallsOf(EvaluationResult verdict) =>
        verdict.Metrics.Values
            .OfType<NumericMetric>()
            .Where(static metric => metric.Interpretation is { Failed: true })
            .Select(metric => string.Create(
                CultureInfo.InvariantCulture,
                $"{metric.Name} was {metric.Value} — {metric.Interpretation!.Reason}"));
}
