// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Scheduling;

namespace MailFathom.Application.Emails.AttachmentText.Limits;

/// <summary>What an instance is willing to spend on reading its mail's attachments, and the period it is counted over.</summary>
/// <remarks>
/// <para>
/// The ceilings <c>EmailAttachmentTextBounds</c> does not carry.  Those three bound one attachment, one message, and
/// one account run, which is what keeps any single unit of work small; none of them bounds how much a deployment reads
/// in a day, because a run that starts with a full budget starts one again on the next interval.  These are the
/// aggregate ceilings that do, and they are the shape <c>EmbeddingSpendBudget</c> already established for embedding:
/// one for the deployment, which bounds the bill, and one for any single owner, so a mailbox full of large attachments
/// cannot exhaust the window everybody else is working in.
/// </para>
/// <para>
/// Each step is counted in the unit that fits it rather than in one unit forced on both, which is what
/// <see cref="AttachmentDerivationStep" /> exists to say: extraction is octets a parser was handed, because a parse is
/// CPU and memory over a byte stream a stranger composed and no character count predicts it, and description is chat
/// calls, because that is what a provider prices.  Converting either into the other would produce a number that bounds
/// neither.
/// </para>
/// <para>
/// The period is the deployment's one budget window — the same length the embedding ceilings are counted over, so an
/// operator reads one roll-over instant rather than three.  It is a fixed window anchored at the Unix epoch, so every
/// process and every restart agrees on where a period begins with nothing stored to say so.
/// </para>
/// <para>
/// Configuration alone, and never part of an embedding profile.  A ceiling decides how much of a mailbox gets read and
/// changes the meaning of no stored passage, so raising one re-derives nothing.
/// </para>
/// </remarks>
public sealed class AttachmentDerivationBudget
{
    private AttachmentDerivationBudget(
        long maxInputOctetsPerPeriod,
        long maxInputOctetsPerPeriodPerOwner,
        long maxDescriptionsPerPeriod,
        long maxDescriptionsPerPeriodPerOwner,
        TimeSpan period)
    {
        this.MaxInputOctetsPerPeriod = maxInputOctetsPerPeriod;
        this.MaxInputOctetsPerPeriodPerOwner = maxInputOctetsPerPeriodPerOwner;
        this.MaxDescriptionsPerPeriod = maxDescriptionsPerPeriod;
        this.MaxDescriptionsPerPeriodPerOwner = maxDescriptionsPerPeriodPerOwner;
        this.Period = period;
    }

    /// <summary>Gets a budget that refuses nothing, which is what an operator writing ceilings of zero asked for.</summary>
    public static AttachmentDerivationBudget Unbounded { get; } = new(
        maxInputOctetsPerPeriod: 0,
        maxInputOctetsPerPeriodPerOwner: 0,
        maxDescriptionsPerPeriod: 0,
        maxDescriptionsPerPeriodPerOwner: 0,
        TimeSpan.FromDays(1));

    /// <summary>Gets the octets one period may read out of attachments in total, or zero where no ceiling was declared.</summary>
    public long MaxInputOctetsPerPeriod { get; }

    /// <summary>Gets the octets one period may read for any one owner, or zero where no per-owner ceiling was declared.</summary>
    public long MaxInputOctetsPerPeriodPerOwner { get; }

    /// <summary>Gets the image descriptions one period may ask a provider for in total, or zero where no ceiling was declared.</summary>
    public long MaxDescriptionsPerPeriod { get; }

    /// <summary>Gets the image descriptions one period may ask for on behalf of any one owner, or zero where none was declared.</summary>
    public long MaxDescriptionsPerPeriodPerOwner { get; }

    /// <summary>Gets the length of the window every ceiling here is counted over.</summary>
    public TimeSpan Period { get; }

    /// <summary>Gets whether this budget refuses nothing at all.</summary>
    public bool IsUnbounded => this.MaxInputOctetsPerPeriod == 0
        && this.MaxInputOctetsPerPeriodPerOwner == 0
        && this.MaxDescriptionsPerPeriod == 0
        && this.MaxDescriptionsPerPeriodPerOwner == 0;

    /// <summary>Builds a budget from what a deployment declared.</summary>
    /// <param name="maxInputOctetsPerPeriod">The octets one period may read in total, or zero for no ceiling.</param>
    /// <param name="maxInputOctetsPerPeriodPerOwner">The octets one period may read for any one owner, or zero for no ceiling.</param>
    /// <param name="maxDescriptionsPerPeriod">The descriptions one period may ask for in total, or zero for no ceiling.</param>
    /// <param name="maxDescriptionsPerPeriodPerOwner">The descriptions one period may ask for per owner, or zero for no ceiling.</param>
    /// <param name="period">The window the ceilings are counted over.</param>
    /// <returns>The budget.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when any ceiling is negative, or the period is not positive.</exception>
    public static AttachmentDerivationBudget Create(
        long maxInputOctetsPerPeriod,
        long maxInputOctetsPerPeriodPerOwner,
        long maxDescriptionsPerPeriod,
        long maxDescriptionsPerPeriodPerOwner,
        TimeSpan period)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxInputOctetsPerPeriod);
        ArgumentOutOfRangeException.ThrowIfNegative(maxInputOctetsPerPeriodPerOwner);
        ArgumentOutOfRangeException.ThrowIfNegative(maxDescriptionsPerPeriod);
        ArgumentOutOfRangeException.ThrowIfNegative(maxDescriptionsPerPeriodPerOwner);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(period, TimeSpan.Zero);

        var budget = new AttachmentDerivationBudget(
            maxInputOctetsPerPeriod,
            maxInputOctetsPerPeriodPerOwner,
            maxDescriptionsPerPeriod,
            maxDescriptionsPerPeriodPerOwner,
            period);

        return budget.IsUnbounded ? Unbounded : budget;
    }

    /// <summary>Reads the ceiling one step is bounded by, for the deployment or for any one owner.</summary>
    /// <param name="derivationStep">The step being bounded.</param>
    /// <param name="forOwner"><see langword="true" /> for the per-owner ceiling, <see langword="false" /> for the deployment's.</param>
    /// <returns>The ceiling, or zero where the deployment declared none for that step and scope.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="derivationStep" /> is not a member of the set.</exception>
    public long CeilingFor(AttachmentDerivationStep derivationStep, bool forOwner) => derivationStep switch
    {
        AttachmentDerivationStep.Extraction => forOwner
            ? this.MaxInputOctetsPerPeriodPerOwner
            : this.MaxInputOctetsPerPeriod,
        AttachmentDerivationStep.Description => forOwner
            ? this.MaxDescriptionsPerPeriodPerOwner
            : this.MaxDescriptionsPerPeriod,
        _ => throw new ArgumentOutOfRangeException(nameof(derivationStep), derivationStep, "The step names no attachment derivation ceiling."),
    };

    /// <summary>Finds the start of the period an instant falls in, which is the key a consumed total is counted under.</summary>
    /// <param name="instant">The moment to place in a period.</param>
    /// <returns>The period's start, in UTC.</returns>
    public DateTimeOffset PeriodStartAt(DateTimeOffset instant) => EpochAnchoredPeriod.StartAt(this.Period, instant);
}
