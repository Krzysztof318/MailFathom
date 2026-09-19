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

namespace MailFathom.Evaluations.Descriptions;

/// <summary>Shows every declared model every image in <see cref="ImageDescriptionScenario.All" />.</summary>
/// <remarks>Shaped like the mail-answering evaluation, for the reasons it gives.</remarks>
public sealed class ImageDescriptionEvaluations
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
    public async Task Describe_EveryImage_EveryDeclaredModelDescribesOnlyWhatItShows()
    {
        // Arrange
        var judge = JudgeDeclaration.Read();
        var apiKey = EvaluationEndpoint.ApiKey();

        // Act
        var shortfalls = await Task.WhenAll([.. ModelsUnderTest.Plans().Select(plan => MeasureAsync(judge, plan, apiKey))]);

        // Assert
        AiEvaluationRun.AssertNoShortfalls(shortfalls.SelectMany(static modelShortfalls => modelShortfalls));
    }

    /// <summary>Puts every scenario to one model and names what it fell short on.</summary>
    private static async Task<IReadOnlyList<string>> MeasureAsync(
        JudgeDeclaration judge,
        ChatGenerationPlan plan,
        string apiKey)
    {
        var modelSpend = new SpendMeter();
        var judgeSpend = new SpendMeter();

        using var model = ProviderChatClient.Open(plan.Endpoint, apiKey, plan.RequestTimeout, modelSpend);
        using var judgeClient = judge.Open(judgeSpend);

        var reporting = EvaluationStore.Open(judgeClient, judge.CachingKey, ImageDescriptionScenario.Evaluators);
        List<string> shortfalls = [];

        foreach (var scenario in ImageDescriptionScenario.All)
        {
            var verdict = await scenario.RunAsync(
                reporting,
                model,
                plan,
                modelSpend,
                judgeSpend,
                TestContext.Current.CancellationToken);

            shortfalls.AddRange(scenario.ShortfallsOf(verdict).Select(shortfall => $"{plan.Endpoint.RoutedModelName}: {shortfall}"));
        }

        return shortfalls;
    }
}
