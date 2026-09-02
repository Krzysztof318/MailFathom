// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Synchronization;
using Xunit;

namespace MailFathom.Application.UnitTests.Synchronization;

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

    /// <summary>
    /// The first failure draws from the interval up to twice it, and a delay that never left the interval is the shared
    /// curve being handed a failure count where it expects an attempt count — which halves every backed-off delay while
    /// still satisfying every bound above.
    /// </summary>
    [Fact]
    public void DelayBeforeNextRun_TheFirstFailure_DrawsPastTheIntervalRatherThanStayingOnIt()
    {
        // Arrange, Act
        var delays = Enumerable.Range(0, 64)
            .Select(_ => SynchronizationRunBackoff.DelayBeforeNextRun(Interval, MaxDelay, consecutiveFailureCount: 1))
            .ToArray();

        // Assert
        Assert.All(delays, delay => Assert.InRange(delay, Interval, 2 * Interval));
        Assert.Contains(delays, delay => delay > Interval);
    }

    /// <summary>A failure count no account reaches is still a count this answers with the ceiling rather than refuses.</summary>
    [Fact]
    public void DelayBeforeNextRun_TheLargestFailureCount_StaysWithinTheConfiguredCeiling()
    {
        // Arrange, Act
        var delay = SynchronizationRunBackoff.DelayBeforeNextRun(Interval, MaxDelay, int.MaxValue);

        // Assert
        Assert.InRange(delay, MaxDelay / 2, MaxDelay);
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
