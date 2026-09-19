// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.AI.Chat;
using MailFathom.Evaluations.Costing;
using MailFathom.Evaluations.Judging;
using MailFathom.Evaluations.Providers;
using MailFathom.Evaluations.Reporting;
using Microsoft.Extensions.AI.Evaluation;
using xRetry.v3;
using Xunit;

namespace MailFathom.Evaluations.Discovery;

/// <summary>Measures what the Discover planning agent reads each question into, under every declared model.</summary>
/// <remarks>
/// <para>
/// A theory over the cases rather than over the models, because the cases are written here while the models are read
/// from the run's declaration, which a theory's data would read before the skip condition. Within a case the models are
/// measured at the same time, each over clients, meters, and a store handle of its own, as the enrichment scenario does.
/// </para>
/// <para>
/// A model whose plan falls short is collected rather than failing the case, so one weak model never hides what the
/// others read; the case fails at the end, naming every model that fell short and why.
/// </para>
/// </remarks>
public sealed class DiscoveryPlanningEvaluations
{
    /// <summary>How many times a case is run before its failure is reported.</summary>
    private const int MaxAttempts = 3;

    /// <summary>How long to wait before running it again, sized for a rate limit or a momentary overload to clear.</summary>
    private const int DelayBetweenAttemptsMs = 5000;

    /// <summary>Gets whether an evaluation run was explicitly asked for.</summary>
    /// <remarks>Public and static because that is the shape xUnit reads a skip condition from.</remarks>
    public static bool EvaluationsRequested => AiEvaluationRun.Requested;

    /// <summary>Gets every case, by the name it is filed under.</summary>
    public static TheoryData<string> Cases { get; } = new(DiscoveryPlanningCase.All.Select(static scenario => scenario.Name));

    [RetryTheory(
        MaxAttempts,
        DelayBetweenAttemptsMs,
        Skip = AiEvaluationRun.SkipReason,
        SkipUnless = nameof(EvaluationsRequested))]
    [MemberData(nameof(Cases))]
    public async Task DerivePlan_AQuestion_EveryDeclaredModelReadsItIntoThePlanItStated(string caseName)
    {
        // Arrange
        var scenario = DiscoveryPlanningCase.Named(caseName);
        var judge = JudgeDeclaration.Read();
        var apiKey = EvaluationEndpoint.ApiKey();

        // Act
        var outcomes = await Task.WhenAll(
            [.. ModelsUnderTest.Plans().Select(plan => MeasureAsync(judge, plan, apiKey, scenario))]);

        // Assert
        AiEvaluationRun.AssertNoShortfalls(outcomes.SelectMany(ShortfallsOf));
    }

    /// <summary>Measures one model over clients, meters, and a store handle of its own, which is what lets the models run at once.</summary>
    private static async Task<DiscoveryPlanningOutcome> MeasureAsync(
        JudgeDeclaration judge,
        ChatGenerationPlan plan,
        string apiKey,
        DiscoveryPlanningCase scenario)
    {
        var modelSpend = new SpendMeter();
        var judgeSpend = new SpendMeter();

        using var model = ProviderChatClient.Open(plan.Endpoint, apiKey, plan.RequestTimeout, modelSpend);
        using var judgeClient = judge.Open(judgeSpend);

        var reporting = EvaluationStore.Open(judgeClient, judge.CachingKey, DiscoveryPlanningScenario.EvaluatorsFor(scenario));

        return await DiscoveryPlanningScenario.RunAsync(
            reporting,
            model,
            plan,
            scenario,
            modelSpend,
            judgeSpend,
            TestContext.Current.CancellationToken);
    }

    /// <summary>Names what one model's plan falls short on, in words a failed run can be read by.</summary>
    private static IEnumerable<string> ShortfallsOf(DiscoveryPlanningOutcome outcome)
    {
        if (outcome.Shortfall is { } shortfall)
        {
            yield return $"{outcome.Model}: {shortfall}";
        }

        if (!outcome.Case.IsAmbiguous)
        {
            yield break;
        }

        var resolution = outcome.Verdict.Get<NumericMetric>(DiscoveryPlanningScenario.IntentResolutionMetricName);

        if (resolution.Value is not >= DiscoveryPlanningScenario.IntentResolutionThreshold)
        {
            yield return $"{outcome.Model}: intent resolution {resolution.Value?.ToString("0.#", CultureInfo.InvariantCulture) ?? "was not rated"}, below {DiscoveryPlanningScenario.IntentResolutionThreshold.ToString(CultureInfo.InvariantCulture)} — {resolution.Reason}";
        }
    }
}
