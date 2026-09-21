// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Application.Calendar.Import;
using MailFathom.Infrastructure.Calendar;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Calendar;

/// <summary>
/// Covers the reader an offered iCalendar file is parsed by. What it has to hold is that an instant the file states in
/// any of the three ways RFC 5545 allows resolves to the instant the file names, that a day stays a day, that every
/// shape this deployment has no event for is skipped under the reason that says why, and that octets which are not
/// iCalendar are reported rather than raised about.
/// </summary>
public sealed class CalendarFileReaderTests
{
    private const string Warsaw = "Europe/Warsaw";

    private static readonly TimeZoneInfo Unzoned = TimeZoneInfo.FindSystemTimeZoneById(Warsaw);

    private readonly CalendarFileReader reader = new();

    [Fact]
    public void Read_AnEntryStatingUtcInstants_ResolvesBothOfThem()
    {
        // Act
        var entry = this.Single(Entry("DTSTART:20260921T080000Z", "DTEND:20260921T093000Z"));

        // Assert
        Assert.Equal(new DateTimeOffset(2026, 9, 21, 8, 0, 0, TimeSpan.Zero), entry.Start);
        Assert.Equal(new DateTimeOffset(2026, 9, 21, 9, 30, 0, TimeSpan.Zero), entry.End);
        Assert.False(entry.IsAllDay);
    }

    /// <summary>Eight in the morning in Warsaw is six in the morning in the coordinated zone that September, and the file is the only place that is written down.</summary>
    [Fact]
    public void Read_AnEntryNamingItsOwnZone_ResolvesTheInstantThatZoneGivesIt()
    {
        // Act
        var entry = this.Single(Entry($"DTSTART;TZID={Warsaw}:20260921T080000"));

        // Assert
        Assert.Equal(new DateTimeOffset(2026, 9, 21, 6, 0, 0, TimeSpan.Zero), entry.Start.ToUniversalTime());
    }

    /// <summary>A time stated with no zone at all is the local time of whoever reads it, which is the zone the caller stated.</summary>
    [Fact]
    public void Read_AnEntryStatingNeitherAZoneNorAUtcInstant_ReadsItInTheZoneTheCallerStated()
    {
        // Act
        var entry = this.Single(Entry("DTSTART:20260921T080000"));

        // Assert
        Assert.Equal(new DateTimeOffset(2026, 9, 21, 6, 0, 0, TimeSpan.Zero), entry.Start.ToUniversalTime());
    }

    /// <summary>A day this deployment read in its own zone would be a different day for anybody east or west of it.</summary>
    [Fact]
    public void Read_AnEntryStatingADayRatherThanATime_StaysADayOpeningInTheZoneTheCallerStated()
    {
        // Act
        var entry = this.Single(Entry("DTSTART;VALUE=DATE:20260921", "DTEND;VALUE=DATE:20260922"));

        // Assert
        Assert.True(entry.IsAllDay);
        Assert.Equal(new DateTimeOffset(2026, 9, 20, 22, 0, 0, TimeSpan.Zero), entry.Start.ToUniversalTime());
        Assert.Equal(new DateTimeOffset(2026, 9, 21, 22, 0, 0, TimeSpan.Zero), entry.End!.Value.ToUniversalTime());
    }

    [Fact]
    public void Read_AnEntryStatingADurationRatherThanAnEnd_MeasuresTheEndFromItsOwnStart()
    {
        // Act
        var entry = this.Single(Entry("DTSTART:20260921T080000Z", "DURATION:PT90M"));

        // Assert
        Assert.Equal(new DateTimeOffset(2026, 9, 21, 9, 30, 0, TimeSpan.Zero), entry.End);
    }

    [Fact]
    public void Read_AnEntryStatingNeitherAnEndNorADuration_CarriesNoEnd()
    {
        // Act and assert
        Assert.Null(this.Single(Entry("DTSTART:20260921T080000Z")).End);
    }

