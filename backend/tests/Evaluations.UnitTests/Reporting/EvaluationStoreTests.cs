// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using MailFathom.AI.Chat;
using MailFathom.Evaluations.Providers;
using MailFathom.Evaluations.Reporting;
using MailFathom.Evaluations.UnitTests.Enrichment;
using MailFathom.Evaluations.UnitTests.StructuredAnswers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation.Reporting;
using Xunit;

namespace MailFathom.Evaluations.UnitTests.Reporting;

/// <summary>Proves an answer is read back only under the request it was given for, and that each answer is tallied by where it came from.</summary>
/// <remarks>
/// Free: the model is scripted, and the store is written to a temporary directory for the reason
/// <see cref="EmailEnrichmentScenarioTests" /> gives, since what a later run reads back is what the store kept.
/// </remarks>
public sealed class EvaluationStoreTests : IDisposable
{
    private const string Held = "Scenario.Held";

    private const string SearchSchema = """{"type":"object","properties":{"query":{"type":"string"}}}""";

    private readonly DirectoryInfo store = Directory.CreateTempSubdirectory("mailfathom-evaluations-");

    [Fact]
    public async Task CacheOverAsync_AQuestionAskedTwice_TalliesOneAnswerAskedAfreshAndOneReadFromTheCache()
    {
        // Arrange
        var reporting = EvaluationStore.OpenUnjudgedAt(this.store.FullName, "only", []);
        var iterationName = EvaluationStore.IterationNameFor(ScriptedStructuredAnswerRun.ModelUnderTest, repetition: 1);

        // Act
        await ReachesTheModelAsync(reporting, Held);
        await ReachesTheModelAsync(reporting, Held);
        var tally = EvaluationStore.TallyOf(reporting, Held, iterationName);

        // Assert
        Assert.Equal((1, 1), (tally.Read, tally.Asked));
    }

    [Fact]
    public async Task CacheOverAsync_ARunDeclaringAReasoningEffort_AsksAgainRatherThanReadingTheAnswerCachedWithoutOne()
    {
        // Arrange
        var reporting = EvaluationStore.OpenUnjudgedAt(this.store.FullName, "only", []);
        var withoutEffort = ScriptedStructuredAnswerRun.PlanFor(ScriptedStructuredAnswerRun.ModelUnderTest);
        var withEffort = ScriptedStructuredAnswerRun.PlanFor(ScriptedStructuredAnswerRun.ModelUnderTest, "high");

        // Act
        bool[] reached =
        [
            await ReachesTheModelAsync(reporting, Held, withoutEffort),
            await ReachesTheModelAsync(reporting, Held, withEffort),
            await ReachesTheModelAsync(reporting, Held, withEffort),
            await ReachesTheModelAsync(reporting, Held, withoutEffort),
        ];

        // Assert
        Assert.Equal([true, true, false, false], reached);
    }

    [Fact]
    public async Task CacheOverAsync_ARunDeclaringAnAdditionalRequestMember_AsksAgainRatherThanReadingTheAnswerCachedWithoutOne()
    {
        // Arrange
        var reporting = EvaluationStore.OpenUnjudgedAt(this.store.FullName, "only", []);
        var withoutMember = ScriptedStructuredAnswerRun.PlanFor(ScriptedStructuredAnswerRun.ModelUnderTest);
        var withMember = Assert.Single(EvaluationDeclaration.Parse(
            $"{{MainModel: {{Model: {ScriptedStructuredAnswerRun.ModelUnderTest}, AdditionalProperties: {{top_k: 40}}}}}}",
            fallbackMainModel: null,
            address: null).ModelsFor(ChatCapability.Enrichment)).Plan;

        // Act
        bool[] reached =
        [
            await ReachesTheModelAsync(reporting, Held, withoutMember),
            await ReachesTheModelAsync(reporting, Held, withMember),
            await ReachesTheModelAsync(reporting, Held, withMember),
            await ReachesTheModelAsync(reporting, Held, withoutMember),
        ];

        // Assert
        Assert.Equal([true, true, false, false], reached);
    }

    [Fact]
    public async Task CacheOverAsync_AToolWhoseDescriptionChanged_AsksTheModelAgainRatherThanReadingTheAnswerGivenUnderTheOldOne()
    {
        // Arrange
        var reporting = EvaluationStore.OpenUnjudgedAt(this.store.FullName, "only", []);
        AITool[] before = [SearchTool("Finds mail.", SearchSchema)];
        AITool[] after = [SearchTool("Finds mail by its sender.", SearchSchema)];

        // Act
        bool[] reached =
        [
            await ReachesTheModelAsync(reporting, Held, tools: before),
            await ReachesTheModelAsync(reporting, Held, tools: before),
            await ReachesTheModelAsync(reporting, Held, tools: after),
        ];

        // Assert
        Assert.Equal([true, false, true], reached);
    }

