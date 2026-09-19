// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using MailFathom.Application.Emails.ReplyDrafts;
using MailFathom.Evaluations.Corpus;
using MailFathom.Evaluations.Costing;
using MailFathom.Evaluations.Enrichment;
using MailFathom.Evaluations.Judging;
using MailFathom.Evaluations.Reporting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Xunit;

namespace MailFathom.Evaluations.ReplyDrafts;

/// <summary>
/// Proves, without calling any provider, that the structural checks a drafting run records fail on exactly the drafts
/// they exist to catch, and that every scenario answers a correspondent's message rather than the mailbox's own.
/// </summary>
/// <remarks>
/// Free, and therefore not gated on the run switch, and writing a real store to a temporary directory for the reason
/// <see cref="EmailEnrichmentScenarioTests" /> gives: the verdict a scenario reports is what the store files.
/// </remarks>
public sealed class ReplyDraftScenarioTests : IDisposable
{
    private const string ModelUnderTest = "model-under-test";

    /// <summary>A verdict in the shape the task adherence evaluator reads.</summary>
    private const string JudgeAnswer = "<S0>The reply does what was asked.</S0><S1>Adheres.</S1><S2>5</S2>";

    private const string FaithfulDraft =
        """{"body":"Dear Zofia,\nThank you for checking the corrected invoice. We now consider INV-4827 closed.\nBest regards,\nMaren","claims":[{"text":"The corrected INV-4827 was checked.","messages":[2]}],"recipients":[0]}""";

    private static readonly Uri JudgeAddress = new("https://judge.invalid/v1/");

    private static readonly ReplyDraftScenario ClosesAnInvoiceThread = Named("ReplyDraft.ClosesAnInvoiceThread");

    private static readonly ReplyDraftScenario AssertsWhatTheConversationDoesNot = Named("ReplyDraft.AssertsWhatTheConversationDoesNot");

    private readonly DirectoryInfo store = Directory.CreateTempSubdirectory("mailfathom-evaluations-");

    [Fact]
    public async Task RunAsync_ADraftProposingTheCorrespondentAndCitingTheConversation_PassesEveryStructuralCheck()
    {
        // Arrange
        using var model = Model(FaithfulDraft);

        // Act
        var verdict = await this.RunAsync(ClosesAnInvoiceThread, model);

        // Assert
        Assert.All(StructuralChecks(verdict), static check => Assert.True(check.Value, check.Reason));
    }

    [Theory]
    [InlineData("""{"body":"Thank you, INV-4827 is closed.","claims":[],"recipients":[0,3]}""", ReplyDraftScenario.ProposesNumberedPeopleMetricName)]
    [InlineData("""{"body":"Thank you, INV-4827 is closed.","claims":[{"text":"INV-4827 was checked.","messages":[7]}],"recipients":[0]}""", ReplyDraftScenario.CitesNumberedMessagesMetricName)]
    [InlineData("""{"body":"Thank you, INV-4827 is closed. Write to zofia.iversen@quietfjord.test with anything else.","claims":[],"recipients":[0]}""", ReplyDraftScenario.NamesNoAddressMetricName)]
    [InlineData("""{"body":"Thank you, INV-4827 is closed.","claims":[{"text":"INV-4827 was checked.","messages":[0,1,2,0,1]}],"recipients":[0]}""", ReplyDraftScenario.WithinBoundsMetricName)]
    [InlineData("I would thank her and close the invoice.", ReplyDraftScenario.ReadAsADraftMetricName)]
    public async Task RunAsync_ADraftBreakingOneRule_FailsTheCheckForThatRule(string answer, string failedCheck)
    {
        // Arrange
        using var model = Model(answer);

        // Act
        var verdict = await this.RunAsync(ClosesAnInvoiceThread, model);

        // Assert
        Assert.False(verdict.Get<BooleanMetric>(failedCheck).Value);
    }