    /// <summary>Line folding and escaping are the two parts of this format nobody hand-rolls correctly, and a title is where both meet.</summary>
    [Fact]
    public void Read_AnEntryWhoseSummaryIsFoldedAndEscaped_ReadsTheTitleTheFileMeant()
    {
        // Arrange
        var file = File(
            """
            BEGIN:VCALENDAR
            VERSION:2.0
            BEGIN:VEVENT
            UID:one
            SUMMARY:Planning\, review
              and hand-over
            DTSTART:20260921T080000Z
            END:VEVENT
            END:VCALENDAR
            """);

        // Act
        var entry = this.Single(file);

        // Assert
        Assert.Equal("Planning, review and hand-over", entry.Title.Value);
    }

    [Theory]
    [InlineData("RRULE:FREQ=WEEKLY;COUNT=4", CalendarImportSkipReason.Recurring)]
    [InlineData("RDATE:20260928T080000Z", CalendarImportSkipReason.Recurring)]
    [InlineData("RECURRENCE-ID:20260921T080000Z", CalendarImportSkipReason.Recurring)]
    [InlineData("DTEND:20260921T080000Z", CalendarImportSkipReason.EndNotAfterStart)]
    [InlineData("DTEND:20260921T070000Z", CalendarImportSkipReason.EndNotAfterStart)]
    [InlineData("DTEND;TZID=Mars/Olympus_Mons:20260921T100000", CalendarImportSkipReason.UnknownTimeZone)]
    public void Read_AnEntryThisDeploymentHoldsNoEventFor_IsSkippedUnderTheReasonThatSaysWhy(
        string property,
        CalendarImportSkipReason expected)
    {
        // Act
        var reading = this.reader.Read(File(Entry("DTSTART:20260921T080000Z", property)), Unzoned);

        // Assert
        Assert.Empty(reading.Entries);
        Assert.Equal(expected, Assert.Single(reading.Skipped));
    }

    [Theory]
    [InlineData("UID:one\nSUMMARY:Planning", CalendarImportSkipReason.NoStart)]
    [InlineData("SUMMARY:Planning\nDTSTART:20260921T080000Z", CalendarImportSkipReason.UnreadableIdentifier)]
    [InlineData("UID:one\nDTSTART:20260921T080000Z", CalendarImportSkipReason.UnreadableTitle)]
    [InlineData("UID:one\nSUMMARY:Planning\nDTSTART;TZID=Mars/Olympus_Mons:20260921T080000", CalendarImportSkipReason.UnknownTimeZone)]
    public void Read_AnEntryMissingWhatAnEventIsMadeOf_IsSkippedUnderTheReasonThatSaysWhy(
        string properties,
        CalendarImportSkipReason expected)
    {
        // Arrange
        var file = File($"BEGIN:VCALENDAR\nVERSION:2.0\nBEGIN:VEVENT\n{properties}\nEND:VEVENT\nEND:VCALENDAR");

        // Act
        var reading = this.reader.Read(file, Unzoned);

        // Assert
        Assert.Empty(reading.Entries);
        Assert.Equal(expected, Assert.Single(reading.Skipped));
    }

    /// <summary>
    /// An entry stating no identifier is told apart from one stating a bare UUID by whether the file carries it, so
    /// this is the case that would be lost to that reading if it read the shape alone. Losing it would report a
    /// perfectly good export as an unreadable one.
    /// </summary>
    [Fact]
    public void Read_AnEntryWhoseStatedIdentifierIsABareUuid_KeepsItRatherThanTakingItForOneNobodyStated()
    {
        // Arrange
        const string Stated = "8c4c4d0e-3a4f-4a2a-9f0e-4b2f1c9d7a10";

        // Act
        var reading = this.reader.Read(
            File($"BEGIN:VCALENDAR\nVERSION:2.0\nBEGIN:VEVENT\nUID:{Stated}\nSUMMARY:Planning\n"
                + "DTSTART:20260921T080000Z\nEND:VEVENT\nEND:VCALENDAR"),
            Unzoned);

        // Assert
        Assert.Equal(Stated, Assert.Single(reading.Entries).Uid.Value);
    }

