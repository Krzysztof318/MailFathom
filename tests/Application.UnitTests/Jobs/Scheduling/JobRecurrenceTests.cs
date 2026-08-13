// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Jobs.Scheduling;
using Xunit;

namespace MailFathom.Application.UnitTests.Jobs.Scheduling;

/// <summary>The declared syntax, the occasions it produces, and the two daylight-saving cases a local time meets.</summary>
public sealed class JobRecurrenceTests
{
    /// <summary>A zone with a spring-forward gap and an autumn-back overlap, which is what the two cases below need.</summary>
    private const string WarsawZoneId = "Europe/Warsaw";

    [Theory]
    [InlineData("Every 01:00:00", "every:01:00:00")]
    [InlineData("every 7.00:00:00", "every:7.00:00:00")]
    [InlineData("Daily at 03:30", "daily:03:30:UTC")]
    [InlineData("  Daily   at   03:30  ", "daily:03:30:UTC")]
    [InlineData("Daily at 03:30 Europe/Warsaw", "daily:03:30:Europe/Warsaw")]
    public void TryParse_ADeclarationThisSystemRuns_ReadsItIntoOneCanonicalForm(string declaration, string canonicalForm)
    {
        // Act
        var parsed = JobRecurrence.TryParse(declaration, out var recurrence, out var error);

        // Assert
        Assert.True(parsed);
        Assert.Null(error);
        Assert.Equal(canonicalForm, recurrence?.CanonicalForm);
    }

