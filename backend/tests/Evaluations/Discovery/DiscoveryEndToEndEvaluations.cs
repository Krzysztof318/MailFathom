// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using MailFathom.Evaluations.Costing;
using MailFathom.Evaluations.Providers;
using MailFathom.Evaluations.Reporting;
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
    /// <summary>Gets whether an evaluation run was explicitly asked for.</summary>
    /// <remarks>Public and static because that is the shape xUnit reads a skip condition from.</remarks>
    public static bool EvaluationsRequested => AiEvaluationRun.Requested;

    [Fact(Skip = AiEvaluationRun.SkipReason, SkipUnless = nameof(EvaluationsRequested))]
    public async Task RunAsync_EveryScenario_EveryDeclaredModelAnswersFromTheMessagesHoldingTheEvidence()
    {
        // Arrange
        var apiKey = EvaluationEndpoint.ApiKey();
        var repetitions = EvaluationRepetitions.Declared();

        // Act
        var shortfalls = await Task.WhenAll([.. ModelsUnderTest.Plans().Select(plan => MeasureAsync(plan, apiKey, repetitions))]);

        // Assert
        AiEvaluationRun.AssertNoShortfalls(shortfalls.SelectMany(static modelShortfalls => modelShortfalls));
    }

    /// <summary>Runs every scenario under one model and names what it fell short on.</summary>
    private static async Task<IReadOnlyList<string>> MeasureAsync(ChatGenerationPlan plan, string apiKey, int repetitions)
    {
        var modelSpend = new SpendMeter();

        using var model = ProviderChatClient.Open(plan.Endpoint, apiKey, plan.RequestTimeout, modelSpend);

        var reporting = EvaluationStore.OpenUnjudged(DiscoveryEndToEndScenario.Evaluators);
        List<string> shortfalls = [];

        foreach (var scenario in DiscoveryEndToEndScenario.All)
        {
            shortfalls.AddRange(await EvaluationRepetitions.MeasureAsync(
                reporting,
                scenario.Name,
                plan.Endpoint.RoutedModelName,
                repetitions,
                async repetition => [.. scenario.ShortfallsOf(await scenario.RunAsync(
                    reporting,
                    model,
                    plan,
                    repetition,
                    modelSpend,
                    TestContext.Current.CancellationToken))],
                TestContext.Current.CancellationToken));
        }

        return shortfalls;
    }
}
