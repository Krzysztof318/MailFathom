// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using MailFathom.Evaluations.Costing;
using MailFathom.Evaluations.Judging;
using MailFathom.Evaluations.Providers;
using MailFathom.Evaluations.Reporting;
using xRetry.v3;
using Xunit;

namespace MailFathom.Evaluations.Answering;

/// <summary>Asks every declared model every mailbox question in <see cref="MailAnsweringScenario.All" />.</summary>
/// <remarks>
/// <para>
/// Shaped like the enrichment evaluation for the reasons it gives: one test over the whole model list, the models
/// measured at the same time over clients, meters, and a store handle of their own, a model that falls short collected
/// rather than failing the run, and a retry that pays only for what the previous attempt did not reach.
/// </para>
/// <para>
/// One model's scenarios run one after another, because each one's cost is read off that model's meters and two
/// questions answered at once would each report the other's spend.
/// </para>
/// </remarks>
public sealed class MailAnsweringEvaluations
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
    public async Task Answer_EveryScenario_EveryDeclaredModelAnswersFromTheMailItRetrieved()
    {
        // Arrange
        var judge = JudgeDeclaration.Read();
        var apiKey = EvaluationEndpoint.ApiKey();
        var repetitions = EvaluationRepetitions.Declared();

        // Act
        var shortfalls = await Task.WhenAll([.. ModelsUnderTest.Plans().Select(plan => MeasureAsync(judge, plan, apiKey, repetitions))]);

        // Assert
        AiEvaluationRun.AssertNoShortfalls(shortfalls.SelectMany(static modelShortfalls => modelShortfalls));
    }

    /// <summary>Puts every scenario to one model and names what it fell short on.</summary>
    private static async Task<IReadOnlyList<string>> MeasureAsync(
        JudgeDeclaration judge,
        ChatGenerationPlan plan,
        string apiKey,
        int repetitions)
    {
        var modelSpend = new SpendMeter();
        var judgeSpend = new SpendMeter();

        using var model = ProviderChatClient.Open(plan.Endpoint, apiKey, plan.RequestTimeout, modelSpend);
        using var judgeClient = judge.Open(judgeSpend);

        var reporting = EvaluationStore.Open(judgeClient, judge.CachingKey, MailAnsweringScenario.Evaluators);
        List<string> shortfalls = [];

        foreach (var scenario in MailAnsweringScenario.All)
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
                    judgeSpend,
                    TestContext.Current.CancellationToken))],
                TestContext.Current.CancellationToken));
        }

        return shortfalls;
    }
}
