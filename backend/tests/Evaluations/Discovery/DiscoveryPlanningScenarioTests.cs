// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Evaluations.Costing;
using MailFathom.Evaluations.StructuredAnswers;
using Microsoft.Extensions.AI.Evaluation;
using Xunit;

namespace MailFathom.Evaluations.Discovery;

/// <summary>
/// Proves, without calling any provider, that each case holds a plan to what its question stated and that the judge is
/// asked about an ambiguous question alone.
/// </summary>
/// <remarks>Free, and therefore not gated on the run switch.</remarks>
public sealed class DiscoveryPlanningScenarioTests : IDisposable
{
    private readonly ScriptedStructuredAnswerRun run = new();

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
        using var model = ScriptedStructuredAnswerRun.Model(answer);

        // Act
        var outcome = await this.run.RunAsync(DiscoveryPlanningScenario.RequestFor(DiscoveryPlanningCase.Named(caseName)), model);

        // Assert
        var metric = outcome.Verdict.Get<BooleanMetric>(DiscoveryPlanningScenario.ExpectationMetricName);

        Assert.Equal(expected, metric.Value);
        Assert.Equal(expected, outcome.Shortfall is null);
    }

    [Fact]
    public async Task RunAsync_AQuestionWithOneRightReading_AsksTheJudgeNothing()
    {
        // Arrange
        using var model = ScriptedStructuredAnswerRun.Model("""{"intent":"findFact","sufficientPassages":3,"lookups":[{"queryText":"offsite venue"}]}""");

        // Act
        var outcome = await this.run.RunAsync(DiscoveryPlanningScenario.RequestFor(DiscoveryPlanningCase.Named("NoScope")), model);

        // Assert
        Assert.Equal(0, this.run.Judge.Requests);
        Assert.False(outcome.Verdict.Metrics.ContainsKey(StructuredAnswerScenario.IntentResolutionMetricName));
    }

    [Fact]
    public async Task RunAsync_AnAmbiguousQuestion_FilesTheJudgesIntentResolutionBesideTheModel()
    {
        // Arrange
        using var model = ScriptedStructuredAnswerRun.Model("""{"intent":"findDocuments","sufficientPassages":4,"lookups":[{"queryText":"Q3 figures","hasAttachments":true}]}""");

        // Act
        var outcome = await this.run.RunAsync(
            DiscoveryPlanningScenario.RequestFor(DiscoveryPlanningCase.Named("AmbiguousDocumentOrFact")),
            model);

        // Assert
        var resolution = outcome.Verdict.Get<NumericMetric>(StructuredAnswerScenario.IntentResolutionMetricName);

        Assert.Equal((1, 5d), (this.run.Judge.Requests, resolution.Value));
        Assert.Equal(ScriptedStructuredAnswerRun.ModelUnderTest, outcome.Model);
        Assert.NotNull(outcome.Verdict.Get<NumericMetric>(EvaluationCost.MetricName));
        Assert.Empty(StructuredAnswerScenario.ShortfallsOf(outcome));
    }

    public void Dispose() => this.run.Dispose();
}
