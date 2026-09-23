// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using MailFathom.Evaluations.Costing;
using MailFathom.Evaluations.Judging;
using MailFathom.Evaluations.Providers;
using MailFathom.Evaluations.Reporting;
using Xunit;

namespace MailFathom.Evaluations.AgentConversations;

/// <summary>Asks every declared model every question in <see cref="AgentConversationScenario.All" />.</summary>
/// <remarks>
/// <para>
/// One test over the whole model list rather than a theory per model, because the list is read from the run's
/// declaration and a theory's data is read before the skip condition is: a run nobody asked for would fail on a list
/// nobody declared. Each model is still its own result in the store, filed under its name.
/// </para>
/// <para>
/// The questions are asked at the same time inside the test rather than as a theory over them, because xUnit runs one
/// class's tests one after another, and a group this large asked in turn would alone decide how long a run takes. Each
/// question opens its own clients, its own spend meters, and its own handle on the store for every model, so nothing is
/// shared between two questions or two models but the directory their results are filed in, each under its own
/// scenario and iteration, and the cost a question reports is read off meters no other question charges.
/// </para>
/// <para>
/// The models are measured at the same time as well. A model that falls short is collected rather than failing the
/// run, so one weak model or one hard question never hides what the others did, and the test fails at the end naming
/// every question and model that fell short and why.
/// </para>
/// </remarks>
public sealed class AgentConversationEvaluations
{
    /// <summary>How many questions are asked at once.</summary>
    /// <remarks>
    /// A question waits on a provider rather than on a processor, so the bound is what a provider's rate limit is expected
    /// to take: this many questions times the declared models, with <see cref="TransientProviderRetryChatClient" />
    /// asking again after a rate limit that goes past it.
    /// </remarks>
    private const int ConcurrentQuestions = 8;

    /// <summary>Gets whether an evaluation run was explicitly asked for.</summary>
    /// <remarks>Public and static because that is the shape xUnit reads a skip condition from.</remarks>
    public static bool EvaluationsRequested => AiEvaluationRun.Requested;

    [Fact(Skip = AiEvaluationRun.SkipReason, SkipUnless = nameof(EvaluationsRequested))]
    public async Task Answer_EveryScenario_EveryDeclaredModelAnswersInThePersonsLanguageAndProposesOnlyWhatWasAsked()
    {
        // Arrange
        var judge = JudgeDeclaration.Read();
        var apiKey = EvaluationEndpoint.ApiKey();
        var repetitions = EvaluationRepetitions.Declared();
        var plans = ModelsUnderTest.Plans();
        var scenarios = AgentConversationScenario.All;
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
        AgentConversationScenario scenario,
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

        var reporting = EvaluationStore.Open(judgeClient, judge.CachingKey, AgentConversationScenario.Evaluators);

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
