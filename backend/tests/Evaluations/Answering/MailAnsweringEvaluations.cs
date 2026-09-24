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
    /// <summary>Gets whether an evaluation run was explicitly asked for.</summary>
    /// <remarks>Public and static because that is the shape xUnit reads a skip condition from.</remarks>
    public static bool EvaluationsRequested => AiEvaluationRun.Requested;

    /// <summary>Gets every case, by the name it is filed under.</summary>
    public static TheoryData<string> Cases { get; } = new(MailAnsweringScenario.All.Select(static scenario => scenario.Name));

    [Theory(Skip = AiEvaluationRun.SkipReason, SkipUnless = nameof(EvaluationsRequested))]
    [MemberData(nameof(Cases))]
    public async Task Answer_AQuestion_EveryDeclaredModelAnswersFromTheMailItRetrieved(string caseName)
    {
        // Arrange
        var scenario = MailAnsweringScenario.All.Single(candidate => candidate.Name == caseName);
        var judge = JudgeDeclaration.Read();
        var apiKey = EvaluationEndpoint.ApiKey();
        var repetitions = EvaluationRepetitions.Declared();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        var shortfalls = await Task.WhenAll(
            [.. ModelsUnderTest.For(ChatCapability.MailAnswering).Select(model => MeasureAsync(scenario, judge, model, apiKey, repetitions, cancellationToken))]);

        // Assert
        AiEvaluationRun.AssertNoShortfalls(shortfalls.SelectMany(static modelShortfalls => modelShortfalls));
    }

    /// <summary>Puts one question to one model and names what it fell short on.</summary>
    private static async Task<IReadOnlyList<string>> MeasureAsync(
        MailAnsweringScenario scenario,
        JudgeDeclaration judge,
        ModelUnderTest modelUnderTest,
        string apiKey,
        int repetitions,
        CancellationToken cancellationToken)
    {
        var plan = modelUnderTest.Plan;
        var modelSpend = new SpendMeter();
        var judgeSpend = new SpendMeter();

        using var model = ProviderChatClient.Open(modelUnderTest, apiKey, modelSpend);
        using var judgeClient = judge.Open(judgeSpend);

        var reporting = EvaluationStore.Open(judgeClient, judge.CachingKey, MailAnsweringScenario.Evaluators);

        return await EvaluationRepetitions.MeasureAsync(
            reporting,
            scenario.Name,
            modelUnderTest.Name,
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
