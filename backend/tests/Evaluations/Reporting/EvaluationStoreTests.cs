// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using MailFathom.AI.Chat;
using MailFathom.Evaluations.Enrichment;
using MailFathom.Evaluations.Providers;
using MailFathom.Evaluations.StructuredAnswers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation.Reporting;
using Xunit;

namespace MailFathom.Evaluations.Reporting;

/// <summary>Proves a scenario a model fell short on is asked again, and that every other scenario stays paid for.</summary>
/// <remarks>
/// Free: the model is scripted, and the store is written to a temporary directory for the reason
/// <see cref="EmailEnrichmentScenarioTests" /> gives, since what a later run reads back is what the store kept.
/// </remarks>
public sealed class EvaluationStoreTests : IDisposable
{
    private const string FellShort = "Scenario.FellShort";

    private const string Held = "Scenario.Held";

    private const string SearchSchema = """{"type":"object","properties":{"query":{"type":"string"}}}""";

    private readonly DirectoryInfo store = Directory.CreateTempSubdirectory("mailfathom-evaluations-");

    [Fact]
    public async Task ForgetAsync_AScenarioAnsweredOnAnEarlierRun_AsksTheModelAgainAndKeepsTheOtherScenarioCached()
    {
        // Arrange
        await this.AskEveryScenarioAsync("earlier", forget: null);

        // Act
        await this.AskEveryScenarioAsync("retry", forget: FellShort);
        var later = await this.AskEveryScenarioAsync("later", forget: null);

        // Assert
        Assert.Equal([FellShort], later);
    }

    [Fact]
    public async Task ForgetAsync_AScenarioAnsweredOnThisRun_AsksTheModelAgainOnTheNextAndKeepsTheOtherScenarioCached()
    {
        // Arrange
        await this.AskEveryScenarioAsync("first", forget: FellShort);

        // Act
        var next = await this.AskEveryScenarioAsync("next", forget: null);

        // Assert
        Assert.Equal([FellShort], next);
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

    /// <summary>Asks both scenarios in one run, forgetting one if named, and names the scenarios that reached the model.</summary>
    private async Task<IReadOnlyList<string>> AskEveryScenarioAsync(string executionName, string? forget)
    {
        var reporting = EvaluationStore.OpenUnjudgedAt(this.store.FullName, executionName, []);
        List<string> reached = [];

        foreach (var scenarioName in new[] { FellShort, Held })
        {
            if (await ReachesTheModelAsync(reporting, scenarioName))
            {
                reached.Add(scenarioName);
            }
        }

        if (forget is not null)
        {
            await EvaluationStore.ForgetAsync(
                reporting,
                forget,
                EvaluationStore.IterationNameFor(ScriptedStructuredAnswerRun.ModelUnderTest, repetition: 1),
                TestContext.Current.CancellationToken);
        }

        return reached;
    }

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
