// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using MailFathom.Evaluations.Corpus;
using MailFathom.Evaluations.Costing;
using MailFathom.Evaluations.Enrichment;
using MailFathom.Evaluations.Judging;
using MailFathom.Evaluations.Reporting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Xunit;

namespace MailFathom.Evaluations.Answering;

/// <summary>
/// Proves, without calling any provider, that the structural checks a mail-answering run records fail on exactly the
/// answers they exist to catch, and that a repeated run is answered from the store.
/// </summary>
/// <remarks>
/// Free, and therefore not gated on the run switch. Like the enrichment scenario's free tests, they write a real store to
/// a temporary directory, because a run is filed there and read back from there.
/// </remarks>
public sealed class MailAnsweringScenarioTests : IDisposable
{
    private const string LumenfieldLookup = "Lumenfield INV-4827";

    private static readonly MailAnsweringScenario KeywordTrap =
        MailAnsweringScenario.All.Single(static scenario => scenario.Name == "MailAnswering.KeywordMatchesTheWrongMessage");

    /// <summary>Zofia Iversen's request for the corrected INV-4827, the message the keyword-trap question is answered by.</summary>
    private static readonly CorpusMessage LumenfieldInvoice = CorpusMessage.At(position: 20);

    private readonly DirectoryInfo store = Directory.CreateTempSubdirectory("mailfathom-evaluations-");

    [Fact]
    public async Task RunAsync_AModelThatLooksUpAndCitesTheEvidence_PassesEveryStructuralCheck()
    {
        // Arrange
        using var model = new ScriptedAnsweringChatClient(LumenfieldLookup, $"It needed 42 Lantern Way [{LumenfieldInvoice.Id}].");

        // Act
        var verdict = await this.RunAsync(KeywordTrap, model, executionName: "only");

        // Assert
        Assert.All(StructuralChecks(verdict), static check => Assert.True(check.Value, check.Reason));
    }

    [Fact]
    public async Task RunAsync_AModelThatAnswersWithoutLookingUp_FailsTheSearchCheck()
    {
        // Arrange
        using var model = new ScriptedAnsweringChatClient(queryText: null, "It needed 42 Lantern Way.");

        // Act
        var verdict = await this.RunAsync(KeywordTrap, model, executionName: "only");

        // Assert
        Assert.False(verdict.Get<BooleanMetric>(MailAnsweringScenario.SearchedMetricName).Value);
    }

    [Fact]
    public async Task RunAsync_AModelCitingAMessageItNeverRetrieved_FailsTheRetrievedMailCheckAlone()
    {
        // Arrange
        var unretrieved = CorpusMessage.At(position: 99);
        using var model = new ScriptedAnsweringChatClient(LumenfieldLookup, $"It needed 42 Lantern Way [{unretrieved.Id}].");

        // Act
        var verdict = await this.RunAsync(KeywordTrap, model, executionName: "only");

        // Assert
        Assert.Equal(
            (true, false),
            (verdict.Get<BooleanMetric>(MailAnsweringScenario.CitesExistingMailMetricName).Value,
                verdict.Get<BooleanMetric>(MailAnsweringScenario.CitesRetrievedMailMetricName).Value));
    }

    [Fact]
    public async Task RunAsync_AModelCitingAnIdentifierNoMessageCarries_FailsTheExistingMailCheck()
    {
        // Arrange
        using var model = new ScriptedAnsweringChatClient(LumenfieldLookup, $"It needed 42 Lantern Way [{Guid.Empty:D}].");

        // Act
        var verdict = await this.RunAsync(KeywordTrap, model, executionName: "only");

        // Assert
        Assert.False(verdict.Get<BooleanMetric>(MailAnsweringScenario.CitesExistingMailMetricName).Value);
    }

    [Fact]
    public async Task RunAsync_AModelCitingTheKeywordMatchInsteadOfTheAnsweringMessage_FailsTheEvidenceCheck()
    {
        // Arrange
        var wrongInvoice = CorpusMessage.At(position: 70);
        using var model = new ScriptedAnsweringChatClient("Alderwick INV-4827", $"It needed Alderwick Studio GmbH [{wrongInvoice.Id}].");

        // Act
        var verdict = await this.RunAsync(KeywordTrap, model, executionName: "only");

        // Assert
        Assert.Equal(
            (true, false),
            (verdict.Get<BooleanMetric>(MailAnsweringScenario.CitesRetrievedMailMetricName).Value,
                verdict.Get<BooleanMetric>(MailAnsweringScenario.RestsOnEvidenceMetricName).Value));
    }

