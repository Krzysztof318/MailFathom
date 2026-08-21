// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Resilience;

namespace MailFathom.Application.Synchronization;

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
/// <para>
/// The curve itself is <see cref="JitteredRetryBackoff" />, which every scheduler of this system draws its delays from.
/// What is decided here is what a synchronization run means by it: a failure count rather than an attempt count, the
/// configured interval as the floor, and a healthy account answered before any of that arithmetic runs.
/// </para>
/// </remarks>
public static class SynchronizationRunBackoff
{
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

        // The shared curve counts attempts from one and grows by the attempts already made, while a failure count is
        // zero for an account that has never failed. Passing the count without the increment would halve every
        // backed-off delay. The increment saturates rather than wrapping, so a count no account reaches but a caller
        // may still hand this is answered with the ceiling instead of being refused as a negative attempt count.
        return JitteredRetryBackoff.DelayBeforeNextAttempt(
            interval,
            maxDelay,
            minimumDelay: interval,
            attemptCount: Math.Min(consecutiveFailureCount, int.MaxValue - 1) + 1);
    }
}
