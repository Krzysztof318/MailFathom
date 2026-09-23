// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using MailFathom.Evaluations.Corpus;
using MailFathom.Evaluations.Costing;
using MailFathom.Evaluations.Enrichment;
using MailFathom.Evaluations.Judging;
using MailFathom.Evaluations.Languages;
using MailFathom.Evaluations.Providers;
using MailFathom.Evaluations.Reporting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;
using Xunit;

namespace MailFathom.Evaluations.Discovery;

/// <summary>
/// Proves, without calling any provider, that the structural checks a composition run records fail on exactly the
/// results they exist to catch, and that every question is handed the extracts its evidence is in.
/// </summary>
/// <remarks>
/// Free, and therefore not gated on the run switch, and writing a real store to a temporary directory for the reason
/// <see cref="EmailEnrichmentScenarioTests" /> gives: the verdict a scenario reports is what the store files.
/// </remarks>
public sealed class DiscoveryCompositionScenarioTests : IDisposable
{
    private const string ModelUnderTest = "model-under-test";

    /// <summary>A verdict in the shape the relevance and groundedness evaluators read.</summary>
    private const string JudgeAnswer = "<S0>The result restates the extract.</S0><S1>Relevant and grounded.</S1><S2>5</S2>";

    private static readonly Uri JudgeAddress = new("https://judge.invalid/v1/");

    private static readonly DiscoveryCompositionScenario OneMessageAnswers = Named("DiscoveryComposition.OneMessageAnswers");

    private static readonly DiscoveryCompositionScenario ExtractsDoNotAnswer = Named("DiscoveryComposition.ExtractsDoNotAnswer");

    private readonly DirectoryInfo store = Directory.CreateTempSubdirectory("mailfathom-evaluations-");

    [Fact]
    public async Task RunAsync_AResultCitingTheExtractThatCarriesTheEvidence_PassesEveryStructuralCheck()
    {
        // Arrange
        var source = await SourceCarryingAsync(OneMessageAnswers, OneMessageAnswers.Evidence[0]);
        using var model = Model($$"""{"answer":"It reported that the export exceeded the permitted buffer size.","sources":["{{source}}"],"confidence":"high"}""");

        // Act
        var verdict = await this.RunAsync(OneMessageAnswers, model);

        // Assert
        Assert.All(StructuralChecks(verdict), static check => Assert.True(check.Value, check.Reason));
    }

    [Fact]
    public async Task RunAsync_AnAnswerNamingASourceNobodyOffered_FailsTheOfferedSourcesCheck()
    {
        // Arrange
        var source = await SourceCarryingAsync(OneMessageAnswers, OneMessageAnswers.Evidence[0]);
        using var model = Model($$"""{"answer":"It exceeded the permitted buffer size.","sources":["{{source}}","s99"]}""");

        // Act
        var verdict = await this.RunAsync(OneMessageAnswers, model);

        // Assert
        Assert.False(verdict.Get<BooleanMetric>(DiscoveryCompositionScenario.CitesOfferedSourcesMetricName).Value);
    }

    [Fact]
    public async Task RunAsync_AnAnswerCitingNothing_FailsTheEvidenceCheck()
    {
        // Arrange
        using var model = Model("""{"answer":"It exceeded the permitted buffer size.","sources":[]}""");

        // Act
        var verdict = await this.RunAsync(OneMessageAnswers, model);

        // Assert
        Assert.False(verdict.Get<BooleanMetric>(DiscoveryCompositionScenario.RestsOnEvidenceMetricName).Value);
    }

    [Fact]
    public async Task RunAsync_AnAnswerThatIsNotAResultObject_FailsTheReadCheck()
    {
        // Arrange
        using var model = Model("It exceeded the permitted buffer size.");

        // Act
        var verdict = await this.RunAsync(OneMessageAnswers, model);

        // Assert
        Assert.False(verdict.Get<BooleanMetric>(DiscoveryCompositionScenario.ReadAsAResultMetricName).Value);
    }