    /// <summary>What an entry attaches, links to, or names a person by is never read, which is what keeps an import from reaching anything.</summary>
    [Fact]
    public void Read_AnEntryCarryingAttachmentsUrlsAndParticipants_ReadsNoneOfThemIntoTheEvent()
    {
        // Arrange
        var file = File(
            """
            BEGIN:VCALENDAR
            VERSION:2.0
            METHOD:REQUEST
            BEGIN:VEVENT
            UID:one
            SUMMARY:Planning
            DTSTART:20260921T080000Z
            DESCRIPTION:Somebody else's notes
            LOCATION:Somewhere
            URL:https://example.invalid/agenda
            ATTACH:https://example.invalid/agenda.pdf
            ORGANIZER:mailto:organiser@example.invalid
            ATTENDEE:mailto:attendee@example.invalid
            END:VEVENT
            END:VCALENDAR
            """);

        // Act
        var entry = this.Single(file);

        // Assert
        Assert.Equal("Planning", entry.Title.Value);
        Assert.Equal(new DateTimeOffset(2026, 9, 21, 8, 0, 0, TimeSpan.Zero), entry.Start);
        Assert.Null(entry.End);
    }

    [Theory]
    [InlineData("")]
    [InlineData("this is not a calendar at all")]
    [InlineData("BEGIN:VEVENT\nUID:one\nEND:VEVENT")]
    public void Read_OctetsThatAreNotACalendar_AreReportedRatherThanRaisedAbout(string text)
    {
        // Act
        var reading = this.reader.Read(File(text), Unzoned);

        // Assert
        Assert.Equal(CalendarImportOutcome.NotCalendarData, reading.Outcome);
    }

    /// <summary>A calendar holding nothing is a readable file rather than a broken one, and it creates nothing.</summary>
    [Fact]
    public void Read_ACalendarWithNoEntriesInIt_IsReadAndOffersNothing()
    {
        // Act
        var reading = this.reader.Read(File("BEGIN:VCALENDAR\nVERSION:2.0\nEND:VCALENDAR"), Unzoned);

        // Assert
        Assert.Equal(CalendarImportOutcome.Read, reading.Outcome);
        Assert.Empty(reading.Entries);
        Assert.Empty(reading.Skipped);
    }

    [Fact]
    public void Read_AFileNamingMoreEntriesThanOneImportWrites_IsRefusedWholeRatherThanTruncated()
    {
        // Arrange
        var entries = string.Concat(Enumerable
            .Range(0, CalendarFileImport.MaximumEntryCount + 1)
            .Select(number =>
                $"BEGIN:VEVENT\nUID:{number}\nSUMMARY:Planning\nDTSTART:20260921T080000Z\nEND:VEVENT\n"));

        // Act
        var reading = this.reader.Read(File($"BEGIN:VCALENDAR\nVERSION:2.0\n{entries}END:VCALENDAR"), Unzoned);

        // Assert
        Assert.Equal(CalendarImportOutcome.TooManyEntries, reading.Outcome);
        Assert.Empty(reading.Entries);
    }

    /// <summary>An export can hold several calendars in one file, and every entry of each of them was offered.</summary>
    [Fact]
    public void Read_SeveralCalendarsInOneFile_ReadsTheEntriesOfAllOfThem()
    {
        // Arrange
        var file = File(
            """
            BEGIN:VCALENDAR
            VERSION:2.0
            BEGIN:VEVENT
            UID:one
            SUMMARY:Planning
            DTSTART:20260921T080000Z
            END:VEVENT
            END:VCALENDAR
            BEGIN:VCALENDAR
            VERSION:2.0
            BEGIN:VEVENT
            UID:two
            SUMMARY:Review
            DTSTART:20260922T080000Z
            END:VEVENT
            END:VCALENDAR
            """);

        // Act
        var reading = this.reader.Read(file, Unzoned);

        // Assert
        Assert.Equal(["one", "two"], reading.Entries.Select(entry => entry.Uid.Value));
    }

    private static string Entry(params string[] properties) =>
        $"BEGIN:VCALENDAR\nVERSION:2.0\nBEGIN:VEVENT\nUID:one\nSUMMARY:Planning\n"
        + $"{string.Join('\n', properties)}\nEND:VEVENT\nEND:VCALENDAR";

    private static MemoryStream File(string text) =>
        new(Encoding.UTF8.GetBytes(text.ReplaceLineEndings("\r\n")));

    private CalendarFileEntry Single(string file) =>
        Assert.Single(this.reader.Read(File(file), Unzoned).Entries);

    private CalendarFileEntry Single(MemoryStream file) =>
        Assert.Single(this.reader.Read(file, Unzoned).Entries);
}
