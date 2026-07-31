// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.Synchronization;
using Xunit;

namespace MailMcp.Application.UnitTests;

public sealed class SynchronizationRunBackoffTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromHours(1);

    [Fact]
    public void DelayBeforeNextRun_NoRunHasFailed_ReturnsTheConfiguredInterval()
    {
        // Arrange, Act
        var delay = SynchronizationRunBackoff.DelayBeforeNextRun(Interval, MaxDelay, consecutiveFailureCount: 0);

        // Assert
        Assert.Equal(Interval, delay);
    }

    /// <summary>Each further consecutive failure doubles both ends of the range the delay is drawn from.</summary>
    [Theory]
    [InlineData(1, 1, 2)]
    [InlineData(2, 2, 4)]
    [InlineData(3, 4, 8)]
    [InlineData(4, 8, 16)]
    public void DelayBeforeNextRun_ConsecutiveFailures_DrawsFromTheDoubledRange(
        int consecutiveFailureCount,
        int expectedMinimumIntervals,
        int expectedMaximumIntervals)
    {
        // Arrange
        var expectedMinimum = expectedMinimumIntervals * Interval;
        var expectedMaximum = expectedMaximumIntervals * Interval;

        // Act
        var delay = SynchronizationRunBackoff.DelayBeforeNextRun(Interval, MaxDelay, consecutiveFailureCount);

        // Assert
        Assert.InRange(delay, expectedMinimum, expectedMaximum);
    }

    /// <summary>A failing account must never approach its server sooner than a healthy one does.</summary>
    [Fact]
    public void DelayBeforeNextRun_CeilingEqualsTheInterval_NeverFallsBelowTheInterval()
    {
        // Arrange, Act
        var delays = Enumerable.Range(1, 20)
            .Select(consecutiveFailureCount => SynchronizationRunBackoff.DelayBeforeNextRun(
                Interval,
                Interval,
                consecutiveFailureCount));

        // Assert
        Assert.All(delays, delay => Assert.Equal(Interval, delay));
    }

    [Fact]
    public void DelayBeforeNextRun_ManyConsecutiveFailures_StaysWithinTheConfiguredCeiling()
    {
        // Arrange, Act
        var delays = Enumerable.Range(1, 100)
            .Select(consecutiveFailureCount => SynchronizationRunBackoff.DelayBeforeNextRun(
                Interval,
                MaxDelay,
                consecutiveFailureCount));

        // Assert
        Assert.All(delays, delay => Assert.InRange(delay, Interval, MaxDelay));
    }

    [Fact]
    public void DelayBeforeNextRun_CeilingBelowTheInterval_IsRejected()
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => SynchronizationRunBackoff.DelayBeforeNextRun(
            Interval,
            Interval - TimeSpan.FromSeconds(1),
            consecutiveFailureCount: 1));
    }

    [Fact]
    public void DelayBeforeNextRun_NonPositiveInterval_IsRejected()
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => SynchronizationRunBackoff.DelayBeforeNextRun(
            TimeSpan.Zero,
            MaxDelay,
            consecutiveFailureCount: 0));
    }

    [Fact]
    public void DelayBeforeNextRun_NegativeFailureCount_IsRejected()
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => SynchronizationRunBackoff.DelayBeforeNextRun(
            Interval,
            MaxDelay,
            consecutiveFailureCount: -1));
    }
}
