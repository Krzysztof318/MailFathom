// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using MailFathom.Evaluations.Costing;
using MailFathom.Evaluations.Judging;
using MailFathom.Evaluations.Providers;
using MailFathom.Evaluations.Reporting;
using Xunit;

namespace MailFathom.Evaluations.Answering;

/// <summary>Asks every declared model every mailbox question in <see cref="MailAnsweringScenario.All" />.</summary>
/// <remarks>Shaped like <see cref="AgentConversations.AgentConversationEvaluations" />, for the reasons it gives.</remarks>
public sealed class MailAnsweringEvaluations
{
    /// <summary>How many questions are asked at once.</summary>
    /// <remarks>Bounded by what a provider's rate limit is expected to take, for the reason the Agent's evaluation gives.</remarks>
    private const int ConcurrentQuestions = 8;

    /// <summary>Gets whether an evaluation run was explicitly asked for.</summary>
    /// <remarks>Public and static because that is the shape xUnit reads a skip condition from.</remarks>
    public static bool EvaluationsRequested => AiEvaluationRun.Requested;

    [Fact(Skip = AiEvaluationRun.SkipReason, SkipUnless = nameof(EvaluationsRequested))]
    public async Task Answer_EveryScenario_EveryDeclaredModelAnswersFromTheMailItRetrieved()
    {
        // Arrange
        var judge = JudgeDeclaration.Read();
        var apiKey = EvaluationEndpoint.ApiKey();
        var repetitions = EvaluationRepetitions.Declared();
        var plans = ModelsUnderTest.Plans();
        var scenarios = MailAnsweringScenario.All;
        var shortfalls = new IReadOnlyList<string>[scenarios.Count];

        // Act
        await Parallel.ForEachAsync(
            Enumerable.Range(0, scenarios.Count),
            new ParallelOptions { MaxDegreeOfParallelism = ConcurrentQuestions, CancellationToken = TestContext.Current.CancellationToken },
            async (index, cancellationToken) =>
            {
                var modelShortfalls = await Task.WhenAll(
                    [.. plans.Select(plan => MeasureAsync(scenarios[index], judge, plan, apiKey, repetitions, cancellationToken))]);

                shortfalls[index] = [.. modelShortfalls.SelectMany(static modelShortfall => modelShortfall)];
            });

        // Assert
        AiEvaluationRun.AssertNoShortfalls(shortfalls.SelectMany(static questionShortfalls => questionShortfalls));
    }

    /// <summary>Puts one question to one model and names what it fell short on.</summary>
    private static async Task<IReadOnlyList<string>> MeasureAsync(
        MailAnsweringScenario scenario,
        JudgeDeclaration judge,
        ChatGenerationPlan plan,
        string apiKey,
        int repetitions,
        CancellationToken cancellationToken)
    {
        var modelSpend = new SpendMeter();
        var judgeSpend = new SpendMeter();

        using var model = ProviderChatClient.Open(plan.Endpoint, apiKey, plan.RequestTimeout, modelSpend);
        using var judgeClient = judge.Open(judgeSpend);

        var reporting = EvaluationStore.Open(judgeClient, judge.CachingKey, MailAnsweringScenario.Evaluators);

        return await EvaluationRepetitions.MeasureAsync(
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
                judgeSpend,
                cancellationToken))],
            cancellationToken);
    }
}
