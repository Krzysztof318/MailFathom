// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Retrieval.AskMail;
using Xunit;

namespace MailFathom.Application.UnitTests.Retrieval.AskMail;

/// <summary>Covers what every answering run of one period may add up to before the next question is refused.</summary>
public sealed class MailAnsweringPeriodBoundsTests
{
    [Fact]
    public void Default_TheBoundsADeploymentReceives_MeetAnOperatorWithinTheHourRatherThanTheDay()
    {
        // Act
        var bounds = MailAnsweringPeriodBounds.Default;

        // Assert
        Assert.Equal(TimeSpan.FromHours(1), bounds.Period);
        Assert.Equal(30, bounds.MaximumRuns);
        Assert.Equal(300_000L, bounds.MaximumTokens);
    }

    [Theory]
    [InlineData(0, 30, 300_000)]
    [InlineData(-1, 30, 300_000)]
    public void Create_APeriodThatCouldNeverElapse_IsRefused(int periodSeconds, int runs, long tokens)
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MailAnsweringPeriodBounds.Create(TimeSpan.FromSeconds(periodSeconds), runs, tokens));
    }

    [Theory]
    [InlineData(0, 300_000)]
    [InlineData(-1, 300_000)]
    [InlineData(30, 0)]
    [InlineData(30, -1)]
    public void Create_ACeilingNoPeriodCouldAdmitAQuestionUnder_IsRefused(int runs, long tokens)
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MailAnsweringPeriodBounds.Create(TimeSpan.FromHours(1), runs, tokens));
    }

    /// <summary>
    /// Anchored at the epoch rather than at start-up, which is what lets every restart of a process — and the other
    /// spend ceiling this product has — agree on where a period begins without anything being stored to say so.
    /// </summary>
    [Theory]
    [InlineData("2026-08-08T12:00:00Z", "2026-08-08T12:00:00Z")]
    [InlineData("2026-08-08T12:37:41Z", "2026-08-08T12:00:00Z")]
    [InlineData("2026-08-08T12:59:59Z", "2026-08-08T12:00:00Z")]
    [InlineData("2026-08-08T13:00:00Z", "2026-08-08T13:00:00Z")]
    public void PeriodStartAt_AnInstant_PlacesItInTheWindowTheClockDecides(string instant, string expectedStart)
    {
        // Arrange
        var bounds = MailAnsweringPeriodBounds.Create(TimeSpan.FromHours(1), 30, 300_000);

        // Act
        var start = bounds.PeriodStartAt(DateTimeOffset.Parse(instant, CultureInfo.InvariantCulture));

        // Assert
        Assert.Equal(DateTimeOffset.Parse(expectedStart, CultureInfo.InvariantCulture), start);
    }

    /// <summary>The roll-over is the instant a refused caller is worth telling to come back at.</summary>
    [Fact]
    public void PeriodEndAt_AnInstant_IsOneWholePeriodAfterTheStartItFallsIn()
    {
        // Arrange
        var bounds = MailAnsweringPeriodBounds.Create(TimeSpan.FromHours(1), 30, 300_000);
        var instant = new DateTimeOffset(2026, 8, 8, 12, 37, 41, TimeSpan.Zero);

        // Act, Assert
        Assert.Equal(new DateTimeOffset(2026, 8, 8, 13, 0, 0, TimeSpan.Zero), bounds.PeriodEndAt(instant));
    }

    /// <summary>An instant a test may hand it lands on the start of its period rather than the end of the one before.</summary>
    [Fact]
    public void PeriodStartAt_AnInstantBeforeTheEpoch_FloorsRatherThanTruncates()
    {
        // Arrange
        var bounds = MailAnsweringPeriodBounds.Create(TimeSpan.FromHours(1), 30, 300_000);
        var instant = DateTimeOffset.UnixEpoch - TimeSpan.FromMinutes(30);

        // Act, Assert
        Assert.Equal(DateTimeOffset.UnixEpoch - TimeSpan.FromHours(1), bounds.PeriodStartAt(instant));
    }

    /// <summary>The rendering reaches a log, so it states the numbers and nothing that was measured against them.</summary>
    [Fact]
    public void ToString_TheBounds_ReportsBothCeilingsAndThePeriod()
    {
        // Act
        var rendered = MailAnsweringPeriodBounds.Create(TimeSpan.FromHours(1), 30, 300_000).ToString();

        // Assert
        Assert.Equal(
            string.Format(
                CultureInfo.InvariantCulture,
                "at most 30 runs costing at most 300000 tokens every {0}",
                TimeSpan.FromHours(1)),
            rendered);
    }
}
