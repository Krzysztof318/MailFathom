// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Scheduling;

namespace MailFathom.Domain.Delivery.Governance;

/// <summary>How much mail one period may be asked to send, for one account and for the whole deployment.</summary>
/// <remarks>
/// <para>
/// This is the bound that turns a fault above it into a refusal rather than into a provider suspending the account. A
/// rule matching more mail than its author expected and a caller looping on a send both produce the same thing — more
/// outgoing records than anybody meant — and a count per period is what stops that at a number an operator agreed to
/// instead of at whatever the submission server tolerates.
/// </para>
/// <para>
/// The period is a fixed window anchored at the Unix epoch rather than a rolling one, which is the shape the embedding
/// spend ceiling already uses and for the same reasons: a fixed window has a start an operator can name, a roll-over
/// instant a refused caller can be told to come back after, and one bounded count to answer it with, while a rolling
/// window would have to retain every send for the length of the window and would never name a moment at which the
/// refusal lifts.
/// </para>
/// <para>
/// A ceiling of zero is no ceiling at all, which is what an operator gets by writing nothing. It is a supported posture
/// rather than an unbounded one by omission: sending is off until an account is turned on, so an instance nobody
/// enabled is bounded by that, and the documentation states what a deployment with sending on and no ceiling is exposed
/// to.
/// </para>
/// </remarks>
public sealed class OutgoingMailCeilings
{
    private OutgoingMailCeilings(
        TimeSpan period,
        long maxMessagesPerAccount,
        long maxRecipientsPerAccount,
        long maxMessagesPerDeployment,
        long maxRecipientsPerDeployment)
    {
        this.Period = period;
        this.MaxMessagesPerAccount = maxMessagesPerAccount;
        this.MaxRecipientsPerAccount = maxRecipientsPerAccount;
        this.MaxMessagesPerDeployment = maxMessagesPerDeployment;
        this.MaxRecipientsPerDeployment = maxRecipientsPerDeployment;
    }

    /// <summary>Gets the ceilings of a deployment that declared none, which refuse nothing.</summary>
    public static OutgoingMailCeilings Unbounded { get; } = new(
        TimeSpan.FromDays(1),
        maxMessagesPerAccount: 0,
        maxRecipientsPerAccount: 0,
        maxMessagesPerDeployment: 0,
        maxRecipientsPerDeployment: 0);

    /// <summary>Gets the length of the window every count is taken over.</summary>
    public TimeSpan Period { get; }

    /// <summary>Gets the messages one account may be asked for in a period, or zero where the operator declared no ceiling.</summary>
    public long MaxMessagesPerAccount { get; }

    /// <summary>Gets the recipients one account may be asked to write to in a period, or zero where the operator declared no ceiling.</summary>
    public long MaxRecipientsPerAccount { get; }

    /// <summary>Gets the messages this deployment may be asked for in a period, or zero where the operator declared no ceiling.</summary>
    public long MaxMessagesPerDeployment { get; }

    /// <summary>Gets the recipients this deployment may be asked to write to in a period, or zero where the operator declared no ceiling.</summary>
    public long MaxRecipientsPerDeployment { get; }

    /// <summary>Gets whether these ceilings refuse nothing, which is what makes reading a period's usage unnecessary.</summary>
    public bool IsUnbounded => this.MaxMessagesPerAccount == 0
        && this.MaxRecipientsPerAccount == 0
        && this.MaxMessagesPerDeployment == 0
        && this.MaxRecipientsPerDeployment == 0;

