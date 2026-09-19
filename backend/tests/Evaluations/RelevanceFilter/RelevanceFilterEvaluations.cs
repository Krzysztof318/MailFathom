// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.AI.Chat;
using MailFathom.Evaluations.Costing;
using MailFathom.Evaluations.Providers;
using MailFathom.Evaluations.Reporting;
using Microsoft.Extensions.AI.Evaluation;
using xRetry.v3;
using Xunit;

namespace MailFathom.Evaluations.RelevanceFilter;

/// <summary>Measures the model-judged relevance filter under every declared model, against the labelled candidates.</summary>
/// <remarks>
/// <para>
/// Shaped like <see cref="Enrichment.EmailEnrichmentEvaluations" /> — one test over the whole model list, the models
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
    /// <summary>How many times the test is run before its failure is reported.</summary>
    private const int MaxAttempts = 3;

    /// <summary>How long to wait before running it again, sized for a rate limit or a momentary overload to clear.</summary>
    private const int DelayBetweenAttemptsMs = 5000;

    /// <summary>Gets whether an evaluation run was explicitly asked for.</summary>
    /// <remarks>Public and static because that is the shape xUnit reads a skip condition from.</remarks>
    public static bool EvaluationsRequested => AiEvaluationRun.Requested;

    [RetryFact(
        MaxAttempts,
        DelayBetweenAttemptsMs,
        Skip = AiEvaluationRun.SkipReason,
        SkipUnless = nameof(EvaluationsRequested))]
    public async Task FindPassagesAsync_LabelledCandidates_EveryDeclaredModelKeepsWhatAnswersAndDropsWhatDoesNot()
    {
        // Arrange
        var apiKey = EvaluationEndpoint.ApiKey();

        // Act
        var outcomes = await Task.WhenAll([.. ModelsUnderTest.Plans().Select(plan => MeasureAsync(plan, apiKey))]);

        // Assert
        Assert.Empty(outcomes.SelectMany(static outcome => ShortfallsOf(outcome.Model, outcome.Verdict)));
    }

    /// <summary>Measures one model over a client, a meter, and a store handle of its own, which is what lets the models run at once.</summary>
    private static async Task<(string Model, EvaluationResult Verdict)> MeasureAsync(ChatGenerationPlan plan, string apiKey)
    {
        var modelSpend = new SpendMeter();

        using var model = ProviderChatClient.Open(plan.Endpoint, apiKey, plan.RequestTimeout, modelSpend);

        var reporting = EvaluationStore.OpenUnjudged(RelevanceFilterScenario.Evaluators);
        var verdict = await RelevanceFilterScenario.RunAsync(
            reporting,
            model,
            plan,
            modelSpend,
            TestContext.Current.CancellationToken);

        return (plan.Endpoint.RoutedModelName, verdict);
    }

    /// <summary>Names every metric one model's measurement failed on, in words a failed run can be read by.</summary>
    private static IEnumerable<string> ShortfallsOf(string model, EvaluationResult verdict) =>
        verdict.Metrics.Values
            .OfType<NumericMetric>()
            .Where(static metric => metric.Interpretation is { Failed: true })
            .Select(metric => string.Create(
                CultureInfo.InvariantCulture,
                $"{model}: {metric.Name} was {metric.Value} — {metric.Interpretation!.Reason}"));
}
