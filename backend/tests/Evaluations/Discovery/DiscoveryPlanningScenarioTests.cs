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
    [InlineData("TracksAChange", """{"intent":"trackChange","sufficientPassages":6,"lookups":[{"queryText":"office move date"}]}""", true)]
    [InlineData("TracksAChange", """{"intent":"findFact","sufficientPassages":6,"lookups":[{"queryText":"office move date"}]}""", false)]
    [InlineData("ComparesOffers", """{"intent":"compareTerms","sufficientPassages":6,"lookups":[{"queryText":"removal quote"}]}""", true)]
    [InlineData("ComparesOffers", """{"intent":"findDocuments","sufficientPassages":6,"lookups":[{"queryText":"removal quote"}]}""", false)]
    [InlineData("LooksForFiles", """{"intent":"findDocuments","sufficientPassages":3,"lookups":[{"queryText":"floor plan","hasAttachments":true}]}""", true)]
    [InlineData("LooksForFiles", """{"intent":"findFact","sufficientPassages":3,"lookups":[{"queryText":"floor plan"}]}""", false)]
    [InlineData("NamesOneOfTwoSimilarPeople", """{"intent":"findFact","sufficientPassages":3,"lookups":[{"queryText":"Solheim archive boxes -Solberg"}]}""", true)]
    [InlineData("NamesOneOfTwoSimilarPeople", """{"intent":"findFact","sufficientPassages":3,"lookups":[{"queryText":"Ingrid archive boxes"}]}""", false)]
    [InlineData("NamesOneOfTwoSimilarPeople", """{"intent":"findFact","sufficientPassages":3,"lookups":[{"queryText":"Solheim archive boxes"},{"queryText":"Ingrid Solberg boxes"}]}""", false)]
    [InlineData("NamesAQuotedSpeaker", """{"intent":"findFact","sufficientPassages":3,"lookups":[{"queryText":"Horváth goods lift"}]}""", true)]
    [InlineData("NamesAQuotedSpeaker", """{"intent":"findFact","sufficientPassages":3,"lookups":[{"queryText":"goods lift","senderAddress":"pal.horvath@example.com"}]}""", false)]
    [InlineData("StatesARecipient", """{"intent":"findFact","sufficientPassages":3,"lookups":[{"queryText":"VPN certificate","recipientAddress":"IT-Desk@Tidewater.example"}]}""", true)]
    [InlineData("StatesARecipient", """{"intent":"findFact","sufficientPassages":3,"lookups":[{"queryText":"VPN certificate","senderAddress":"it-desk@tidewater.example"}]}""", false)]
    [InlineData("NamesADateRange", """{"intent":"findDocuments","sufficientPassages":5,"lookups":[{"queryText":"Brightwater invoice","receivedOnOrAfter":"2026-08-01T00:00:00+02:00","receivedBefore":"2026-08-16T00:00:00+02:00"}]}""", true)]
    [InlineData("NamesADateRange", """{"intent":"findDocuments","sufficientPassages":5,"lookups":[{"queryText":"Brightwater invoice","receivedOnOrAfter":"2026-08-01T00:00:00Z","receivedBefore":"2026-09-01T00:00:00Z"}]}""", false)]
    [InlineData("AsksForFlagAndReadState", """{"intent":"findFact","sufficientPassages":5,"lookups":[{"queryText":"movers","isRemotelyFlagged":true,"isRemotelySeen":false}]}""", true)]
    [InlineData("AsksForFlagAndReadState", """{"intent":"findFact","sufficientPassages":5,"lookups":[{"queryText":"movers starred unread"}]}""", false)]
    [InlineData("NoScope", "I would look for the venue confirmation.", false)]
    [InlineData("Polish.ExplicitScope", """{"intent":"findFact","sufficientPassages":5,"lookups":[{"queryText":"nieopłacone faktury","senderAddress":"Billing@Northwind.example"}]}""", true)]
    [InlineData("Polish.ExplicitScope", """{"intent":"findFact","sufficientPassages":5,"lookups":[{"queryText":"nieopłacone faktury Northwind"}]}""", false)]
    [InlineData("Polish.NoScope", """{"intent":"findFact","sufficientPassages":3,"lookups":[{"queryText":"miejsce wyjazdu integracyjnego"}]}""", true)]
    [InlineData("Polish.NoScope", """{"intent":"findFact","sufficientPassages":3,"lookups":[{"queryText":"miejsce wyjazdu","receivedOnOrAfter":"2026-01-01T00:00:00Z"}]}""", false)]
    [InlineData("Polish.NamesPerson", """{"intent":"findFact","sufficientPassages":4,"lookups":[{"queryText":"Agnieszka Dąbrowska błąd eksportu"}]}""", true)]
    [InlineData("Polish.NamesPerson", """{"intent":"findFact","sufficientPassages":4,"lookups":[{"queryText":"błąd eksportu","senderAddress":"agnieszka.dabrowska@serwisownia.test"}]}""", false)]
    [InlineData("Polish.NamesPeriod", """{"intent":"findFact","sufficientPassages":4,"lookups":[{"queryText":"podwyżka czynszu","receivedOnOrAfter":"2026-03-01T00:00:00+01:00","receivedBefore":"2026-04-01T00:00:00+02:00"}]}""", true)]
    [InlineData("Polish.NamesPeriod", """{"intent":"findFact","sufficientPassages":4,"lookups":[{"queryText":"podwyżka czynszu marzec 2026"}]}""", false)]
    [InlineData("Polish.OutsideWhatARunCanDo", """{"intent":"unclassified","sufficientPassages":3,"lookups":[{"queryText":"oferta Tomasz"}]}""", true)]
    [InlineData("Polish.OutsideWhatARunCanDo", """{"intent":"findFact","sufficientPassages":3,"lookups":[{"queryText":"oferta Tomasz"}]}""", false)]
    [InlineData("Polish.TracksAChange", """{"intent":"trackChange","sufficientPassages":6,"lookups":[{"queryText":"termin przeprowadzki biura"}]}""", true)]
    [InlineData("Polish.TracksAChange", """{"intent":"findFact","sufficientPassages":6,"lookups":[{"queryText":"termin przeprowadzki biura"}]}""", false)]
    [InlineData("Polish.LooksForFiles", """{"intent":"findDocuments","sufficientPassages":3,"lookups":[{"queryText":"plan piętra","hasAttachments":true}]}""", true)]
    [InlineData("Polish.LooksForFiles", """{"intent":"findFact","sufficientPassages":3,"lookups":[{"queryText":"plan piętra"}]}""", false)]
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
