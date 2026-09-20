// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.CalendarEvents;
using MailFathom.Application.Calendar.Extraction;
using Xunit;

namespace MailFathom.AI.UnitTests.CalendarEvents;

/// <summary>Covers what survives being read out of an agent's answer, and what falls away.</summary>
/// <remarks>
/// Every case here is a shape a provider actually produces — a fence, a prose preface, a zone offset nobody asked
/// for, a reversed span — and every one of them is a pure function of the answer and the instant it is anchored to,
/// which is what makes them examples rather than something only a live endpoint reaches.
/// </remarks>
public sealed class CalendarEventExtractionReadingTests
{
    private static readonly DateTimeOffset Anchor = new(2026, 9, 21, 9, 30, 0, TimeSpan.FromHours(2));

    [Fact]
    public void Read_AnAnswerNamingOneEvent_KeepsItInTheOffsetTheTextWasWrittenIn()
    {
        // Arrange
        const string answer = """
            { "events": [ { "title": "Racking survey", "start": "2026-09-24T10:00", "end": "2026-09-24T11:30" } ] }
            """;

        // Act
        var events = CalendarEventExtractionReading.Read(answer, Anchor, CalendarEventExtraction.MaximumEvents);

        // Assert
        var read = Assert.Single(events);
        Assert.Equal("Racking survey", read.Title.Value);
        Assert.Equal(new DateTimeOffset(2026, 9, 24, 10, 0, 0, TimeSpan.FromHours(2)), read.Start);
        Assert.Equal(new DateTimeOffset(2026, 9, 24, 11, 30, 0, TimeSpan.FromHours(2)), read.End);
    }

    /// <summary>A model told to answer with one object still fences it and writes a sentence around it often enough.</summary>
    [Fact]
    public void Read_AnAnswerFencedAndPrefacedWithProse_ReadsTheObjectInsideIt()
    {
        // Arrange
        const string answer = """
            Here is what I found:
            ```json
            { "events": [ { "title": "Site visit", "start": "2026-09-24T10:00" } ] }
            ```
            Let me know if that helps.
            """;

        // Act
        var events = CalendarEventExtractionReading.Read(answer, Anchor, CalendarEventExtraction.MaximumEvents);

        // Assert
        Assert.Equal("Site visit", Assert.Single(events).Title.Value);
    }

    /// <summary>An event with no stated length is the ordinary case, and carries no end rather than a guessed one.</summary>
    [Fact]
    public void Read_AnEventWithNoEnd_KeepsItWithoutOne()
    {
        // Arrange
        const string answer = """{ "events": [ { "title": "Site visit", "start": "2026-09-24T10:00" } ] }""";

        // Act
        var events = CalendarEventExtractionReading.Read(answer, Anchor, CalendarEventExtraction.MaximumEvents);

        // Assert
        Assert.Null(Assert.Single(events).End);
    }

    /// <summary>What the text fixed was when the thing begins, so a length that read wrongly is the part to discard.</summary>
    [Theory]
    [InlineData("2026-09-24T10:00")]
    [InlineData("2026-09-24T09:00")]
    public void Read_AnEndAtOrBeforeItsStart_KeepsTheEventWithoutTheEnd(string end)
    {
        // Arrange
        var answer = $$"""
            { "events": [ { "title": "Site visit", "start": "2026-09-24T10:00", "end": "{{end}}" } ] }
            """;

        // Act
        var events = CalendarEventExtractionReading.Read(answer, Anchor, CalendarEventExtraction.MaximumEvents);

        // Assert
        var read = Assert.Single(events);
        Assert.Null(read.End);
        Assert.Equal(new DateTimeOffset(2026, 9, 24, 10, 0, 0, TimeSpan.FromHours(2)), read.Start);
    }

    /// <summary>A model that supplied an offset guessed it, and a guessed zone moves the event by whole hours.</summary>
    [Theory]
    [InlineData("2026-09-24T10:00Z")]
    [InlineData("2026-09-24T10:00+05:00")]
    [InlineData("2026-09-24")]
    [InlineData("24 September 2026, 10am")]
    [InlineData("")]
    public void Read_AStartWrittenInAFormTheInstructionDidNotAskFor_DropsTheEvent(string start)
    {
        // Arrange
        var answer = $$"""{ "events": [ { "title": "Site visit", "start": "{{start}}" } ] }""";

        // Act
        var events = CalendarEventExtractionReading.Read(answer, Anchor, CalendarEventExtraction.MaximumEvents);

        // Assert
        Assert.Empty(events);
    }

