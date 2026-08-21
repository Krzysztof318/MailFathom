// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;

namespace MailFathom.Application.Resilience;

/// <summary>Decides how long a unit of durable work waits before it is attempted again after a transient failure.</summary>
/// <remarks>
/// <para>
/// This is a scheduler's own backoff, and it is a different decision from the one an adapter's resilience pipeline
/// makes. The pipeline decides whether one call to a dependency is worth repeating within a single attempt and has
/// already spent that budget by the time the work raises; this decides when the whole unit is worth attempting again,
/// minutes later and in a process that need not be the one that failed. The two never wrap each other, which is what
/// keeps the attempt counts from multiplying into a retry storm.
/// </para>
/// <para>
/// Every backoff curve in this system is drawn here — the durable job queue, the outbox, one account's synchronization
/// run, and a conflicted local commit — because each answers that same question about work of its own, and a second
/// implementation of it would be a second set of jitter arithmetic to get subtly wrong. What the callers differ on is
/// the arguments: the bounds of the curve, and whether a delay has a floor of its own to respect.
/// </para>
/// <para>
/// The delay doubles per attempt and is drawn from a range rather than computed exactly. Work that failed together
/// failed on the same dependency, and an exact delay would return every one of them to it in the same instant on every
/// later attempt. Half the ceiling is the floor of the draw, so a delay always at least halves the rate of approach
/// while never exceeding the configured maximum.
/// </para>
/// </remarks>
public static class JitteredRetryBackoff
{
    /// <summary>Bounds the exponent so the doubling stays inside the tick arithmetic that applies it.</summary>
    /// <remarks>Growth has reached any usable ceiling long before this, so the bound costs nothing an operator can observe.</remarks>
    private const int MaxGrowthSteps = 16;

    /// <summary>How finely a delay is drawn from its range.</summary>
    private const int JitterResolution = 1000;

    /// <summary>Computes how long the work waits before its next attempt.</summary>
    /// <param name="baseDelay">The delay the first retry is drawn around, from which the doubling grows.</param>
    /// <param name="maxDelay">The ceiling a grown delay never exceeds.</param>
    /// <param name="minimumDelay">The delay no draw falls below, which is <see cref="TimeSpan.Zero" /> for work that has no wait of its own to respect.</param>
    /// <param name="attemptCount">How many attempts the work has already been handed out for, counting from one.</param>
    /// <returns>A jittered delay of at least half the grown ceiling, never below <paramref name="minimumDelay" />, and at most <paramref name="maxDelay" />.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="baseDelay" /> is not positive, when <paramref name="maxDelay" /> is below it, when <paramref name="minimumDelay" /> is negative or above <paramref name="baseDelay" />, or when <paramref name="attemptCount" /> is not positive.</exception>
    /// <remarks>
    /// The floor is bounded by the base delay rather than by the ceiling, because a floor above the range the first
    /// attempt draws from is a caller stating two delays that contradict each other rather than one curve with a lower
    /// bound.
    /// </remarks>
    public static TimeSpan DelayBeforeNextAttempt(
        TimeSpan baseDelay,
        TimeSpan maxDelay,
        TimeSpan minimumDelay,
        int attemptCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(baseDelay, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxDelay, baseDelay);
        ArgumentOutOfRangeException.ThrowIfLessThan(minimumDelay, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minimumDelay, baseDelay);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(attemptCount);

        var ceilingTicks = GrowFromBaseDelay(baseDelay.Ticks, maxDelay.Ticks, attemptCount - 1);
        var floorTicks = Math.Max(minimumDelay.Ticks, ceilingTicks / 2);

        return TimeSpan.FromTicks(floorTicks + DrawJitterTicks(ceilingTicks - floorTicks));
    }

    /// <summary>Doubles the base delay once per attempt already made and caps the result.</summary>
    /// <remarks>
    /// The comparison shifts the ceiling down rather than shifting the base delay up, so the product that would exceed
    /// the ceiling — and, for a large enough attempt count, the tick range itself — is never computed.
    /// </remarks>
    private static long GrowFromBaseDelay(long baseDelayTicks, long maxDelayTicks, int completedAttempts)
    {
        var growthSteps = Math.Min(completedAttempts, MaxGrowthSteps);

        return baseDelayTicks <= maxDelayTicks >> growthSteps
            ? baseDelayTicks << growthSteps
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