    [Fact]
    public async Task CacheOverAsync_AToolWhoseNameChanged_AsksTheModelAgainRatherThanReadingTheAnswerGivenUnderTheOldOne()
    {
        // Arrange
        var reporting = EvaluationStore.OpenUnjudgedAt(this.store.FullName, "only", []);
        AITool[] before = [SearchTool("Finds mail.", SearchSchema)];
        AITool[] after = [SearchTool("Finds mail.", SearchSchema, name: "find_messages")];

        // Act
        bool[] reached =
        [
            await ReachesTheModelAsync(reporting, Held, tools: before),
            await ReachesTheModelAsync(reporting, Held, tools: before),
            await ReachesTheModelAsync(reporting, Held, tools: after),
        ];

        // Assert
        Assert.Equal([true, false, true], reached);
    }

    [Fact]
    public async Task CacheOverAsync_AToolWhoseParameterSchemaChanged_AsksTheModelAgainRatherThanReadingTheAnswerGivenUnderTheOldOne()
    {
        // Arrange
        var reporting = EvaluationStore.OpenUnjudgedAt(this.store.FullName, "only", []);
        AITool[] before = [SearchTool("Finds mail.", SearchSchema)];
        AITool[] after =
        [
            SearchTool("Finds mail.", """{"type":"object","properties":{"query":{"type":"string"},"limit":{"type":"integer"}}}"""),
        ];

        // Act
        bool[] reached =
        [
            await ReachesTheModelAsync(reporting, Held, tools: before),
            await ReachesTheModelAsync(reporting, Held, tools: before),
            await ReachesTheModelAsync(reporting, Held, tools: after),
        ];

        // Assert
        Assert.Equal([true, false, true], reached);
    }

    [Fact]
    public async Task CacheOverAsync_ARequestOfferedNoTools_ReadsBackTheAnswerCachedUnderTheKeyItWasFiledUnderBefore()
    {
        // Arrange
        var reporting = EvaluationStore.OpenUnjudgedAt(this.store.FullName, "only", []);
        var iterationName = EvaluationStore.IterationNameFor(ScriptedStructuredAnswerRun.ModelUnderTest, repetition: 1);
        var cache = await reporting.ResponseCacheProvider!.GetCacheAsync(Held, iterationName, TestContext.Current.CancellationToken);
        using var earlier = ScriptedStructuredAnswerRun.Model("{}");
        using var filedBefore = new DistributedCachingChatClient(earlier, cache)
        {
            CacheKeyAdditionalValues = [ScriptedStructuredAnswerRun.ModelUnderTest, string.Empty],
        };
        await filedBefore.GetResponseAsync([Question], cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var reached = await ReachesTheModelAsync(reporting, Held);

        // Assert
        Assert.False(reached);
    }

    /// <inheritdoc />
    public void Dispose() => this.store.Delete(recursive: true);

    /// <summary>Asks one scenario through the run's cache and says whether the question reached the model.</summary>
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Disposing the caching wrapper would dispose the model, which this method already disposes.")]
    private static async Task<bool> ReachesTheModelAsync(
        ReportingConfiguration reporting,
        string scenarioName,
        ChatGenerationPlan? plan = null,
        AITool[]? tools = null)
    {
        using var model = ScriptedStructuredAnswerRun.Model("{}");
        var cached = await EvaluationStore.CacheOverAsync(
            reporting,
            model,
            plan ?? ScriptedStructuredAnswerRun.PlanFor(ScriptedStructuredAnswerRun.ModelUnderTest),
            scenarioName,
            EvaluationStore.IterationNameFor(ScriptedStructuredAnswerRun.ModelUnderTest, repetition: 1),
            TestContext.Current.CancellationToken);

        await cached.GetResponseAsync(
            [Question],
            tools is null ? null : new ChatOptions { Tools = tools },
            TestContext.Current.CancellationToken);

        return model.Requests > 0;
    }

    private static ChatMessage Question => new(ChatRole.User, "What is this about?");

    private static AIFunctionDeclaration SearchTool(string description, string schema, string name = "search_mail") =>
        AIFunctionFactory.CreateDeclaration(name, description, JsonElement.Parse(schema));
}
