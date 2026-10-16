// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

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

    /// <summary>Gets every case, by the name it is filed under.</summary>
    public static TheoryData<string> Cases { get; } = new(DiscoveryEndToEndScenario.All.Select(static scenario => scenario.Name));

    [Theory(Skip = AiEvaluationRun.SkipReason, SkipUnless = nameof(EvaluationsRequested))]
    [MemberData(nameof(Cases))]
    public async Task RunAsync_AQuestion_EveryDeclaredModelAnswersFromTheMessagesHoldingTheEvidence(string caseName)
    {
        // Arrange
        var scenario = DiscoveryEndToEndScenario.All.Single(candidate => candidate.Name == caseName);
        var apiKey = EvaluationEndpoint.ApiKey();
        var repetitions = EvaluationRepetitions.Declared();
        var pairings = EvaluationDeclaration.Read().PairingsFor(ChatCapability.DiscoveryPlanning, ChatCapability.DiscoveryComposition);
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        var shortfalls = await Task.WhenAll(
            [.. pairings.Select(pairing => MeasureAsync(scenario, pairing.First, pairing.Second, apiKey, repetitions, cancellationToken))]);

        // Assert
        AiEvaluationRun.AssertNoShortfalls(shortfalls.SelectMany(static modelShortfalls => modelShortfalls));
    }

    /// <summary>Runs one case under one pairing of a planning and a composing model and names what it fell short on.</summary>
    /// <remarks>A pairing of one model with itself opens one client, which is the run a deployment naming neither agent makes.</remarks>
    private static Task<IReadOnlyList<string>> MeasureAsync(
        DiscoveryEndToEndScenario scenario,
        ModelUnderTest planning,
        ModelUnderTest composition,
        string apiKey,
        int repetitions,
        CancellationToken cancellationToken)
    {
        var reporting = EvaluationStore.OpenUnjudged(DiscoveryEndToEndScenario.Evaluators);

        return EvaluationRepetitions.MeasureAsync(
            reporting,
            scenario.Name,
            DiscoveryEndToEndScenario.NameOf(planning.Plan, composition.Plan),
            repetitions,
            async repetition =>
            {
                var modelSpend = new SpendMeter();

                using var planningModel = ProviderChatClient.Open(planning, apiKey, modelSpend);
                using var separateCompositionModel = ReferenceEquals(planning, composition) ? null : ProviderChatClient.Open(composition, apiKey, modelSpend);

                return [.. scenario.ShortfallsOf(await scenario.RunAsync(
                    reporting,
                    planningModel,
                    planning.Plan,
                    separateCompositionModel ?? planningModel,
                    composition.Plan,
                    repetition,
                    modelSpend,
                    cancellationToken))];
            },
            cancellationToken);
    }
}
