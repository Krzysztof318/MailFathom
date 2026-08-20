// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Scheduling;

namespace MailFathom.Domain.Delivery.Governance;

/// <summary>How much mail one caller may ask this deployment for inside one period.</summary>
/// <remarks>
/// <para>
/// <see cref="OutgoingMailCeilings" /> bounds the deployment and every account of it; this bounds one client. The two
/// answer different faults and neither substitutes for the other: an installation whose whole allowance is a hundred
/// messages a day has said nothing about one agent spending all hundred in the four minutes before anybody notices, and
/// that is what a caller looping on a send does.
/// </para>
/// <para>
/// The window is the same fixed one anchored at the Unix epoch, and deliberately the same window the deployment's own
/// ceilings are counted over: two bounds on one send that rolled over at different instants would refuse a caller for a
/// reason it could not be told how to wait out.
/// </para>
/// <para>
/// A ceiling of zero is no ceiling, which is what an operator gets by writing nothing. That is a posture rather than an
/// oversight — sending is off until an account is turned on, and the deployment's own ceilings still bound whatever a
/// caller is admitted to ask for.
/// </para>
/// </remarks>
public sealed class AuthoredSendCeilings
{
    private AuthoredSendCeilings(TimeSpan period, long maxMessagesPerCaller, long maxRecipientsPerCaller)
    {
        this.Period = period;
        this.MaxMessagesPerCaller = maxMessagesPerCaller;
        this.MaxRecipientsPerCaller = maxRecipientsPerCaller;
    }

    /// <summary>Gets the ceilings of a deployment that bounded no caller, which refuse nothing.</summary>
    public static AuthoredSendCeilings Unbounded { get; } = new(
        TimeSpan.FromDays(1),
        maxMessagesPerCaller: 0,
        maxRecipientsPerCaller: 0);

    /// <summary>Gets the length of the window a caller's own counts are taken over.</summary>
    public TimeSpan Period { get; }

    /// <summary>Gets the messages one caller may ask for in a period, or zero where the operator declared no ceiling.</summary>
    public long MaxMessagesPerCaller { get; }

    /// <summary>Gets the people one caller may ask this deployment to write to in a period, or zero where the operator declared no ceiling.</summary>
    public long MaxRecipientsPerCaller { get; }

    /// <summary>Gets whether these ceilings refuse nothing, which is what makes counting a caller's sends unnecessary.</summary>
    public bool IsUnbounded => this.MaxMessagesPerCaller == 0 && this.MaxRecipientsPerCaller == 0;

    /// <summary>Builds the ceilings from what a deployment declared about one caller.</summary>
    /// <param name="period">The window a caller's counts are taken over.</param>
    /// <param name="maxMessagesPerCaller">The messages one caller may ask for in a period, or zero for no ceiling.</param>
    /// <param name="maxRecipientsPerCaller">The people one caller may ask this deployment to write to in a period, or zero for no ceiling.</param>
    /// <returns>The ceilings.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the period is not positive, or a ceiling is negative.</exception>
    public static AuthoredSendCeilings Create(
        TimeSpan period,
        long maxMessagesPerCaller,
        long maxRecipientsPerCaller)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(period, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegative(maxMessagesPerCaller);
        ArgumentOutOfRangeException.ThrowIfNegative(maxRecipientsPerCaller);

        return maxMessagesPerCaller == 0 && maxRecipientsPerCaller == 0
            ? Unbounded
            : new AuthoredSendCeilings(period, maxMessagesPerCaller, maxRecipientsPerCaller);
    }

    /// <summary>Finds the start of the period an instant falls in, which is the moment a caller's counts are taken since.</summary>
    /// <param name="instant">The moment to place in a period.</param>
    /// <returns>The period's start, in UTC.</returns>
    /// <remarks>
    /// Anchored at the Unix epoch, so a period has a start an operator can name and a roll-over instant a refused caller
    /// can be told to come back after, with nothing stored to say where either is.
    /// </remarks>
    public DateTimeOffset PeriodStartAt(DateTimeOffset instant) => EpochAnchoredPeriod.StartAt(this.Period, instant);

    /// <summary>Finds the ceiling one further message would carry a caller's period past.</summary>
    /// <param name="usage">What the caller has already been admitted for in the period.</param>
    /// <param name="recipientCount">The people the message being asked for names.</param>
    /// <returns>The ceiling reached, or <see langword="null" /> when the message is within both of them.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="recipientCount" /> is not positive.</exception>
    /// <remarks>
    /// The message is weighed rather than admitted on anything at all being left, for the reason the deployment's own
    /// ceilings weigh one: a send's cost is its recipient count and that is known here, so admitting a message that
    /// carries the period past its ceiling would be an overshoot nothing needed to accept.
    /// </remarks>
    public AuthoredSendCeiling? FindReachedCeiling(AuthoredSendUsage usage, int recipientCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(recipientCount);

        if (Exceeds(usage.MessageCount + 1, this.MaxMessagesPerCaller))
        {
            return AuthoredSendCeiling.CallerMessages;
        }

        return Exceeds(usage.RecipientCount + recipientCount, this.MaxRecipientsPerCaller)
            ? AuthoredSendCeiling.CallerRecipients
            : null;
    }

    /// <summary>Reports whether a total would stand above a ceiling a deployment declared.</summary>
    /// <remarks>A ceiling of zero is one nobody declared, so nothing is ever above it.</remarks>
    private static bool Exceeds(long total, long ceiling) => ceiling > 0 && total > ceiling;
}
