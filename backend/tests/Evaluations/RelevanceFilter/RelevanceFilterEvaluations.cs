// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

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
/// One test over the whole model list, the models and each model's repetitions measured at the same time and every
/// shortfall collected before the test fails, as <see cref="AgentConversations.AgentConversationEvaluations" /> does for
/// each of its questions. No judge is declared, read, or opened, so the run's only provider calls are the filter's own.
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
        var shortfalls = await Task.WhenAll([.. ModelsUnderTest.For(ChatCapability.RelevanceFilter).Select(model => MeasureAsync(model, apiKey, repetitions))]);

        // Assert
        AiEvaluationRun.AssertNoShortfalls(shortfalls.SelectMany(static modelShortfalls => modelShortfalls));
    }

    /// <summary>Measures one model, each repetition over a client and a meter of its own, which is what lets the models and the repetitions run at once.</summary>
    private static Task<IReadOnlyList<string>> MeasureAsync(ModelUnderTest modelUnderTest, string apiKey, int repetitions)
    {
        var reporting = EvaluationStore.OpenUnjudged(RelevanceFilterScenario.Evaluators);

        return EvaluationRepetitions.MeasureAsync(
            reporting,
            RelevanceFilterScenario.Name,
            modelUnderTest.Name,
            repetitions,
            async repetition =>
            {
                var modelSpend = new SpendMeter();

                using var model = ProviderChatClient.Open(modelUnderTest, apiKey, modelSpend);

                return [.. ShortfallsOf(await RelevanceFilterScenario.RunAsync(
                    reporting,
                    model,
                    modelUnderTest.Plan,
                    repetition,
                    modelSpend,
                    TestContext.Current.CancellationToken))];
            },
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