    /// <summary>A malformed schedule is refused when the configuration is read, so a typo never becomes a rule that never fires.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0 3 * * *")]
    [InlineData("Every")]
    [InlineData("Every hourly")]
    [InlineData("Daily 03:30")]
    [InlineData("Daily at 3:30")]
    [InlineData("Daily at 25:00")]
    [InlineData("Daily at 03:30 Middle/Earth")]
    [InlineData("Every 00:00:30")]
    [InlineData("Every 366.00:00:00")]
    public void TryParse_ADeclarationThisSystemCannotRun_IsRefusedWithSomethingToFix(string? declaration)
    {
        // Act
        var parsed = JobRecurrence.TryParse(declaration, out var recurrence, out var error);

        // Assert
        Assert.False(parsed);
        Assert.Null(recurrence);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    /// <summary>Occasions are anchored to the epoch rather than to the last dispatch, so they do not drift and every replica agrees.</summary>
    [Fact]
    public void LatestOccurrenceAtOrBefore_AnInterval_IsAnchoredToTheEpochRatherThanToTheReadingInstant()
    {
        // Arrange
        var recurrence = Parse("Every 06:00:00");

        // Act
        var latest = recurrence.LatestOccurrenceAtOrBefore(Instant("2026-08-13T14:37:22Z"));
        var next = recurrence.NextOccurrenceAfter(Instant("2026-08-13T14:37:22Z"));

        // Assert
        Assert.Equal(Instant("2026-08-13T12:00:00Z"), latest);
        Assert.Equal(Instant("2026-08-13T18:00:00Z"), next);
    }

    /// <summary>A daily schedule read before the day's time is answered by the previous day's occasion rather than by nothing.</summary>
    [Fact]
    public void LatestOccurrenceAtOrBefore_ADailyTimeThatHasNotComeRoundToday_AnswersWithYesterdays()
    {
        // Arrange
        var recurrence = Parse("Daily at 03:00");

        // Act
        var latest = recurrence.LatestOccurrenceAtOrBefore(Instant("2026-08-13T01:00:00Z"));

        // Assert
        Assert.Equal(Instant("2026-08-12T03:00:00Z"), latest);
    }

    /// <summary>Without a zone the time is UTC, which is the answer an operator has to be able to read off the declaration.</summary>
    [Fact]
    public void NextOccurrenceAfter_ADailyTimeWithNoZone_IsReadInCoordinatedUniversalTime()
    {
        // Arrange
        var recurrence = Parse("Daily at 03:00");

        // Act
        var next = recurrence.NextOccurrenceAfter(Instant("2026-08-13T01:00:00Z"));

        // Assert
        Assert.Equal(Instant("2026-08-13T03:00:00Z"), next);
    }

    /// <summary>A named zone reads the wall clock in it, so the same declaration is a different instant in winter and in summer.</summary>
    [Theory]
    [InlineData("2026-01-13T00:00:00Z", "2026-01-13T02:30:00Z")]
    [InlineData("2026-08-13T00:00:00Z", "2026-08-13T01:30:00Z")]
    public void NextOccurrenceAfter_ADailyTimeInANamedZone_FollowsThatZonesOffset(string after, string expected)
    {
        // Arrange
        var recurrence = Parse($"Daily at 03:30 {WarsawZoneId}");

        // Act
        var next = recurrence.NextOccurrenceAfter(Instant(after));

        // Assert
        Assert.Equal(Instant(expected), next);
    }

    /// <summary>A local time the clock skips over does not occur, and the day's occasion happens at the instant the gap ends rather than being lost.</summary>
    [Fact]
    public void NextOccurrenceAfter_ALocalTimeTheClockSkipsOver_HappensWhenTheGapEnds()
    {
        // Arrange
        // Warsaw springs forward at 02:00 local on 29 March 2026, so 02:30 that day never happens.
        var recurrence = Parse($"Daily at 02:30 {WarsawZoneId}");

        // Act
        var next = recurrence.NextOccurrenceAfter(Instant("2026-03-29T00:00:00Z"));

        // Assert
        Assert.Equal(Instant("2026-03-29T01:00:00Z"), next);
    }

    /// <summary>A local time the clock passes through twice happens once, at the earlier of the two readings.</summary>
    [Fact]
    public void NextOccurrenceAfter_ALocalTimeTheClockPassesThroughTwice_HappensAtTheFirstOfThem()
    {
        // Arrange
        // Warsaw falls back at 03:00 local on 25 October 2026, so 02:30 that day happens at 00:30Z and again at 01:30Z.
        var recurrence = Parse($"Daily at 02:30 {WarsawZoneId}");

        // Act
        var next = recurrence.NextOccurrenceAfter(Instant("2026-10-24T23:00:00Z"));

        // Assert
        Assert.Equal(Instant("2026-10-25T00:30:00Z"), next);
    }

    /// <summary>The dispatch decides from the latest occasion, so the skipped local time has to resolve the same way looking back as looking forward.</summary>
    [Fact]
    public void LatestOccurrenceAtOrBefore_ALocalTimeTheClockSkipsOver_AnswersWithTheInstantTheGapEnded()
    {
        // Arrange
        // Warsaw springs forward at 02:00 local on 29 March 2026, so 02:30 that day never happens.
        var recurrence = Parse($"Daily at 02:30 {WarsawZoneId}");

        // Act
        var latest = recurrence.LatestOccurrenceAtOrBefore(Instant("2026-03-29T01:30:00Z"));

        // Assert
        Assert.Equal(Instant("2026-03-29T01:00:00Z"), latest);
    }

    /// <summary>Asked between the two readings of a repeated local time, the day's occasion is the earlier one and has already happened.</summary>
    [Fact]
    public void LatestOccurrenceAtOrBefore_ALocalTimeTheClockPassesThroughTwice_AnswersWithTheFirstOfThem()
    {
        // Arrange
        // Warsaw falls back at 03:00 local on 25 October 2026, so 02:30 that day happens at 00:30Z and again at 01:30Z.
        var recurrence = Parse($"Daily at 02:30 {WarsawZoneId}");

        // Act
        var latest = recurrence.LatestOccurrenceAtOrBefore(Instant("2026-10-25T01:00:00Z"));

        // Assert
        Assert.Equal(Instant("2026-10-25T00:30:00Z"), latest);
    }

    /// <summary>The count is what a skipped occasion is reported by, so a window holding several has to say how many.</summary>
    [Theory]
    [InlineData("Every 01:00:00", "2026-08-13T00:00:00Z", "2026-08-13T05:00:00Z", 5)]
    [InlineData("Every 01:00:00", "2026-08-13T00:30:00Z", "2026-08-13T01:00:00Z", 1)]
    [InlineData("Every 01:00:00", "2026-08-13T00:10:00Z", "2026-08-13T00:50:00Z", 0)]
    [InlineData("Daily at 03:00", "2026-08-10T03:00:00Z", "2026-08-13T03:00:00Z", 3)]
    [InlineData("Daily at 03:00", "2026-08-13T03:00:00Z", "2026-08-13T20:00:00Z", 0)]
    public void CountOccurrencesIn_AWindow_HoldsTheOccasionsBetweenItsEnds(
        string declaration,
        string exclusiveStart,
        string inclusiveEnd,
        int expected)
    {
        // Arrange
        var recurrence = Parse(declaration);

        // Act
        var count = recurrence.CountOccurrencesIn(Instant(exclusiveStart), Instant(inclusiveEnd));

        // Assert
        Assert.Equal(expected, count);
    }

    /// <summary>An empty or backwards window holds nothing, which is what a schedule that has kept up asks about itself.</summary>
    [Fact]
    public void CountOccurrencesIn_AWindowThatEndsBeforeItOpens_HoldsNothing()
    {
        // Arrange
        var recurrence = Parse("Every 01:00:00");

        // Act
        var count = recurrence.CountOccurrencesIn(Instant("2026-08-13T05:00:00Z"), Instant("2026-08-13T00:00:00Z"));

        // Assert
        Assert.Equal(0, count);
    }

    private static JobRecurrence Parse(string declaration)
    {
        Assert.True(JobRecurrence.TryParse(declaration, out var recurrence, out _));

        return recurrence!;
    }

    private static DateTimeOffset Instant(string value) => DateTimeOffset.Parse(
        value,
        CultureInfo.InvariantCulture,
        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
}