    [Fact]
    public async Task RunAsync_ABodyPastADraftsBound_FailsTheBoundsCheck()
    {
        // Arrange
        using var model = Model($$"""{"body":"{{new string('a', ReplyDraft.MaximumBodyLength + 1)}}","claims":[],"recipients":[0]}""");

        // Act
        var verdict = await this.RunAsync(ClosesAnInvoiceThread, model);

        // Assert
        Assert.False(verdict.Get<BooleanMetric>(ReplyDraftScenario.WithinBoundsMetricName).Value);
    }

    [Theory]
    [InlineData("""{"body":"The payment for INV-ATLAS-1031 arrived on 10 October.","claims":[{"text":"The payment for INV-ATLAS-1031 arrived on 10 October 2026.","messages":[]}],"recipients":[0]}""", true)]
    [InlineData("""{"body":"The payment for INV-ATLAS-1031 arrived on 10 October.","claims":[{"text":"The payment for INV-ATLAS-1031 arrived on 10 October 2026.","messages":[2]}],"recipients":[0]}""", false)]
    public async Task RunAsync_AnAskTheConversationDoesNotSupport_PassesOnlyWhereTheClaimIsMarked(string answer, bool expected)
    {
        // Arrange
        using var model = Model(answer);

        // Act
        var verdict = await this.RunAsync(AssertsWhatTheConversationDoesNot, model);

        // Assert
        Assert.Equal(expected, verdict.Get<BooleanMetric>(ReplyDraftScenario.MarksUnsupportedClaimMetricName).Value);
    }

    [Fact]
    public void Sources_EveryScenario_AnswersACorrespondentAndNumbersNobodyButThem()
    {
        // Act
        var readings = ReplyDraftScenario.All.Select(static scenario =>
        {
            var sources = scenario.Sources();

            return (
                AnswersCorrespondent: CorpusMessage.At(scenario.AnsweredPosition).Sender != ReplyDraftScenario.MailboxAddress,
                Numbered: sources.Participants.Select(static person => person.Address.NormalizedAddress).ToArray(),
                Answered: sources.Messages[^1].StoredEmailId == CorpusMessage.At(scenario.AnsweredPosition).Id);
        });

        // Assert
        Assert.All(readings, static reading =>
        {
            Assert.True(reading.AnswersCorrespondent);
            Assert.True(reading.Answered);
            Assert.Single(reading.Numbered);
            Assert.DoesNotContain(ReplyDraftScenario.MailboxAddress, reading.Numbered);
        });
    }

    public void Dispose() => this.store.Delete(recursive: true);

    private static ReplyDraftScenario Named(string name) =>
        ReplyDraftScenario.All.Single(scenario => scenario.Name == name);

    private static IEnumerable<BooleanMetric> StructuralChecks(EvaluationResult verdict) =>
        [
            .. new[]
            {
                ReplyDraftScenario.ReadAsADraftMetricName,
                ReplyDraftScenario.ProposesNumberedPeopleMetricName,
                ReplyDraftScenario.CitesNumberedMessagesMetricName,
                ReplyDraftScenario.NamesNoAddressMetricName,
                ReplyDraftScenario.WithinBoundsMetricName,
            }.Select(verdict.Get<BooleanMetric>),
        ];

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

    private async Task<EvaluationResult> RunAsync(ReplyDraftScenario scenario, IChatClient model)
    {
        using var judge = new ScriptedChatClient(JudgeAnswer, new ChatClientMetadata("scripted-judge", JudgeAddress, "judge-model"));
        using var anonymousJudge = new AnonymousJudgeChatClient(judge);
        var declaration = JudgeDeclaration.Of(JudgeAddress, "judge-model", "judge-key", reasoningEffort: null);
        var reporting = EvaluationStore.OpenAt(
            this.store.FullName,
            "only",
            anonymousJudge,
            declaration.CachingKey,
            ReplyDraftScenario.Evaluators);

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
