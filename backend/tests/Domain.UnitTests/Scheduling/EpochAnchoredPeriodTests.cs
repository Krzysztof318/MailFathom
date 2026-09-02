// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Scheduling;
using Xunit;

namespace MailFathom.Domain.UnitTests.Scheduling;

public sealed class EpochAnchoredPeriodTests
{
    private static readonly TimeSpan Hourly = TimeSpan.FromHours(1);

    [Fact]
    public void StartAt_AnInstantInsideAPeriod_ReturnsTheBoundaryBelowIt()
    {
        // Arrange
        var instant = new DateTimeOffset(2026, 8, 20, 10, 17, 33, TimeSpan.Zero);

        // Act
        var start = EpochAnchoredPeriod.StartAt(Hourly, instant);

        // Assert
        Assert.Equal(new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero), start);
    }

    [Fact]
    public void StartAt_AnInstantOnABoundary_ReturnsThatInstant()
    {
        // Arrange
        var instant = new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero);

        // Act
        var start = EpochAnchoredPeriod.StartAt(Hourly, instant);

        // Assert
        Assert.Equal(instant, start);
    }

    /// <summary>
    /// The zone an instant is written in says nothing about which period it falls in, so two spellings of one moment
    /// must be counted under the same key.
    /// </summary>
    [Fact]
    public void StartAt_AnInstantWrittenWithAnOffset_PlacesItByTheMomentRatherThanTheLocalClock()
    {
        // Arrange
        var daily = TimeSpan.FromDays(1);
        var instant = new DateTimeOffset(2026, 8, 20, 2, 30, 0, TimeSpan.FromHours(2));

        // Act
        var start = EpochAnchoredPeriod.StartAt(daily, instant);

        // Assert
        Assert.Equal(new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero), start);
    }

    /// <summary>
    /// No clock this runs on reports an instant before the epoch, but a test may hand it one, and truncating there
    /// would answer with the end of the period before rather than with a start the instant actually falls in.
    /// </summary>
    [Fact]
    public void StartAt_AnInstantBeforeTheEpoch_FloorsRatherThanTruncates()
    {
        // Arrange
        var instant = DateTimeOffset.UnixEpoch - TimeSpan.FromMinutes(30);

        // Act
        var start = EpochAnchoredPeriod.StartAt(Hourly, instant);

        // Assert
        Assert.Equal(DateTimeOffset.UnixEpoch - Hourly, start);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(24)]
    [InlineData(168)]
    public void EndAt_AnyInstant_IsOnePeriodAfterTheStart(int periodHours)
    {
        // Arrange
        var period = TimeSpan.FromHours(periodHours);
        var instant = new DateTimeOffset(2026, 8, 20, 10, 17, 33, TimeSpan.Zero);

        // Act
        var end = EpochAnchoredPeriod.EndAt(period, instant);

        // Assert
        Assert.Equal(EpochAnchoredPeriod.StartAt(period, instant) + period, end);
        Assert.InRange(instant, end - period, end);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void StartAt_APeriodThatIsNotPositive_IsRefused(int periodHours)
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => EpochAnchoredPeriod.StartAt(
            TimeSpan.FromHours(periodHours),
            DateTimeOffset.UnixEpoch));
    }
}