    [Fact]
    public async Task RunAsync_AQuestionNothingAnswersAnsweredWithACitation_FailsTheEvidenceCheck()
    {
        // Arrange
        var unanswerable = MailAnsweringScenario.All.Single(static scenario => scenario.Name == "MailAnswering.NothingAnswers");
        using var model = new ScriptedAnsweringChatClient("check-up", $"Your dentist moved it [{LumenfieldInvoice.Id}].");

        // Act
        var verdict = await this.RunAsync(unanswerable, model, executionName: "only");

        // Assert
        Assert.False(verdict.Get<BooleanMetric>(MailAnsweringScenario.RestsOnEvidenceMetricName).Value);
    }

    [Fact]
    public async Task RunAsync_AgainOverAnUnchangedQuestionAndModel_AsksNeitherTheModelNorTheJudgeASecondTime()
    {
        // Arrange
        using var model = new ScriptedAnsweringChatClient(LumenfieldLookup, $"It needed 42 Lantern Way [{LumenfieldInvoice.Id}].");
        using var judge = Judge();

        await this.RunAsync(KeywordTrap, model, executionName: "first", judge);
        var asked = (model.Requests, judge.Requests);

        // Act
        var repeated = await this.RunAsync(KeywordTrap, model, executionName: "second", judge);

        // Assert
        Assert.Equal(asked, (model.Requests, judge.Requests));
        Assert.All(StructuralChecks(repeated), static check => Assert.True(check.Value, check.Reason));
    }

    [Fact]
    public void All_EveryPieceOfEvidence_IsCarriedBySomeCorpusMessage()
    {
        // Arrange
        var evidence = MailAnsweringScenario.All.SelectMany(static scenario => scenario.Evidence);

        // Act
        var uncarried = evidence.Where(static phrase => !CorpusMessage.All.Any(message =>
            message.GroundingText.Contains(phrase, StringComparison.OrdinalIgnoreCase)));

        // Assert
        Assert.Empty(uncarried);
    }

    public void Dispose() => this.store.Delete(recursive: true);

    private static IEnumerable<BooleanMetric> StructuralChecks(EvaluationResult verdict) =>
        [
            .. new[]
            {
                MailAnsweringScenario.SearchedMetricName,
                MailAnsweringScenario.CitesExistingMailMetricName,
                MailAnsweringScenario.CitesRetrievedMailMetricName,
                MailAnsweringScenario.RestsOnEvidenceMetricName,
                MailAnsweringScenario.WithinBoundsMetricName,
                MailAnsweringScenario.ReportsSpendAndModelMetricName,
            }.Select(verdict.Get<BooleanMetric>),
        ];

    private static ScriptedChatClient Judge() =>
        new(
            "<S0>The answer cites the message.</S0><S1>Resolved.</S1><S2>5</S2>",
            new ChatClientMetadata("planted-provider", new Uri("https://planted-judge-host.invalid/v1/"), "planted-judge-model"));

    private static ChatGenerationPlan PlanFor(IChatClient model) =>
        ChatGenerationPlan.Create(
            new ChatEndpoint(
                "evaluation",
                Address: null,
                model.GetService<ChatClientMetadata>()?.DefaultModelId ?? "model-under-test",
                ChatProviderApi.ChatCompletions,
                PublishedModelName: string.Empty),
            maximumOutputTokens: 1024,
            temperature: null,
            topP: null,
            reasoningEffort: null,
            maximumMessagesPerRequest: 8,
            maximumRequestCharacters: 64_000,
            maximumRequestImageOctets: 1024,
            requestTimeout: TimeSpan.FromSeconds(30));

    private async Task<EvaluationResult> RunAsync(
        MailAnsweringScenario scenario,
        IChatClient model,
        string executionName,
        ScriptedChatClient? judge = null)
    {
        using var ownJudge = judge is null ? Judge() : null;
        using var anonymousJudge = new AnonymousJudgeChatClient(judge ?? ownJudge!);
        var declaration = JudgeDeclaration.Of(new Uri("https://planted-judge-host.invalid/v1/"), "planted-judge-model", "planted-judge-key", reasoningEffort: null);
        var reporting = EvaluationStore.OpenAt(
            this.store.FullName,
            executionName,
            anonymousJudge,
            declaration.CachingKey,
            MailAnsweringScenario.Evaluators);

        return await scenario.RunAsync(
            reporting,
            model,
            PlanFor(model),
            new SpendMeter(),
            new SpendMeter(),
            TestContext.Current.CancellationToken);
    }
}
