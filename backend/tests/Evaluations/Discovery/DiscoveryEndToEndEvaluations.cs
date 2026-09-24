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
        var pairings = EvaluationDeclaration.Read().PairingsFor(ChatCapability.DiscoveryPlanning, ChatCapability.DiscoveryComposition);

        // Act
        var shortfalls = await Task.WhenAll([.. pairings.Select(pairing => MeasureAsync(pairing.First, pairing.Second, apiKey, repetitions))]);

        // Assert
        AiEvaluationRun.AssertNoShortfalls(shortfalls.SelectMany(static modelShortfalls => modelShortfalls));
    }

    /// <summary>Runs every scenario under one pairing of a planning and a composing model and names what it fell short on.</summary>
    /// <remarks>A pairing of one model with itself opens one client, which is the run a deployment naming neither agent makes.</remarks>
    private static async Task<IReadOnlyList<string>> MeasureAsync(
        ModelUnderTest planning,
        ModelUnderTest composition,
        string apiKey,
        int repetitions)
    {
        var modelSpend = new SpendMeter();

        using var planningModel = ProviderChatClient.Open(planning, apiKey, modelSpend);
        using var separateCompositionModel = ReferenceEquals(planning, composition) ? null : ProviderChatClient.Open(composition, apiKey, modelSpend);

        var compositionModel = separateCompositionModel ?? planningModel;
        var reporting = EvaluationStore.OpenUnjudged(DiscoveryEndToEndScenario.Evaluators);
        var name = DiscoveryEndToEndScenario.NameOf(planning.Plan, composition.Plan);
        List<string> shortfalls = [];

        foreach (var scenario in DiscoveryEndToEndScenario.All)
        {
            shortfalls.AddRange(await EvaluationRepetitions.MeasureAsync(
                reporting,
                scenario.Name,
                name,
                repetitions,
                async repetition => [.. scenario.ShortfallsOf(await scenario.RunAsync(
                    reporting,
                    planningModel,
                    planning.Plan,
                    compositionModel,
                    composition.Plan,
                    repetition,
                    modelSpend,
                    TestContext.Current.CancellationToken))],
                TestContext.Current.CancellationToken));
        }

        return shortfalls;
    }
}
