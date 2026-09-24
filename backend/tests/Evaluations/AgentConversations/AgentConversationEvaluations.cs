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
/// A theory over the questions rather than over the models, for the reason <c>DiscoveryPlanningEvaluations</c> gives,
/// so each question is its own result and runs beside the others under the assembly's parallelization. Each opens its
/// own clients, its own spend meters, and its own handle on the store for every model, so nothing is shared between two
/// questions or two models but the directory their results are filed in, each under its own scenario and iteration, and
/// the cost a question reports is read off meters no other question charges.
/// </para>
/// <para>
/// The models are measured at the same time. A model that falls short is collected rather than failing the question,
/// so one weak model never hides what the others did, and the question fails at the end naming every model that fell
/// short and why.
/// </para>
/// </remarks>
public sealed class AgentConversationEvaluations
{
    /// <summary>Gets whether an evaluation run was explicitly asked for.</summary>
    /// <remarks>Public and static because that is the shape xUnit reads a skip condition from.</remarks>
    public static bool EvaluationsRequested => AiEvaluationRun.Requested;

    /// <summary>Gets every case, by the name it is filed under.</summary>
    public static TheoryData<string> Cases { get; } = new(AgentConversationScenario.All.Select(static scenario => scenario.Name));

    [Theory(Skip = AiEvaluationRun.SkipReason, SkipUnless = nameof(EvaluationsRequested))]
    [MemberData(nameof(Cases))]
    public async Task Answer_AQuestion_EveryDeclaredModelAnswersInThePersonsLanguageAndProposesOnlyWhatWasAsked(string caseName)
    {
        // Arrange
        var scenario = AgentConversationScenario.All.Single(candidate => candidate.Name == caseName);
        var judge = JudgeDeclaration.Read();
        var apiKey = EvaluationEndpoint.ApiKey();
        var repetitions = EvaluationRepetitions.Declared();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        var shortfalls = await Task.WhenAll(
            [.. ModelsUnderTest.For(ChatCapability.Agent).Select(model => MeasureAsync(scenario, judge, model, apiKey, repetitions, cancellationToken))]);

        // Assert
        AiEvaluationRun.AssertNoShortfalls(shortfalls.SelectMany(static modelShortfalls => modelShortfalls));
    }

    /// <summary>Puts one question to one model and names what it fell short on.</summary>
    private static async Task<IReadOnlyList<string>> MeasureAsync(
        AgentConversationScenario scenario,
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

        var reporting = EvaluationStore.Open(judgeClient, judge.CachingKey, AgentConversationScenario.Evaluators);

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
