// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Evaluations.Enrichment;
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
    private static async Task<bool> ReachesTheModelAsync(ReportingConfiguration reporting, string scenarioName)
    {
        using var model = ScriptedStructuredAnswerRun.Model("{}");
        var cached = await EvaluationStore.CacheOverAsync(
            reporting,
            model,
            ScriptedStructuredAnswerRun.PlanFor(ScriptedStructuredAnswerRun.ModelUnderTest),
            scenarioName,
            EvaluationStore.IterationNameFor(ScriptedStructuredAnswerRun.ModelUnderTest, repetition: 1),
            TestContext.Current.CancellationToken);

        await cached.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "What is this about?")],
            cancellationToken: TestContext.Current.CancellationToken);

        return model.Requests > 0;
    }
}
