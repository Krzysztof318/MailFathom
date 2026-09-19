// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Evaluations.StructuredAnswers;
using Microsoft.Extensions.AI.Evaluation;
using Xunit;

namespace MailFathom.Evaluations.Search;

/// <summary>
/// Proves, without calling any provider, that each case holds a reading to the filter its sentence stated and that the
/// judge is asked about the sentence that reads two ways alone.
/// </summary>
public sealed class MailSearchPhraseScenarioTests : IDisposable
{
    private readonly ScriptedStructuredAnswerRun run = new();

    [Theory]
    [InlineData("Sender", """{"filters":{"senderAddress":"Billing@Northwind.example"},"criteria":["invoice"]}""", true)]
    [InlineData("Sender", """{"criteria":["northwind invoice"]}""", false)]
    [InlineData("Sender", """{"filters":{"senderAddress":"billing@northwind.example","hasAttachments":true},"criteria":["invoice"]}""", false)]
    [InlineData("Period", """{"filters":{"receivedFrom":"2026-08-01","receivedTo":"2026-08-31"},"criteria":["contract draft"]}""", true)]
    [InlineData("Period", """{"filters":{"receivedFrom":"2026-08-01"},"criteria":["contract draft"]}""", false)]
    [InlineData("Attachment", """{"filters":{"hasAttachments":true},"criteria":["Q3 budget spreadsheet"]}""", true)]
    [InlineData("Attachment", """{"criteria":["Q3 budget spreadsheet"]}""", false)]
    [InlineData("Unread", """{"filters":{"unread":true},"criteria":["server outage"]}""", true)]
    [InlineData("Unread", """{"filters":{"unread":true}}""", false)]
    [InlineData("WordsOnly", """{"criteria":["kitchen renovation quote"]}""", true)]
    [InlineData("WordsOnly", """{"filters":{"flagged":true},"criteria":["kitchen renovation quote"]}""", false)]
    [InlineData("WordsOnly", """{"filters":{},"criteria":[]}""", false)]
    public async Task RunAsync_AReadingForACase_RecordsWhetherItHoldsTheFiltersTheSentenceStated(
        string caseName,
        string answer,
        bool expected)
    {
        // Arrange
        using var model = ScriptedStructuredAnswerRun.Model(answer);

        // Act
        var outcome = await this.run.RunAsync(MailSearchPhraseScenario.RequestFor(MailSearchPhraseCase.Named(caseName)), model);

        // Assert
        var metric = outcome.Verdict.Get<BooleanMetric>(MailSearchPhraseScenario.ExpectationMetricName);

        Assert.Equal((expected, expected), (metric.Value, outcome.Shortfall is null));
        Assert.Equal(0, this.run.Judge.Requests);
    }

    [Fact]
    public async Task RunAsync_ASentenceThatReadsTwoWays_FilesTheJudgesIntentResolution()
    {
        // Arrange
        using var model = ScriptedStructuredAnswerRun.Model("""{"filters":{"unread":true},"criteria":["invoice"]}""");

        // Act
        var outcome = await this.run.RunAsync(
            MailSearchPhraseScenario.RequestFor(MailSearchPhraseCase.Named("OutstandingOrUnread")),
            model);

        // Assert
        var resolution = outcome.Verdict.Get<NumericMetric>(StructuredAnswerScenario.IntentResolutionMetricName);

        Assert.Equal((1, 5d), (this.run.Judge.Requests, resolution.Value));
        Assert.Empty(StructuredAnswerScenario.ShortfallsOf(outcome));
    }

    public void Dispose() => this.run.Dispose();
}
