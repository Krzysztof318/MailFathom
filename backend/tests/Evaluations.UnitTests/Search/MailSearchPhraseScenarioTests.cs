// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Evaluations.Search;
using MailFathom.Evaluations.StructuredAnswers;
using MailFathom.Evaluations.UnitTests.StructuredAnswers;
using Microsoft.Extensions.AI.Evaluation;
using Xunit;

namespace MailFathom.Evaluations.UnitTests.Search;

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
    [InlineData("Yesterday", """{"filters":{"receivedFrom":"2026-09-13","receivedTo":"2026-09-13"},"criteria":["delivery note"]}""", true)]
    [InlineData("Yesterday", """{"filters":{"receivedFrom":"2026-09-13","receivedTo":"2026-09-14"},"criteria":["delivery note"]}""", false)]
    [InlineData("SinceStartOfMonth", """{"filters":{"receivedFrom":"2026-09-01"},"criteria":["office move"]}""", true)]
    [InlineData("SinceStartOfMonth", """{"filters":{"receivedFrom":"2026-09-01","receivedTo":"2026-09-14"},"criteria":["office move"]}""", true)]
    [InlineData("SinceStartOfMonth", """{"filters":{"receivedFrom":"2026-08-14"},"criteria":["office move"]}""", false)]
    [InlineData("TwoSenders", """{"criteria":["renewal","northwind","fabrikam"]}""", true)]
    [InlineData("TwoSenders", """{"filters":{"senderAddress":["billing@northwind.example","accounts@fabrikam.example"]},"criteria":["renewal"]}""", true)]
    [InlineData("TwoSenders", """{"filters":{"senderAddress":"billing@northwind.example"},"criteria":["renewal"]}""", false)]
    [InlineData("SenderAndWords", """{"filters":{"senderAddress":"support@contoso.example","unread":true},"criteria":["password reset"]}""", true)]
    [InlineData("SenderAndWords", """{"filters":{"senderAddress":"support@contoso.example"},"criteria":["password reset"]}""", false)]
    [InlineData("SenderAndWords", """{"filters":{"senderAddress":"support@contoso.example","unread":true}}""", false)]
    [InlineData("Recipient", """{"filters":{"recipientAddress":"legal@fabrikam.example"},"criteria":["NDA"]}""", true)]
    [InlineData("Recipient", """{"filters":{"senderAddress":"legal@fabrikam.example"},"criteria":["NDA"]}""", false)]
    [InlineData("Flagged", """{"filters":{"flagged":true},"criteria":["office lease"]}""", true)]
    [InlineData("Flagged", """{"filters":{"unread":true},"criteria":["office lease"]}""", false)]
    [InlineData("PersonWithoutAddress", """{"criteria":["Ingrid","travel budget"]}""", true)]
    [InlineData("PersonWithoutAddress", """{"filters":{"senderAddress":"ingrid@example.test"},"criteria":["travel budget"]}""", false)]
    [InlineData("AttachmentInMonth", """{"filters":{"hasAttachments":true,"receivedFrom":"2026-07-01","receivedTo":"2026-07-31"},"criteria":["audit"]}""", true)]
    [InlineData("AttachmentInMonth", """{"filters":{"receivedFrom":"2026-07-01","receivedTo":"2026-07-31"},"criteria":["audit PDF"]}""", false)]
    [InlineData("Polish.Sender", """{"filters":{"senderAddress":"Billing@Northwind.example"},"criteria":["faktura"]}""", true)]
    [InlineData("Polish.Sender", """{"filters":{"senderAddress":"Billing@Northwind.example"},"criteria":["invoice"]}""", false)]
    [InlineData("Polish.Period", """{"filters":{"receivedFrom":"2026-08-01","receivedTo":"2026-08-31"},"criteria":["umowa"]}""", true)]
    [InlineData("Polish.Period", """{"filters":{"receivedFrom":"2026-08-01","receivedTo":"2026-08-31"},"criteria":["contract draft"]}""", false)]
    [InlineData("Polish.Unread", """{"filters":{"unread":true},"criteria":["awaria serwera"]}""", true)]
    [InlineData("Polish.Unread", """{"filters":{"unread":true},"criteria":["server outage"]}""", false)]
    [InlineData("Polish.Yesterday", """{"filters":{"receivedFrom":"2026-09-13","receivedTo":"2026-09-13"},"criteria":["dokumenty dostawy"]}""", true)]
    [InlineData("Polish.Yesterday", """{"filters":{"receivedFrom":"2026-09-13","receivedTo":"2026-09-14"},"criteria":["dokumenty dostawy"]}""", false)]
    [InlineData("Polish.WordsOnly", """{"criteria":["wycena remontu kuchni"]}""", true)]
    [InlineData("Polish.WordsOnly", """{"criteria":["kitchen renovation quote"]}""", false)]
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
