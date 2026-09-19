// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using MailFathom.Evaluations.Costing;
using MailFathom.Evaluations.Enrichment;
using MailFathom.Evaluations.Judging;
using MailFathom.Evaluations.Reporting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Xunit;

namespace MailFathom.Evaluations.Discovery;

/// <summary>
/// Proves, without calling any provider, that each case holds a plan to what its question stated and that the judge is
/// asked about an ambiguous question alone.
/// </summary>
/// <remarks>
/// Free, and therefore not gated on the run switch, and writing a real store to a temporary directory for the reason
/// <see cref="EmailEnrichmentScenarioTests" /> gives: the verdict a case reports is what the store files.
/// </remarks>
public sealed class DiscoveryPlanningScenarioTests : IDisposable
{
    private const string ModelUnderTest = "model-under-test";
    private static readonly Uri JudgeAddress = new("https://judge.invalid/v1/");

    /// <summary>A verdict in the shape the intent resolution evaluator reads.</summary>
    private const string JudgeAnswer =
        """{"explanation":"A reasonable reading.","conversation_has_intent":true,"agent_perceived_intent":"find figures","actual_user_intent":"find figures","correct_intent_detected":true,"intent_resolved":true,"resolution_score":5}""";

    private readonly DirectoryInfo store = Directory.CreateTempSubdirectory("mailfathom-evaluations-");

    [Theory]
    [InlineData("ExplicitScope", """{"intent":"findFact","sufficientPassages":5,"lookups":[{"queryText":"invoice unpaid","senderAddress":"Billing@Northwind.example"}]}""", true)]
    [InlineData("ExplicitScope", """{"intent":"findFact","sufficientPassages":5,"lookups":[{"queryText":"northwind invoice unpaid"}]}""", false)]
    [InlineData("NoScope", """{"intent":"findFact","sufficientPassages":3,"lookups":[{"queryText":"offsite venue confirmed"}]}""", true)]
    [InlineData("NoScope", """{"intent":"findFact","sufficientPassages":3,"lookups":[{"queryText":"offsite venue","receivedOnOrAfter":"2026-01-01T00:00:00Z"}]}""", false)]
    [InlineData("NamesPerson", """{"intent":"findFact","sufficientPassages":4,"lookups":[{"queryText":"Priya migration next quarter"}]}""", true)]
    [InlineData("NamesPerson", """{"intent":"findFact","sufficientPassages":4,"lookups":[{"queryText":"migration next quarter","senderAddress":"priya.raman@example.com"}]}""", false)]
    [InlineData("NamesPeriod", """{"intent":"findFact","sufficientPassages":4,"lookups":[{"queryText":"rent increase","receivedOnOrAfter":"2026-03-01T00:00:00+01:00","receivedBefore":"2026-04-01T00:00:00+02:00"}]}""", true)]
    [InlineData("NamesPeriod", """{"intent":"findFact","sufficientPassages":4,"lookups":[{"queryText":"rent increase March 2026"}]}""", false)]
    [InlineData("OutsideWhatARunCanDo", """{"intent":"unclassified","sufficientPassages":3,"lookups":[{"queryText":"Tomasz offer"}]}""", true)]
    [InlineData("OutsideWhatARunCanDo", """{"intent":"findFact","sufficientPassages":3,"lookups":[{"queryText":"Tomasz offer"}]}""", false)]
    [InlineData("NoScope", "I would look for the venue confirmation.", false)]
    public async Task RunAsync_APlanForACase_RecordsWhetherItSaysWhatTheQuestionStated(
        string caseName,
        string answer,
        bool expected)
    {
        // Arrange
        using var judge = Judge();
        using var model = Model(answer);

        // Act
        var outcome = await this.RunScenarioAsync(DiscoveryPlanningCase.Named(caseName), judge, model);

        // Assert
        var metric = outcome.Verdict.Get<BooleanMetric>(DiscoveryPlanningScenario.ExpectationMetricName);

        Assert.Equal(expected, metric.Value);
        Assert.Equal(expected, outcome.Shortfall is null);
    }

    [Fact]
    public async Task RunAsync_AQuestionWithOneRightReading_AsksTheJudgeNothing()
    {
        // Arrange
        using var judge = Judge();
        using var model = Model("""{"intent":"findFact","sufficientPassages":3,"lookups":[{"queryText":"offsite venue"}]}""");

        // Act
        var outcome = await this.RunScenarioAsync(DiscoveryPlanningCase.Named("NoScope"), judge, model);

        // Assert
        Assert.Equal(0, judge.Requests);
        Assert.False(outcome.Verdict.Metrics.ContainsKey(DiscoveryPlanningScenario.IntentResolutionMetricName));
    }

    [Fact]
    public async Task RunAsync_AnAmbiguousQuestion_FilesTheJudgesIntentResolutionBesideTheModel()
    {
        // Arrange
        using var judge = Judge();
        using var model = Model("""{"intent":"findDocuments","sufficientPassages":4,"lookups":[{"queryText":"Q3 figures","hasAttachments":true}]}""");

        // Act
        var outcome = await this.RunScenarioAsync(DiscoveryPlanningCase.Named("AmbiguousDocumentOrFact"), judge, model);

        // Assert
        var resolution = outcome.Verdict.Get<NumericMetric>(DiscoveryPlanningScenario.IntentResolutionMetricName);

        Assert.Equal((1, 5d), (judge.Requests, resolution.Value));
        Assert.Equal(ModelUnderTest, outcome.Model);
        Assert.NotNull(outcome.Verdict.Get<NumericMetric>(EvaluationCost.MetricName));
    }

    public void Dispose() => this.store.Delete(recursive: true);

    private static ScriptedChatClient Judge() =>
        new(JudgeAnswer, new ChatClientMetadata("scripted-judge", JudgeAddress, "judge-model"));

    private static ScriptedChatClient Model(string answer) =>
        new(answer, new ChatClientMetadata("scripted", defaultModelId: ModelUnderTest));

    private static ChatGenerationPlan PlanFor(string model) =>
        ChatGenerationPlan.Create(
            new ChatEndpoint("evaluation", Address: null, model, ChatProviderApi.ChatCompletions, PublishedModelName: string.Empty),
            maximumOutputTokens: 1024,
            temperature: null,
            topP: null,
            reasoningEffort: null,
            maximumMessagesPerRequest: 8,
            maximumRequestCharacters: 64_000,
            maximumRequestImageOctets: 1024,
            requestTimeout: TimeSpan.FromSeconds(30));

    private async Task<DiscoveryPlanningOutcome> RunScenarioAsync(
        DiscoveryPlanningCase scenario,
        IChatClient judge,
        IChatClient model)
    {
        var declaration = JudgeDeclaration.Of(JudgeAddress, "judge-model", "judge-key", reasoningEffort: null);
        using var anonymousJudge = new AnonymousJudgeChatClient(judge);
        var reporting = EvaluationStore.OpenAt(
            this.store.FullName,
            "only",
            anonymousJudge,
            declaration.CachingKey,
            DiscoveryPlanningScenario.EvaluatorsFor(scenario));

        return await DiscoveryPlanningScenario.RunAsync(
            reporting,
            model,
            PlanFor(ModelUnderTest),
            scenario,
            new SpendMeter(),
            new SpendMeter(),
            TestContext.Current.CancellationToken);
    }
}