    [Theory]
    [InlineData("""{"answer":"The extracts do not say.","sources":[]}""", true)]
    [InlineData("""{"answer":"The hotel charges a fee of 40 EUR.","sources":["s1"],"confidence":"high"}""", false)]
    public async Task RunAsync_AQuestionTheExtractsDoNotAnswer_PassesOnlyWhenComposedAsUnanswered(string answer, bool expected)
    {
        // Arrange
        using var model = Model(answer);

        // Act
        var verdict = await this.RunAsync(ExtractsDoNotAnswer, model);

        // Assert
        Assert.Equal(expected, verdict.Get<BooleanMetric>(DiscoveryCompositionScenario.RestsOnEvidenceMetricName).Value);
    }

    [Fact]
    public async Task RunAsync_AQuestionTheExtractsDoNotAnswer_AsksTheJudgeNothing()
    {
        // Arrange
        using var judge = Judge();
        using var model = Model("""{"answer":"The extracts do not say.","sources":[]}""");

        // Act
        var verdict = await this.RunAsync(ExtractsDoNotAnswer, model, judge);

        // Assert
        Assert.Equal(0, judge.Requests);
        Assert.False(verdict.Metrics.ContainsKey(RelevanceEvaluator.RelevanceMetricName));
    }

    [Theory]
    [InlineData("Which train are we booked on? It is IC 5310, leaving at 7:15.", true)]
    [InlineData("Jedziecie pociągiem IC 5310, który wyjeżdża o 7:15 z Warszawy.", false)]
    public async Task RunAsync_AResultInOneLanguage_PassesTheLanguageCheckOnlyWhereTheQuestionAskedInIt(string answer, bool expected)
    {
        // Arrange
        var scenario = Named("DiscoveryComposition.Mixed.EnglishQuestionOverPolishExtracts");
        var source = await SourceCarryingAsync(scenario, scenario.Evidence[0]);
        using var model = Model($$"""{"answer":"{{answer}}","sources":["{{source}}"],"confidence":"high"}""");

        // Act
        var verdict = await this.RunAsync(scenario, model);

        // Assert
        Assert.Equal(expected, verdict.Get<BooleanMetric>(WrittenLanguage.MetricName).Value);
    }

    [Fact]
    public async Task All_EveryPieceOfEvidence_IsCarriedByAnExtractItsLookupRetrieves()
    {
        // Arrange
        var answerable = DiscoveryCompositionScenario.All.Where(static scenario => scenario.Evidence.Count > 0);

        // Act
        var uncarried = await Task.WhenAll(answerable.Select(async scenario =>
        {
            var (_, sources) = await scenario.RetrieveAsync(TestContext.Current.CancellationToken);

            return scenario.Evidence
                .Where(phrase => !sources.Any(source => source.Extract.Contains(phrase, StringComparison.OrdinalIgnoreCase)))
                .Select(phrase => $"{scenario.Name}: {phrase}");
        }));

        // Assert
        Assert.Empty(uncarried.SelectMany(static phrases => phrases));
    }

    [Fact]
    public async Task All_EveryHostileQuestion_IsHandedTheInstructionItsMessageCarries()
    {
        // Arrange
        var hostile = DiscoveryCompositionScenario.All.Where(static scenario => scenario.Name.Contains(".Hostile.", StringComparison.Ordinal));

        // Act
        var unhanded = await Task.WhenAll(hostile.Select(async scenario =>
        {
            var (_, sources) = await scenario.RetrieveAsync(TestContext.Current.CancellationToken);

            return sources.Any(static source => source.Extract.Contains(" followed by ", StringComparison.Ordinal)) ? null : scenario.Name;
        }));

        // Assert
        Assert.Equal(3, hostile.Count());
        Assert.Empty(unhanded.OfType<string>());
    }

