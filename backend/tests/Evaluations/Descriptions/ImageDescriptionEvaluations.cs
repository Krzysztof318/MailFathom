// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using MailFathom.Evaluations.Costing;
using MailFathom.Evaluations.Judging;
using MailFathom.Evaluations.Providers;
using MailFathom.Evaluations.Reporting;
using Xunit;

namespace MailFathom.Evaluations.Descriptions;

/// <summary>Shows every declared model every image in <see cref="ImageDescriptionScenario.All" />.</summary>
/// <remarks>Shaped like <see cref="ReplyDrafts.ReplyDraftEvaluations" />, for the reasons it gives.</remarks>
public sealed class ImageDescriptionEvaluations
{
    /// <summary>Gets whether an evaluation run was explicitly asked for.</summary>
    /// <remarks>Public and static because that is the shape xUnit reads a skip condition from.</remarks>
    public static bool EvaluationsRequested => AiEvaluationRun.Requested;

    [Fact(Skip = AiEvaluationRun.SkipReason, SkipUnless = nameof(EvaluationsRequested))]
    public async Task Describe_EveryImage_EveryDeclaredModelDescribesOnlyWhatItShows()
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

        var reporting = EvaluationStore.Open(judgeClient, judge.CachingKey, ImageDescriptionScenario.Evaluators);
        List<string> shortfalls = [];

        foreach (var scenario in ImageDescriptionScenario.All)
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
