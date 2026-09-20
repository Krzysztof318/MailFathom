// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Calendar.Extraction;
using MailFathom.Domain.Calendar;
using Xunit;

namespace MailFathom.Application.UnitTests.Calendar.Extraction;

/// <summary>Covers the two unequal outcomes of a reading, and the bound one of them carries.</summary>
public sealed class CalendarEventExtractionTests
{
    /// <summary>An answer of no events is settled, which is what takes the text out of whatever queue it was in.</summary>
    [Fact]
    public void Settled_AReadingThatFoundNothing_IsAnAnswerRatherThanAWithholding()
    {
        // Act
        var extraction = CalendarEventExtraction.Settled([]);

        // Assert
        Assert.True(extraction.IsSettled);
        Assert.Null(extraction.Withheld);
        Assert.Empty(extraction.Events);
    }

    [Fact]
    public void Settled_MoreEventsThanOneReadingMayFind_IsRefused()
    {
        // Arrange
        var events = Enumerable
            .Range(0, CalendarEventExtraction.MaximumEvents + 1)
            .Select(ordinal => Extracted($"Visit {ordinal}"))
            .ToArray();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => CalendarEventExtraction.Settled(events));
    }

    [Fact]
    public void Withholding_AConditionThatStoppedTheReading_CarriesNoEvents()
    {
        // Act
        var extraction = CalendarEventExtraction.Withholding(
            CalendarEventExtractionWithholding.ProviderUnavailable);

        // Assert
        Assert.False(extraction.IsSettled);
        Assert.Equal(CalendarEventExtractionWithholding.ProviderUnavailable, extraction.Withheld);
        Assert.Empty(extraction.Events);
    }

    [Fact]
    public void Withholding_AConditionThisSystemDoesNotStopOn_IsRefused()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CalendarEventExtraction.Withholding((CalendarEventExtractionWithholding)99));
    }

    private static ExtractedCalendarEvent Extracted(string title) =>
        new(
            CalendarEventTitle.Create(title),
            new DateTimeOffset(2026, 9, 24, 10, 0, 0, TimeSpan.Zero),
            End: null);
}
