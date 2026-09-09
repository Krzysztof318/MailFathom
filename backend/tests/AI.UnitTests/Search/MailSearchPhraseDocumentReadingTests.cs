// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Search;
using MailFathom.Application.Emails.Search.Phrasing;
using Xunit;

namespace MailFathom.AI.UnitTests.Search;

/// <summary>Covers what a sentence is read as, and what a search runs as when the answer carries nothing usable.</summary>
/// <remarks>
/// Every case is a pure function of the text, so the answers a provider produces once in a thousand runs — a fenced
/// object, an impossible day, a name where an address was asked for — are ordinary examples here.
/// </remarks>
public sealed class MailSearchPhraseDocumentReadingTests
{
    [Fact]
    public void Read_AWellFormedAnswer_ReadsTheConstraintsTheCriteriaAndWhatWasLeftOver()
    {
        // Arrange
        const string answer = """
            {
              "filters": {
                "senderAddress": "sales@example.test",
                "receivedFrom": "2026-08-01",
                "receivedTo": "2026-08-31",
                "hasAttachments": true
              },
              "criteria": ["racking quotation", "delivery date"],
              "unaccounted": "urgent"
            }
            """;

        // Act
        var reading = MailSearchPhraseDocumentReading.Read(answer);

        // Assert
        Assert.True(reading.WasRead);
        Assert.Equal("sales@example.test", reading.Filters.SenderAddress);
        Assert.Equal(new DateOnly(2026, 8, 1), reading.Filters.ReceivedFrom);
        Assert.Equal(new DateOnly(2026, 8, 31), reading.Filters.ReceivedTo);
        Assert.True(reading.Filters.HasAttachments);
        Assert.False(reading.Filters.Unread);
        Assert.Equal(["racking quotation", "delivery date"], reading.Criteria);
        Assert.Equal("urgent", reading.Unaccounted);
    }

    /// <summary>A model told to answer with one object still fences it, and still writes a sentence around it.</summary>
    [Theory]
    [InlineData("""```json{"criteria": ["invoice"]}```""")]
    [InlineData("""Here is what I read: {"criteria": ["invoice"]} — I hope it helps.""")]
    public void Read_AnAnswerWrappedInSomethingElse_StillReadsTheSentence(string answer)
    {
        // Act
        var reading = MailSearchPhraseDocumentReading.Read(answer);

        // Assert
        Assert.True(reading.WasRead);
        Assert.Equal(["invoice"], reading.Criteria);
    }

    /// <summary>Nothing readable leaves the plain word search exactly as it was, which is what a deployment with no model runs.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("I cannot help with that.")]
    [InlineData("{ not json at all ")]
    [InlineData("""{"filters": {}, "criteria": [], "unaccounted": "   "}""")]
    [InlineData("""{"filters": {"unread": false}, "criteria": [""]}""")]
    public void Read_AnAnswerCarryingNoInterpretation_IsNoReadingAtAll(string? answer)
    {
        // Act
        var reading = MailSearchPhraseDocumentReading.Read(answer);

        // Assert
        Assert.False(reading.WasRead);
        Assert.Same(MailSearchPhraseReading.Nothing, reading);
    }

    /// <summary>A range that selects nothing is dropped whole, because half a contradiction narrows a search by something nobody said.</summary>
    [Fact]
    public void Read_ARangeWhoseLastDayFallsBeforeItsFirst_DropsBothDays()
    {
        // Arrange
        const string answer = """
            {"filters": {"receivedFrom": "2026-09-01", "receivedTo": "2026-08-31"}, "criteria": ["invoice"]}
            """;

        // Act
        var reading = MailSearchPhraseDocumentReading.Read(answer);

        // Assert
        Assert.Null(reading.Filters.ReceivedFrom);
        Assert.Null(reading.Filters.ReceivedTo);
        Assert.Equal(["invoice"], reading.Criteria);
    }

    /// <summary>A day written in any shape but the one the instruction asks for is one this build cannot be sure it read the same way round.</summary>
    [Theory]
    [InlineData("2026-02-30")]
    [InlineData("08/15/2026")]
    [InlineData("last Tuesday")]
    public void Read_ADayNoCalendarHolds_LeavesTheSearchUnconstrainedInTime(string written)
    {
        // Arrange
        var answer = $$"""{"filters": {"receivedFrom": "{{written}}"}, "criteria": ["invoice"]}""";

        // Act
        var reading = MailSearchPhraseDocumentReading.Read(answer);

        // Assert
        Assert.Null(reading.Filters.ReceivedFrom);
        Assert.True(reading.WasRead);
    }

