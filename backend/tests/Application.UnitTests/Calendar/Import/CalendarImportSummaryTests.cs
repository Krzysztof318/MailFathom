// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Calendar.Import;
using Xunit;

namespace MailFathom.Application.UnitTests.Calendar.Import;

/// <summary>
/// Covers the report a person decides an import from. What it has to hold is that the skips arrive as a count per
/// reason in an order two readings agree on, that the span is the first and last event rather than the file's extent,
/// and that a refusal of the whole file reports nothing about events it did not create.
/// </summary>
public sealed class CalendarImportSummaryTests
{
    private static readonly DateTimeOffset Monday = new(2026, 9, 21, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Of_SeveralEntriesSkippedForTwoReasons_CountsEachReasonOnceInTheReasonsOwnOrder()
    {
        // Act
        var summary = CalendarImportSummary.Of(
            [Monday],
            [
                CalendarImportSkipReason.AlreadyOnTheCalendar,
                CalendarImportSkipReason.Recurring,
                CalendarImportSkipReason.Recurring,
            ]);

        // Assert
        Assert.Equal(
            [
                new CalendarImportSkipTally(CalendarImportSkipReason.Recurring, 2),
                new CalendarImportSkipTally(CalendarImportSkipReason.AlreadyOnTheCalendar, 1),
            ],
            summary.Skipped);
    }

    /// <summary>The span is where the events begin, so a long entry does not report a wider import than the file holds.</summary>
    [Fact]
    public void Of_EventsAcrossSeveralDays_ReportsTheFirstAndLastOfThemRatherThanTheOrderTheyArrivedIn()
    {
        // Act
        var summary = CalendarImportSummary.Of([Monday.AddDays(3), Monday, Monday.AddDays(1)], []);

        // Assert
        Assert.Equal(3, summary.Events);
        Assert.Equal(Monday, summary.Earliest);
        Assert.Equal(Monday.AddDays(3), summary.Latest);
    }

    [Fact]
    public void Of_AFileThatWouldCreateNothing_ReportsNoSpanRatherThanAnEmptyOne()
    {
        // Act
        var summary = CalendarImportSummary.Of([], [CalendarImportSkipReason.Recurring]);

        // Assert
        Assert.Equal(0, summary.Events);
        Assert.Null(summary.Earliest);
        Assert.Null(summary.Latest);
    }

    [Fact]
    public void Refused_AFileThatWasNotCalendarData_CarriesTheRefusalAndNothingAboutEvents()
    {
        // Act
        var summary = CalendarImportSummary.Refused(CalendarImportOutcome.NotCalendarData);

        // Assert
        Assert.Equal(CalendarImportOutcome.NotCalendarData, summary.Outcome);
        Assert.Equal(0, summary.Events);
        Assert.Empty(summary.Skipped);
    }

    /// <summary>A reading that succeeded is not a refusal, and composing one as though it were would report a file nobody refused.</summary>
    [Fact]
    public void Refused_TheOutcomeOfAFileThatWasRead_IsRefusedAsAProgrammingError() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CalendarImportSummary.Refused(CalendarImportOutcome.Read));
}
