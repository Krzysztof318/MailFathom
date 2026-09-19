// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.RegularExpressions;
using MailFathom.AI.Chat;
using MailFathom.Evaluations.Costing;
using MailFathom.Evaluations.Judging;
using MailFathom.Evaluations.Reporting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Xunit;

namespace MailFathom.Evaluations.Enrichment;

/// <summary>
/// Proves, without calling any provider, what the store a run publishes holds and what a repeated run costs — the two
/// promises that let the store be kept and shared at all.
/// </summary>
/// <remarks>
/// <para>
/// Free, and therefore not gated on the run switch: both providers are scripted, so every run of this project proves
/// them before or beside the scenarios that spend credit.
/// </para>
/// <para>
/// They write a real store to a temporary directory and read back every file in it, which is the exception to the
/// file-system rule this project states for itself: the claim is about the bytes the disk store writes, and the files
/// are what the workflow publishes.
/// </para>
/// </remarks>
public sealed partial class EmailEnrichmentScenarioTests : IDisposable
{
    private const string ModelUnderTest = "model-under-test";
    private const string JudgeModel = "planted-judge-model-4c1e";
    private const string JudgeApiKey = "planted-judge-key-9b27";
    private static readonly Uri JudgeAddress = new("https://planted-judge-host.invalid/v1/");

    /// <summary>An answer the enrichment agent may write: one reading of the message, citing its first passage.</summary>
    private const string EnrichmentAnswer =
        """{"sense":{"text":"A reply chasing an outstanding invoice.","reason":"It asks whether INV-6044 has been scheduled.","passages":[0]}}""";

    /// <summary>A verdict in the shape the groundedness evaluator reads.</summary>
    private const string JudgeAnswer = "<S0>The reading restates the message.</S0><S1>Supported.</S1><S2>5</S2>";

    private readonly DirectoryInfo store = Directory.CreateTempSubdirectory("mailfathom-evaluations-");

    [Fact]
    public async Task RunAsync_WithADeclaredJudge_PublishesNoValueOfItAndNoAddressOutsideAReservedDomain()
    {
        // Arrange
        var declaration = JudgeDeclaration.Of(JudgeAddress, JudgeModel, JudgeApiKey, reasoningEffort: null);
        using var judge = new ScriptedChatClient(JudgeAnswer, new ChatClientMetadata("planted-provider", JudgeAddress, JudgeModel));
        using var model = ModelClient();

        // Act
        await this.RunScenarioAsync(declaration, judge, model, executionName: "only");

        // Assert
        var published = this.ReadEveryStoredFile();

        // The model under test is named in the store by design, which is also what shows this reading sees the store's
        // content rather than an empty directory: the absences below are measured on the same text.
        Assert.Contains(ModelUnderTest, published, StringComparison.Ordinal);
        Assert.Contains(AnonymousJudgeChatClient.Name, published, StringComparison.Ordinal);

        string[] judgeValues = [JudgeModel, JudgeApiKey, JudgeAddress.Host, "planted-provider", declaration.CachingKey];
        Assert.DoesNotContain(judgeValues, value => published.Contains(value, StringComparison.OrdinalIgnoreCase));

        var addresses = EmailAddress().Matches(published).Select(static match => match.Value).Distinct().ToArray();
        Assert.NotEmpty(addresses);
        Assert.DoesNotContain(addresses, static address => !ReservedDomain().IsMatch(address));
    }

    [Fact]
    public async Task RunAsync_AgainOverAnUnchangedPromptAndModel_AsksNeitherTheModelNorTheJudgeASecondTime()
    {
        // Arrange
        var declaration = JudgeDeclaration.Of(JudgeAddress, JudgeModel, JudgeApiKey, reasoningEffort: null);
        using var judge = new ScriptedChatClient(JudgeAnswer, new ChatClientMetadata("planted-provider", JudgeAddress, JudgeModel));
        using var model = ModelClient();

        await this.RunScenarioAsync(declaration, judge, model, executionName: "first");

        // Act
        var repeated = await this.RunScenarioAsync(declaration, judge, model, executionName: "second");

        // Assert
        Assert.Equal((1, 1), (model.Requests, judge.Requests));
        Assert.NotEmpty(repeated.Marks);
    }

    [Fact]
    public async Task RunAsync_WithNothingReachingAProvider_ReportsTheRunAsHavingSpentNothing()
    {
        // Arrange
        var declaration = JudgeDeclaration.Of(JudgeAddress, JudgeModel, JudgeApiKey, reasoningEffort: null);
        using var judge = new ScriptedChatClient(JudgeAnswer, new ChatClientMetadata("planted-provider", JudgeAddress, JudgeModel));
        using var model = ModelClient();

        // Act
        var outcome = await this.RunScenarioAsync(declaration, judge, model, executionName: "only");

        // Assert
        var cost = outcome.Verdict.Get<NumericMetric>(EvaluationCost.MetricName);

        Assert.Null(cost.Value);
        Assert.Equal("0", cost.Metadata?["paid-calls"]);
    }

    public void Dispose() => this.store.Delete(recursive: true);

    private static ScriptedChatClient ModelClient() =>
        new(EnrichmentAnswer, new ChatClientMetadata("scripted", defaultModelId: ModelUnderTest));

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

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")]
    private static partial Regex EmailAddress();

    /// <summary>The domains RFC 2606 and RFC 6761 set aside, which no mailbox anybody receives mail in can sit under.</summary>
    [GeneratedRegex(@"@(?:[A-Za-z0-9-]+\.)*(?:test|example|invalid|localhost|example\.(?:com|net|org))$", RegexOptions.IgnoreCase)]
    private static partial Regex ReservedDomain();

    private async Task<EmailEnrichmentOutcome> RunScenarioAsync(
        JudgeDeclaration declaration,
        IChatClient judge,
        IChatClient model,
        string executionName)
    {
        using var anonymousJudge = new AnonymousJudgeChatClient(judge);
        var reporting = EvaluationStore.OpenAt(
            this.store.FullName,
            executionName,
            anonymousJudge,
            declaration.CachingKey,
            EmailEnrichmentScenario.Evaluators);

        return await EmailEnrichmentScenario.RunAsync(
            reporting,
            model,
            PlanFor(ModelUnderTest),
            repetition: 1,
            new SpendMeter(),
            new SpendMeter(),
            TestContext.Current.CancellationToken);
    }

    private string ReadEveryStoredFile() =>
        string.Join(
            '\n',
            this.store
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Select(static file => $"{file.FullName}\n{File.ReadAllText(file.FullName)}"));
}
