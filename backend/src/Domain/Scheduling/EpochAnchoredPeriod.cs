// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Scheduling;

/// <summary>Places an instant in the fixed window of a given length that the Unix epoch anchors.</summary>
/// <remarks>
/// <para>
/// Anchoring at the epoch is what lets every process of a deployment, and every restart of one, agree on where a period
/// begins without anything having to be stored to say so: a boundary is a function of the clock and the period alone.
/// </para>
/// <para>
/// The division is floored rather than truncated, so an instant before the epoch — which no clock this runs on reports,
/// but which a test may hand it — lands on the start of its period rather than on the end of the one before.
/// </para>
/// <para>
/// <c>JobRecurrence</c> deliberately does not use this, and is not a fifth caller waiting to be folded in. It truncates
/// instead, and answers <see langword="null" /> for the latest occurrence and <c>-1</c> for the index of an instant
/// before the epoch rather than placing it in a period at all; its own tests pin both.
/// </para>
/// </remarks>
public static class EpochAnchoredPeriod
{
    /// <summary>Finds the start of the period an instant falls in.</summary>
    /// <param name="period">How long one period lasts.</param>
    /// <param name="instant">The moment to place in a period.</param>
    /// <returns>The period's start, in UTC.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="period" /> is not positive.</exception>
    public static DateTimeOffset StartAt(TimeSpan period, DateTimeOffset instant)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(period, TimeSpan.Zero);

        var elapsedTicks = instant.ToUniversalTime().Ticks - DateTimeOffset.UnixEpoch.Ticks;
        var periodsElapsed = (long)Math.Floor((double)elapsedTicks / period.Ticks);

        return DateTimeOffset.UnixEpoch.AddTicks(periodsElapsed * period.Ticks);
    }

    /// <summary>Finds when the period an instant falls in rolls over.</summary>
    /// <param name="period">How long one period lasts.</param>
    /// <param name="instant">The moment to place in a period.</param>
    /// <returns>The instant the next period begins, in UTC.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="period" /> is not positive.</exception>
    public static DateTimeOffset EndAt(TimeSpan period, DateTimeOffset instant) => StartAt(period, instant) + period;
}
