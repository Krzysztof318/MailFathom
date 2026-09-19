// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using MailFathom.Evaluations.Costing;
using MailFathom.Evaluations.Providers;
using MailFathom.Evaluations.Reporting;
using xRetry.v3;
using Xunit;

namespace MailFathom.Evaluations.Discovery;

/// <summary>Has every declared model run every question in <see cref="DiscoveryEndToEndScenario.All" /> through a whole Discover run.</summary>
/// <remarks>
/// Shaped like the composition evaluation for the reasons it gives, with the one difference the relevance filter's makes:
/// nothing here is judged, so no judge is declared, read, or opened, and the run's only provider calls are the two each
/// question makes of the model under test.
/// </remarks>
public sealed class DiscoveryEndToEndEvaluations
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
    public async Task RunAsync_EveryScenario_EveryDeclaredModelAnswersFromTheMessagesHoldingTheEvidence()
    {
        // Arrange
        var apiKey = EvaluationEndpoint.ApiKey();

        // Act
        var shortfalls = await Task.WhenAll([.. ModelsUnderTest.Plans().Select(plan => MeasureAsync(plan, apiKey))]);

        // Assert
        AiEvaluationRun.AssertNoShortfalls(shortfalls.SelectMany(static modelShortfalls => modelShortfalls));
    }

    /// <summary>Runs every scenario under one model and names what it fell short on.</summary>
    private static async Task<IReadOnlyList<string>> MeasureAsync(ChatGenerationPlan plan, string apiKey)
    {
        var modelSpend = new SpendMeter();

        using var model = ProviderChatClient.Open(plan.Endpoint, apiKey, plan.RequestTimeout, modelSpend);

        var reporting = EvaluationStore.OpenUnjudged(DiscoveryEndToEndScenario.Evaluators);
        List<string> shortfalls = [];

        foreach (var scenario in DiscoveryEndToEndScenario.All)
        {
            var verdict = await scenario.RunAsync(reporting, model, plan, modelSpend, TestContext.Current.CancellationToken);
            var scenarioShortfalls = scenario.ShortfallsOf(verdict).ToList();

            if (scenarioShortfalls.Count > 0)
            {
                await EvaluationStore.ForgetAsync(reporting, scenario.Name, plan.Endpoint.RoutedModelName, TestContext.Current.CancellationToken);
            }

            shortfalls.AddRange(scenarioShortfalls.Select(shortfall => $"{plan.Endpoint.RoutedModelName}: {shortfall}"));
        }

        return shortfalls;
    }
}
