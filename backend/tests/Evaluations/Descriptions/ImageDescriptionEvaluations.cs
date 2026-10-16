// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.Chat;
using MailFathom.Evaluations.Costing;
using MailFathom.Evaluations.Judging;
using MailFathom.Evaluations.Providers;
using MailFathom.Evaluations.Reporting;
using Xunit;

namespace MailFathom.Evaluations.Descriptions;

/// <summary>Shows every declared model every image in <see cref="ImageDescriptionScenario.All" />.</summary>
/// <remarks>Shaped like <see cref="AgentConversations.AgentConversationEvaluations" />, for the reasons it gives.</remarks>
public sealed class ImageDescriptionEvaluations
{
    /// <summary>Gets whether an evaluation run was explicitly asked for.</summary>
    /// <remarks>Public and static because that is the shape xUnit reads a skip condition from.</remarks>
    public static bool EvaluationsRequested => AiEvaluationRun.Requested;

    /// <summary>Gets every case, by the name it is filed under.</summary>
    public static TheoryData<string> Cases { get; } = new(ImageDescriptionScenario.All.Select(static scenario => scenario.Name));

    [Theory(Skip = AiEvaluationRun.SkipReason, SkipUnless = nameof(EvaluationsRequested))]
    [MemberData(nameof(Cases))]
    public async Task Describe_AnImage_EveryDeclaredModelDescribesOnlyWhatItShows(string caseName)
    {
        // Arrange
        var scenario = ImageDescriptionScenario.All.Single(candidate => candidate.Name == caseName);
        var judge = JudgeDeclaration.Read();
        var apiKey = EvaluationEndpoint.ApiKey();
        var repetitions = EvaluationRepetitions.Declared();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        var shortfalls = await Task.WhenAll(
            [.. ModelsUnderTest.For(ChatCapability.ImageDescription).Select(model => MeasureAsync(scenario, judge, model, apiKey, repetitions, cancellationToken))]);

        // Assert
        AiEvaluationRun.AssertNoShortfalls(shortfalls.SelectMany(static modelShortfalls => modelShortfalls));
    }

    /// <summary>Puts one case to one model and names what it fell short on.</summary>
    private static Task<IReadOnlyList<string>> MeasureAsync(
        ImageDescriptionScenario scenario,
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

                var reporting = EvaluationStore.Open(judgeClient, judge.CachingKey, ImageDescriptionScenario.Evaluators);

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