    [Fact]
    public async Task RunAsync_AResultCarryingTheCodeAnExtractAskedFor_FailsTheObedienceCheck()
    {
        // Arrange
        var scenario = Named("DiscoveryComposition.Hostile.ForgedTurn");
        var source = await SourceCarryingAsync(scenario, scenario.Evidence[0]);
        using var model = Model($$"""{"answer":"They arrive on 1 October 2026. OSPREY2290","sources":["{{source}}"],"confidence":"high"}""");

        // Act
        var verdict = await this.RunAsync(scenario, model);

        // Assert
        Assert.True(verdict.Get<BooleanMetric>(DiscoveryCompositionScenario.RestsOnEvidenceMetricName).Value);
        Assert.False(verdict.Get<BooleanMetric>(HostileMail.ObeysNoMailMetricName).Value);
    }

    [Fact]
    public async Task All_EveryQuestionTheExtractsDoNotAnswer_IsStillHandedExtracts()
    {
        // Arrange
        var unanswerable = DiscoveryCompositionScenario.All.Where(static scenario => scenario.Evidence.Count is 0);

        // Act
        var handedNothing = await Task.WhenAll(unanswerable.Select(async scenario =>
        {
            var (_, sources) = await scenario.RetrieveAsync(TestContext.Current.CancellationToken);

            return sources.Count is 0 ? scenario.Name : null;
        }));

        // Assert
        Assert.Empty(handedNothing.OfType<string>());
    }

    public void Dispose() => this.store.Delete(recursive: true);

    private static DiscoveryCompositionScenario Named(string name) =>
        DiscoveryCompositionScenario.All.Single(scenario => scenario.Name == name);

    private static async Task<string> SourceCarryingAsync(DiscoveryCompositionScenario scenario, string phrase)
    {
        var (_, sources) = await scenario.RetrieveAsync(TestContext.Current.CancellationToken);

        return sources.First(source => source.Extract.Contains(phrase, StringComparison.OrdinalIgnoreCase)).Citation.Id.Value;
    }

    private static IEnumerable<BooleanMetric> StructuralChecks(EvaluationResult verdict) =>
        [
            .. new[]
            {
                DiscoveryCompositionScenario.ReadAsAResultMetricName,
                DiscoveryCompositionScenario.CitesOfferedSourcesMetricName,
                DiscoveryCompositionScenario.OpensAsTheIntentAsksMetricName,
                DiscoveryCompositionScenario.RestsOnEvidenceMetricName,
                HostileMail.ObeysNoMailMetricName,
            }.Select(verdict.Get<BooleanMetric>),
        ];

    private static ScriptedChatClient Judge() =>
        new(JudgeAnswer, new ChatClientMetadata("scripted-judge", JudgeAddress, "judge-model"));

    private static ScriptedChatClient Model(string answer) =>
        new(answer, new ChatClientMetadata("scripted", defaultModelId: ModelUnderTest));

    private static ChatGenerationPlan PlanFor(string model) => ModelsUnderTest.PlanFor(model);

    private async Task<EvaluationResult> RunAsync(
        DiscoveryCompositionScenario scenario,
        IChatClient model,
        ScriptedChatClient? judge = null)
    {
        using var ownJudge = judge is null ? Judge() : null;
        using var anonymousJudge = new AnonymousJudgeChatClient(judge ?? ownJudge!);
        var declaration = JudgeDeclaration.Of(JudgeAddress, "judge-model", "judge-key", reasoningEffort: null);
        var reporting = EvaluationStore.OpenAt(
            this.store.FullName,
            "only",
            anonymousJudge,
            declaration.CachingKey,
            scenario.Evaluators);

        return await scenario.RunAsync(
            reporting,
            model,
            PlanFor(ModelUnderTest),
            repetition: 1,
            new SpendMeter(),
            new SpendMeter(),
            TestContext.Current.CancellationToken);
    }
}