    /// <summary>Seconds say nothing about whether the day was understood, so they are the one departure admitted.</summary>
    [Fact]
    public void Read_AStartCarryingSeconds_KeepsTheEvent()
    {
        // Arrange
        const string answer = """{ "events": [ { "title": "Site visit", "start": "2026-09-24T10:00:00" } ] }""";

        // Act
        var events = CalendarEventExtractionReading.Read(answer, Anchor, CalendarEventExtraction.MaximumEvents);

        // Assert
        Assert.Equal(new DateTimeOffset(2026, 9, 24, 10, 0, 0, TimeSpan.FromHours(2)), Assert.Single(events).Start);
    }

    /// <summary>A title is drawn in a list beside the other events, so one that would end its row is not one.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Site\nvisit")]
    [InlineData("Site‮visit")]
    public void Read_AnEventWhoseTitleIsNotATitle_DropsTheEvent(string title)
    {
        // Arrange
        var answer = $$"""{ "events": [ { "title": "{{title}}", "start": "2026-09-24T10:00" } ] }""";

        // Act
        var events = CalendarEventExtractionReading.Read(answer, Anchor, CalendarEventExtraction.MaximumEvents);

        // Assert
        Assert.Empty(events);
    }

    /// <summary>One unusable event does not take the ones beside it, which is what dropping rather than failing means.</summary>
    [Fact]
    public void Read_AnAnswerMixingAUsableEventWithAnUnusableOne_KeepsTheUsableOne()
    {
        // Arrange
        const string answer = """
            {
              "events": [
                { "title": "Site visit", "start": "not a date" },
                { "title": "Racking survey", "start": "2026-09-24T10:00" }
              ]
            }
            """;

        // Act
        var events = CalendarEventExtractionReading.Read(answer, Anchor, CalendarEventExtraction.MaximumEvents);

        // Assert
        Assert.Equal("Racking survey", Assert.Single(events).Title.Value);
    }

    /// <summary>The bound is the reading's rather than the model's, so an answer past it is cut instead of refused.</summary>
    [Fact]
    public void Read_AnAnswerNamingMoreEventsThanTheBoundAllows_KeepsTheLeadingOnes()
    {
        // Arrange
        var written = string.Join(
            ",",
            Enumerable.Range(0, CalendarEventExtraction.MaximumEvents + 3).Select(ordinal =>
                $$"""{ "title": "Visit {{ordinal}}", "start": "2026-09-24T10:00" }"""));
        var answer = $$"""{ "events": [ {{written}} ] }""";

        // Act
        var events = CalendarEventExtractionReading.Read(answer, Anchor, CalendarEventExtraction.MaximumEvents);

        // Assert
        Assert.Equal(CalendarEventExtraction.MaximumEvents, events.Count);
        Assert.Equal("Visit 0", events[0].Title.Value);
    }

    /// <summary>The description half keeps one event, because somebody describing a meeting is describing one.</summary>
    [Fact]
    public void Read_AnAnswerNamingSeveralEventsUnderABoundOfOne_KeepsTheFirst()
    {
        // Arrange
        const string answer = """
            {
              "events": [
                { "title": "Site visit", "start": "2026-09-24T10:00" },
                { "title": "Racking survey", "start": "2026-09-25T10:00" }
              ]
            }
            """;

        // Act
        var events = CalendarEventExtractionReading.Read(answer, Anchor, maximumEvents: 1);

        // Assert
        Assert.Equal("Site visit", Assert.Single(events).Title.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("I am afraid I cannot help with that.")]
    [InlineData("{ \"events\": [] }")]
    [InlineData("{ }")]
    [InlineData("{ \"events\": [ ")]
    public void Read_AnAnswerNamingNothingReadable_AnswersWithNoEvents(string? answer)
    {
        // Act
        var events = CalendarEventExtractionReading.Read(answer, Anchor, CalendarEventExtraction.MaximumEvents);

        // Assert
        Assert.Empty(events);
    }

    /// <summary>A date at the very end of the range has no instant once an offset is applied, in some zones.</summary>
    [Theory]
    [InlineData("0001-01-01T00:00")]
    [InlineData("9999-12-31T23:59")]
    public void Read_AStartAtTheEdgeOfTheCalendar_DropsTheEvent(string start)
    {
        // Arrange
        var answer = $$"""{ "events": [ { "title": "Site visit", "start": "{{start}}" } ] }""";

        // Act
        var events = CalendarEventExtractionReading.Read(answer, Anchor, CalendarEventExtraction.MaximumEvents);

        // Assert
        Assert.Empty(events);
    }

    [Fact]
    public void Read_ABoundThatIsNotPositive_IsRefused()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CalendarEventExtractionReading.Read("{}", Anchor, maximumEvents: 0));
    }
}
