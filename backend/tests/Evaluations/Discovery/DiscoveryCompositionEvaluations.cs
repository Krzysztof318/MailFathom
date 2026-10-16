// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.Chat;
using MailFathom.Evaluations.Costing;
using MailFathom.Evaluations.Judging;
using MailFathom.Evaluations.Providers;
using MailFathom.Evaluations.Reporting;
using Xunit;

namespace MailFathom.Evaluations.Discovery;

/// <summary>Has every declared model compose every question in <see cref="DiscoveryCompositionScenario.All" /> from its extracts.</summary>
/// <remarks>
/// Shaped like <see cref="AgentConversations.AgentConversationEvaluations" /> for the reasons it gives, with one
/// difference: a question the extracts do not answer is judged on nothing, so each case opens its store handle with its
/// own evaluators.
/// </remarks>
public sealed class DiscoveryCompositionEvaluations
{
    /// <summary>Gets whether an evaluation run was explicitly asked for.</summary>
    /// <remarks>Public and static because that is the shape xUnit reads a skip condition from.</remarks>
    public static bool EvaluationsRequested => AiEvaluationRun.Requested;

    /// <summary>Gets every case, by the name it is filed under.</summary>
    public static TheoryData<string> Cases { get; } = new(DiscoveryCompositionScenario.All.Select(static scenario => scenario.Name));

    [Theory(Skip = AiEvaluationRun.SkipReason, SkipUnless = nameof(EvaluationsRequested))]
    [MemberData(nameof(Cases))]
    public async Task Compose_AQuestion_EveryDeclaredModelComposesOnlyFromTheExtractsItWasHanded(string caseName)
    {
        // Arrange
        var scenario = DiscoveryCompositionScenario.All.Single(candidate => candidate.Name == caseName);
        var judge = JudgeDeclaration.Read();
        var apiKey = EvaluationEndpoint.ApiKey();
        var repetitions = EvaluationRepetitions.Declared();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        var shortfalls = await Task.WhenAll(
            [.. ModelsUnderTest.For(ChatCapability.DiscoveryComposition).Select(model => MeasureAsync(scenario, judge, model, apiKey, repetitions, cancellationToken))]);

        // Assert
        AiEvaluationRun.AssertNoShortfalls(shortfalls.SelectMany(static modelShortfalls => modelShortfalls));
    }

    /// <summary>Puts one case to one model and names what it fell short on.</summary>
    private static Task<IReadOnlyList<string>> MeasureAsync(
        DiscoveryCompositionScenario scenario,
        JudgeDeclaration judge,
        ModelUnderTest modelUnderTest,
        string apiKey,
        int repetitions,
        CancellationToken cancellationToken) =>
        EvaluationRepetitions.MeasureAsync(
            EvaluationStore.OpenUnjudged([]),
            scenario.Name,
            modelUnderTest.Name,
            repetitions,
            async repetition =>
            {
                var modelSpend = new SpendMeter();
                var judgeSpend = new SpendMeter();

                using var model = ProviderChatClient.Open(modelUnderTest, apiKey, modelSpend);
                using var judgeClient = judge.Open(judgeSpend);

                var reporting = EvaluationStore.Open(judgeClient, judge.CachingKey, scenario.Evaluators);

                return [.. scenario.ShortfallsOf(await scenario.RunAsync(
                    reporting,
                    model,
                    modelUnderTest.Plan,
                    repetition,
                    modelSpend,
                    judgeSpend,
                    cancellationToken))];
            },
            cancellationToken);
}