    /// <summary>A name written where an address was asked for ranks rather than excludes, so it is dropped from the constraints.</summary>
    [Theory]
    [InlineData("Nordwind")]
    [InlineData("sales at example.test")]
    [InlineData("@example.test")]
    [InlineData("sales@")]
    [InlineData("sales@@example.test")]
    public void Read_ASenderThatCouldNotBeAnAddress_NarrowsNothing(string written)
    {
        // Arrange
        var answer = $$"""{"filters": {"senderAddress": "{{written}}"}, "criteria": ["invoice"]}""";

        // Act
        var reading = MailSearchPhraseDocumentReading.Read(answer);

        // Assert
        Assert.Null(reading.Filters.SenderAddress);
    }

    /// <summary>More criteria than a sentence carries is a model listing synonyms, and the ones past the bound rank nothing better.</summary>
    [Fact]
    public void Read_MoreCriteriaThanASentenceIsReadInto_KeepsTheBestOnesInOrder()
    {
        // Arrange
        var written = Enumerable
            .Range(0, MailSearchPhraseReading.MaximumCriteria + 2)
            .Select(static position => $"\"wording {position}\"");
        var answer = $$"""{"criteria": [{{string.Join(",", written)}}]}""";

        // Act
        var reading = MailSearchPhraseDocumentReading.Read(answer);

        // Assert
        Assert.Equal(MailSearchPhraseReading.MaximumCriteria, reading.Criteria.Count);
        Assert.Equal("wording 0", reading.Criteria[0]);
    }

    /// <summary>Each criterion is one object somebody takes off, so two that read the same are one of them.</summary>
    [Fact]
    public void Read_TheSameCriterionWrittenTwice_IsOneCriterion()
    {
        // Arrange
        const string answer = """{"criteria": ["invoice", "Invoice", "racking"]}""";

        // Act
        var reading = MailSearchPhraseDocumentReading.Read(answer);

        // Assert
        Assert.Equal(["invoice", "racking"], reading.Criteria);
    }

    /// <summary>Everything shown back to a person as their own words is refused rather than cut, so nothing reads as something they wrote and did not.</summary>
    [Fact]
    public void Read_ACriterionLongerThanOneMayBe_IsDroppedRatherThanCut()
    {
        // Arrange
        var overLong = new string('a', MailSearchPhraseReading.MaximumCriterionLength + 1);
        var answer = $$"""{"criteria": ["{{overLong}}", "invoice"]}""";

        // Act
        var reading = MailSearchPhraseDocumentReading.Read(answer);

        // Assert
        Assert.Equal(["invoice"], reading.Criteria);
    }

    [Fact]
    public void Read_AnUnaccountedPartLongerThanASentence_IsDroppedRatherThanQuotedBack()
    {
        // Arrange
        var overLong = new string('a', MailSearchPhraseReading.MaximumUnaccountedLength + 1);
        var answer = $$"""{"criteria": ["invoice"], "unaccounted": "{{overLong}}"}""";

        // Act
        var reading = MailSearchPhraseDocumentReading.Read(answer);

        // Assert
        Assert.Null(reading.Unaccounted);
        Assert.Equal(["invoice"], reading.Criteria);
    }

    /// <summary>A sentence that is all constraint is a reading, although it ranks by nothing of its own.</summary>
    [Fact]
    public void Read_AnAnswerOfConstraintsAlone_IsStillAReading()
    {
        // Arrange
        const string answer = """{"filters": {"unread": true}, "criteria": []}""";

        // Act
        var reading = MailSearchPhraseDocumentReading.Read(answer);

        // Assert
        Assert.True(reading.WasRead);
        Assert.True(reading.Filters.Unread);
        Assert.Empty(reading.Criteria);
    }

    /// <summary>Reading is a function of the text alone, which is what makes an interpretation reproducible.</summary>
    [Fact]
    public void Read_TheSameAnswerTwice_ProducesTheSameReading()
    {
        // Arrange
        const string answer = """
            {"filters": {"flagged": true, "receivedFrom": "2026-08-01"}, "criteria": ["delivery"], "unaccounted": "soon"}
            """;

        // Act
        var first = MailSearchPhraseDocumentReading.Read(answer);
        var second = MailSearchPhraseDocumentReading.Read(answer);

        // Assert
        Assert.Equal(first.Filters, second.Filters);
        Assert.Equal(first.Criteria, second.Criteria);
        Assert.Equal(first.Unaccounted, second.Unaccounted);
    }
}