    /// <summary>Builds the ceilings from what a deployment declared.</summary>
    /// <param name="period">The window every count is taken over.</param>
    /// <param name="maxMessagesPerAccount">The messages one account may be asked for in a period, or zero for no ceiling.</param>
    /// <param name="maxRecipientsPerAccount">The recipients one account may be asked to write to in a period, or zero for no ceiling.</param>
    /// <param name="maxMessagesPerDeployment">The messages this deployment may be asked for in a period, or zero for no ceiling.</param>
    /// <param name="maxRecipientsPerDeployment">The recipients this deployment may be asked to write to in a period, or zero for no ceiling.</param>
    /// <returns>The ceilings.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the period is not positive, or a ceiling is negative.</exception>
    public static OutgoingMailCeilings Create(
        TimeSpan period,
        long maxMessagesPerAccount,
        long maxRecipientsPerAccount,
        long maxMessagesPerDeployment,
        long maxRecipientsPerDeployment)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(period, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegative(maxMessagesPerAccount);
        ArgumentOutOfRangeException.ThrowIfNegative(maxRecipientsPerAccount);
        ArgumentOutOfRangeException.ThrowIfNegative(maxMessagesPerDeployment);
        ArgumentOutOfRangeException.ThrowIfNegative(maxRecipientsPerDeployment);

        return maxMessagesPerAccount == 0
            && maxRecipientsPerAccount == 0
            && maxMessagesPerDeployment == 0
            && maxRecipientsPerDeployment == 0
                ? Unbounded
                : new OutgoingMailCeilings(
                    period,
                    maxMessagesPerAccount,
                    maxRecipientsPerAccount,
                    maxMessagesPerDeployment,
                    maxRecipientsPerDeployment);
    }

    /// <summary>Finds the start of the period an instant falls in, which is the moment every count is taken since.</summary>
    /// <param name="instant">The moment to place in a period.</param>
    /// <returns>The period's start, in UTC.</returns>
    /// <remarks>
    /// Anchored at the Unix epoch so every process of a deployment, and every restart of one, agrees on where a period
    /// begins without anything having to be stored to say so.
    /// </remarks>
    public DateTimeOffset PeriodStartAt(DateTimeOffset instant) => EpochAnchoredPeriod.StartAt(this.Period, instant);

    /// <summary>Finds when the period an instant falls in rolls over, which is when a refused send may be asked for again.</summary>
    /// <param name="instant">The moment to place in a period.</param>
    /// <returns>The instant the next period begins, in UTC.</returns>
    public DateTimeOffset PeriodEndAt(DateTimeOffset instant) => EpochAnchoredPeriod.EndAt(this.Period, instant);

    /// <summary>Finds the ceiling one further message would carry a period past.</summary>
    /// <param name="usage">What the period has already been asked for.</param>
    /// <param name="recipientCount">The people the message being asked for names.</param>
    /// <returns>The ceiling reached, or <see langword="null" /> when the message is within every one of them.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="recipientCount" /> is not positive.</exception>
    /// <remarks>
    /// <para>
    /// The message is weighed rather than admitted on anything at all being left, which is the opposite of how the
    /// embedding ceiling treats a batch — and deliberately so, because the two overshoot differently. A batch's cost is
    /// known only after the provider answers, so weighing it would stall a deployment whose ceiling is smaller than one
    /// batch forever; a send's cost is its recipient count, which is known here, so admitting a message that carries the
    /// period past its ceiling would be an overshoot nothing needed to accept.
    /// </para>
    /// <para>
    /// The account's own ceilings are named before the deployment's, because the narrower bound is the one an operator
    /// acts on first and a message over both is over the account's whatever the deployment's says.
    /// </para>
    /// </remarks>
    public OutgoingMailCeiling? FindReachedCeiling(OutgoingMailUsage usage, int recipientCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(recipientCount);

        if (Exceeds(usage.AccountMessageCount + 1, this.MaxMessagesPerAccount))
        {
            return OutgoingMailCeiling.AccountMessages;
        }

        if (Exceeds(usage.AccountRecipientCount + recipientCount, this.MaxRecipientsPerAccount))
        {
            return OutgoingMailCeiling.AccountRecipients;
        }

        if (Exceeds(usage.DeploymentMessageCount + 1, this.MaxMessagesPerDeployment))
        {
            return OutgoingMailCeiling.DeploymentMessages;
        }

        return Exceeds(usage.DeploymentRecipientCount + recipientCount, this.MaxRecipientsPerDeployment)
            ? OutgoingMailCeiling.DeploymentRecipients
            : null;
    }

    /// <summary>Reports whether a total would stand above a ceiling a deployment declared.</summary>
    /// <remarks>A ceiling of zero is one nobody declared, so nothing is ever above it.</remarks>
    private static bool Exceeds(long total, long ceiling) => ceiling > 0 && total > ceiling;
}
