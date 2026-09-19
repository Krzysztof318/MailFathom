// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Evaluations.Costing;
using MailFathom.Evaluations.Enrichment;
using MailFathom.Evaluations.StructuredAnswers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Reporting.Storage;
using Xunit;

namespace MailFathom.Evaluations.Reporting;

/// <summary>Proves a case is asked once per declared repetition, each filed and cached on its own, and held to the share that passed.</summary>
/// <remarks>
/// Free: the model is scripted, and the store is written to a temporary directory for the reason
/// <see cref="EmailEnrichmentScenarioTests" /> gives, since the share a case reports is what the store files.
/// </remarks>
public sealed class EvaluationRepetitionsTests : IDisposable
{
    private const string Scenario = "Scenario.Repeated";

    private const string Model = ScriptedStructuredAnswerRun.ModelUnderTest;

    private readonly DirectoryInfo store = Directory.CreateTempSubdirectory("mailfathom-evaluations-");

    [Fact]
    public void Parse_NothingDeclared_AsksEachCaseOnce()
    {
        // Act
        var repetitions = EvaluationRepetitions.Parse(declared: null);

        // Assert
        Assert.Equal(1, repetitions);
    }

    [Fact]
    public void Parse_AWholeNumber_AsksEachCaseThatManyTimes()
    {
        // Act
        var repetitions = EvaluationRepetitions.Parse(" 5 ");

        // Assert
        Assert.Equal(5, repetitions);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-2")]
    [InlineData("21")]
    [InlineData("three")]
    [InlineData("2.5")]
    public void Parse_NoCountTheRunAccepts_FailsNamingTheVariable(string declared)
    {
        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => EvaluationRepetitions.Parse(declared));

        // Assert
        Assert.Contains(EvaluationRepetitions.RepetitionsVariable, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IterationNameFor_TheFirstRepetition_IsTheModelsNameAlone()
    {
        // Act
        var first = EvaluationStore.IterationNameFor("vendor/model", repetition: 1);
        var third = EvaluationStore.IterationNameFor("vendor/model", repetition: 3);

        // Assert
        Assert.Equal(("vendor_model", "vendor_model#3"), (first, third));
    }

    [Fact]
    public async Task MeasureAsync_OneRepetitionFallsShort_NamesTheCaseTheModelAndHowManyOfHowMany()
    {
        // Arrange
        using var model = ScriptedStructuredAnswerRun.Model("{}");

        // Act
        var shortfalls = await this.MeasureAsync("only", model, fallingShort: [2], repetitions: 3);

        // Assert
        Assert.Equal(
            [$"{Scenario} under {Model}: 1 of 3 repetition(s) fell short.", "  repetition 2: missed the point"],
            shortfalls);
    }

    [Fact]
    public async Task MeasureAsync_EveryRepetitionPasses_NamesNoShortfallAndFilesTheShareInEachRepetition()
    {
        // Arrange
        using var model = ScriptedStructuredAnswerRun.Model("{}");

        // Act
        var shortfalls = await this.MeasureAsync("only", model, fallingShort: [], repetitions: 2);
        var shares = await this.FiledAsync("only", 2, EvaluationRepetitions.PassingShareMetricName);

        // Assert
        Assert.Empty(shortfalls);
        Assert.All(shares, static share => Assert.Equal((1d, false), (share.Value, share.Interpretation!.Failed)));
    }

    [Fact]
    public async Task MeasureAsync_OneRepetitionFallsShort_FilesTheShareThatPassedAsFailedInEachRepetition()
    {
        // Arrange
        using var model = ScriptedStructuredAnswerRun.Model("{}");

        // Act
        await this.MeasureAsync("only", model, fallingShort: [1], repetitions: 4);
        var shares = await this.FiledAsync("only", 4, EvaluationRepetitions.PassingShareMetricName);

        // Assert
        Assert.Equal(4, shares.Count);
        Assert.All(shares, static share => Assert.Equal((0.75d, true), (share.Value, share.Interpretation!.Failed)));
    }

    [Fact]
    public async Task MeasureAsync_ARepeatedRunOverAHeldCase_ReadsEveryRepetitionBackFromTheCache()
    {
        // Arrange
        using var model = ScriptedStructuredAnswerRun.Model("{}");
        await this.MeasureAsync("first", model, fallingShort: [], repetitions: 3);

        // Act
        await this.MeasureAsync("second", model, fallingShort: [], repetitions: 3);

        // Assert
        Assert.Equal(3, model.Requests);
    }

    [Fact]
    public async Task MeasureAsync_ACaseThatFellShort_AsksEveryRepetitionAgainOnTheNextRun()
    {
        // Arrange
        using var model = ScriptedStructuredAnswerRun.Model("{}");
        await this.MeasureAsync("first", model, fallingShort: [3], repetitions: 3);

        // Act
        await this.MeasureAsync("second", model, fallingShort: [], repetitions: 3);

        // Assert
        Assert.Equal(6, model.Requests);
    }

    [Fact]
    public async Task MeasureAsync_RepetitionsThatPaid_FilesWhatTheyPaidTogether()
    {
        // Arrange
        using var model = ScriptedStructuredAnswerRun.Model("{}");
        PaidUsage[] spend = [new(1, 100, 10, 0.01m), new(1, 100, 10, 0.02m), default];

        // Act
        await this.MeasureAsync("only", model, fallingShort: [], repetitions: 3, spend);
        var costs = await this.FiledAsync("only", 3, EvaluationCost.RepetitionsMetricName);

        // Assert
        Assert.All(costs, static cost => Assert.Equal(0.03d, cost.Value!.Value, precision: 10));
    }

    [Fact]
    public async Task MeasureAsync_ARepetitionThatPaidWithNoCharge_LeavesTheTotalUnstated()
    {
        // Arrange
        using var model = ScriptedStructuredAnswerRun.Model("{}");
        PaidUsage[] spend = [new(1, 100, 10, 0.01m), new(1, 100, 10, null)];

        // Act
        await this.MeasureAsync("only", model, fallingShort: [], repetitions: 2, spend);
        var costs = await this.FiledAsync("only", 2, EvaluationCost.RepetitionsMetricName);

        // Assert
        Assert.All(costs, static cost => Assert.Null(cost.Value));
    }

    /// <inheritdoc />
    public void Dispose() => this.store.Delete(recursive: true);

    /// <summary>Asks the case through the store's cache once per repetition, falling short on the numbered ones.</summary>
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Disposing the caching wrapper would dispose the model, which the test disposes.")]
    private async Task<IReadOnlyList<string>> MeasureAsync(
        string executionName,
        ScriptedChatClient model,
        int[] fallingShort,
        int repetitions,
        PaidUsage[]? spend = null)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var reporting = EvaluationStore.OpenUnjudgedAt(this.store.FullName, executionName, []);
        var plan = ScriptedStructuredAnswerRun.PlanFor(Model);
        ChatMessage[] question = [new(ChatRole.User, "What is this about?")];

        return await EvaluationRepetitions.MeasureAsync(
            reporting,
            Scenario,
            Model,
            repetitions,
            async repetition =>
            {
                var iterationName = EvaluationStore.IterationNameFor(Model, repetition);

                await using var scenarioRun = await reporting.CreateScenarioRunAsync(
                    Scenario,
                    iterationName,
                    cancellationToken: cancellationToken);

                var cached = await EvaluationStore.CacheOverAsync(reporting, model, plan, Scenario, iterationName, cancellationToken);
                var answer = await cached.GetResponseAsync(question, cancellationToken: cancellationToken);
                var verdict = await scenarioRun.EvaluateAsync(question, answer, cancellationToken: cancellationToken);

                EvaluationCost.Record(verdict, Model, spend?[repetition - 1] ?? default, judgeSpend: default);

                return fallingShort.Contains(repetition) ? ["missed the point"] : [];
            },
            cancellationToken);
    }

    /// <summary>Reads one metric from every repetition the store filed under a run.</summary>
    private async Task<IReadOnlyList<NumericMetric>> FiledAsync(string executionName, int repetitions, string metricName)
    {
        var resultStore = new DiskBasedResultStore(this.store.FullName);
        List<NumericMetric> metrics = [];

        for (var repetition = 1; repetition <= repetitions; repetition++)
        {
            await foreach (var result in resultStore.ReadResultsAsync(
                executionName,
                Scenario,
                EvaluationStore.IterationNameFor(Model, repetition),
                TestContext.Current.CancellationToken))
            {
                metrics.Add(result.EvaluationResult.Get<NumericMetric>(metricName));
            }
        }

        return metrics;
    }
}
