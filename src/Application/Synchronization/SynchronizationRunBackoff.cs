// Copyright © 2026 Krzysztof Kasprowicz

using System.Security.Cryptography;

namespace MailMcp.Application.Synchronization;

/// <summary>Decides how long one account waits before its next synchronization run.</summary>
/// <remarks>
/// <para>
/// This is run-level backoff, and it is a different decision from the operation-level retry an adapter's resilience
/// pipeline performs. The pipeline decides whether one IMAP command is worth repeating; this decides whether a whole
/// run is worth starting again yet. The two never wrap each other: a run that ended in failure has already spent its
/// pipeline budget, so starting the next one on the ordinary interval would spend the same budget again against a
/// server that has just refused it.
/// </para>
/// <para>
/// A successful run returns the account to its configured interval, so backoff never outlives the condition that
/// caused it, and the delay is drawn from a range rather than computed exactly. Accounts that share a mail server fail
/// together, and an exact delay would return every one of them to that server in the same instant on every later run.
/// </para>
/// </remarks>
public static class SynchronizationRunBackoff
{
    /// <summary>Bounds the exponent so the doubling stays inside the tick arithmetic that applies it.</summary>
    /// <remarks>Growth has reached any usable ceiling long before this, so the bound costs nothing an operator can observe.</remarks>
    private const int MaxGrowthSteps = 16;

    /// <summary>How finely a delay is drawn from its range.</summary>
    private const int JitterResolution = 1000;

    /// <summary>Computes the delay before one account's next synchronization run.</summary>
    /// <param name="interval">The configured interval a healthy account runs on.</param>
    /// <param name="maxDelay">The ceiling a backed-off delay never exceeds.</param>
    /// <param name="consecutiveFailureCount">How many runs of this account failed in a row; zero after one succeeded.</param>
    /// <returns>The configured interval when nothing failed, otherwise a jittered delay of at least that interval and at most <paramref name="maxDelay" />.</returns>
    /// <remarks>
    /// A backed-off delay is never shorter than the configured interval. Backoff exists to approach a struggling server
    /// less often, so a ceiling or a jitter draw that let a failing account run sooner than a healthy one would invert
    /// the whole point of it.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the interval is not positive, the ceiling is below the interval, or the failure count is negative.</exception>
    public static TimeSpan DelayBeforeNextRun(
        TimeSpan interval,
        TimeSpan maxDelay,
        int consecutiveFailureCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxDelay, interval);
        ArgumentOutOfRangeException.ThrowIfNegative(consecutiveFailureCount);

        if (consecutiveFailureCount == 0)
        {
            return interval;
        }

        var ceilingTicks = GrowFromInterval(interval.Ticks, maxDelay.Ticks, consecutiveFailureCount);
        var floorTicks = Math.Max(interval.Ticks, ceilingTicks / 2);

        return TimeSpan.FromTicks(floorTicks + DrawJitterTicks(ceilingTicks - floorTicks));
    }

    /// <summary>Doubles the interval once per consecutive failure and caps the result.</summary>
    /// <remarks>
    /// The comparison shifts the ceiling down rather than shifting the interval up, so the product that would exceed
    /// the ceiling — and, for a large enough failure count, the tick range itself — is never computed.
    /// </remarks>
    private static long GrowFromInterval(
        long intervalTicks,
        long maxDelayTicks,
        int consecutiveFailureCount)
    {
        var growthSteps = Math.Min(consecutiveFailureCount, MaxGrowthSteps);

        return intervalTicks <= maxDelayTicks >> growthSteps
            ? intervalTicks << growthSteps
            : maxDelayTicks;
    }

    /// <summary>Draws the jitter added to the floor of the range.</summary>
    /// <remarks>
    /// The draw is a fraction of the range rather than a tick count, so one generator call covers every range width
    /// without the range having to fit the generator's own bounds.
    /// </remarks>
    private static long DrawJitterTicks(long spreadTicks) =>
        spreadTicks == 0
            ? 0
            : spreadTicks * RandomNumberGenerator.GetInt32(0, JitterResolution + 1) / JitterResolution;
}
