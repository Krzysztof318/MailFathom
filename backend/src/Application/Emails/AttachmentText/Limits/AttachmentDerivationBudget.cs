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
/// one for the deployment, which bounds the bill, and one for any single user, so a mailbox full of large attachments
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
        long maxInputOctetsPerPeriodPerUser,
        long maxDescriptionsPerPeriod,
        long maxDescriptionsPerPeriodPerUser,
        TimeSpan period)
    {
        this.MaxInputOctetsPerPeriod = maxInputOctetsPerPeriod;
        this.MaxInputOctetsPerPeriodPerUser = maxInputOctetsPerPeriodPerUser;
        this.MaxDescriptionsPerPeriod = maxDescriptionsPerPeriod;
        this.MaxDescriptionsPerPeriodPerUser = maxDescriptionsPerPeriodPerUser;
        this.Period = period;
    }

    /// <summary>Gets a budget that refuses nothing, over the default window a deployment declaring no period gets.</summary>
    /// <remarks>
    /// A convenience for a composition that declares nothing at all, and for a test that is not about the window.
    /// A deployment reaches <see cref="Create" /> instead, which keeps whatever period was configured even where every
    /// ceiling is zero, because the period is reported to an operator whether or not anything is counted against it.
    /// </remarks>
    public static AttachmentDerivationBudget Unbounded { get; } = new(
        maxInputOctetsPerPeriod: 0,
        maxInputOctetsPerPeriodPerUser: 0,
        maxDescriptionsPerPeriod: 0,
        maxDescriptionsPerPeriodPerUser: 0,
        TimeSpan.FromDays(1));

    /// <summary>Gets the octets one period may read out of attachments in total, or zero where no ceiling was declared.</summary>
    public long MaxInputOctetsPerPeriod { get; }

    /// <summary>Gets the octets one period may read for any one user, or zero where no per-user ceiling was declared.</summary>
    public long MaxInputOctetsPerPeriodPerUser { get; }

    /// <summary>Gets the image descriptions one period may ask a provider for in total, or zero where no ceiling was declared.</summary>
    public long MaxDescriptionsPerPeriod { get; }

    /// <summary>Gets the image descriptions one period may ask for on behalf of any one user, or zero where none was declared.</summary>
    public long MaxDescriptionsPerPeriodPerUser { get; }

    /// <summary>Gets the length of the window every ceiling here is counted over.</summary>
    public TimeSpan Period { get; }

    /// <summary>Gets whether this budget refuses nothing at all.</summary>
    public bool IsUnbounded => this.MaxInputOctetsPerPeriod == 0
        && this.MaxInputOctetsPerPeriodPerUser == 0
        && this.MaxDescriptionsPerPeriod == 0
        && this.MaxDescriptionsPerPeriodPerUser == 0;

    /// <summary>Builds a budget from what a deployment declared.</summary>
    /// <param name="maxInputOctetsPerPeriod">The octets one period may read in total, or zero for no ceiling.</param>
    /// <param name="maxInputOctetsPerPeriodPerUser">The octets one period may read for any one user, or zero for no ceiling.</param>
    /// <param name="maxDescriptionsPerPeriod">The descriptions one period may ask for in total, or zero for no ceiling.</param>
    /// <param name="maxDescriptionsPerPeriodPerUser">The descriptions one period may ask for per user, or zero for no ceiling.</param>
    /// <param name="period">The window the ceilings are counted over.</param>
    /// <returns>The budget.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when any ceiling is negative, or the period is not positive.</exception>
    public static AttachmentDerivationBudget Create(
        long maxInputOctetsPerPeriod,
        long maxInputOctetsPerPeriodPerUser,
        long maxDescriptionsPerPeriod,
        long maxDescriptionsPerPeriodPerUser,
        TimeSpan period)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxInputOctetsPerPeriod);
        ArgumentOutOfRangeException.ThrowIfNegative(maxInputOctetsPerPeriodPerUser);
        ArgumentOutOfRangeException.ThrowIfNegative(maxDescriptionsPerPeriod);
        ArgumentOutOfRangeException.ThrowIfNegative(maxDescriptionsPerPeriodPerUser);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(period, TimeSpan.Zero);

        // The caller's period is kept whether or not any ceiling was declared, so the singleton above is a default
        // rather than a substitute. A deployment that declared no ceiling still reports a period, and reporting the
        // singleton's own day where the deployment configured something else would put a roll-over instant on the
        // screen that disagrees with the embedding ceiling's — the one thing this window exists to keep identical.
        return new AttachmentDerivationBudget(
            maxInputOctetsPerPeriod,
            maxInputOctetsPerPeriodPerUser,
            maxDescriptionsPerPeriod,
            maxDescriptionsPerPeriodPerUser,
            period);
    }

    /// <summary>Reads the ceiling one step is bounded by, for the deployment or for any one user.</summary>
    /// <param name="derivationStep">The step being bounded.</param>
    /// <param name="forUser"><see langword="true" /> for the per-user ceiling, <see langword="false" /> for the deployment's.</param>
    /// <returns>The ceiling, or zero where the deployment declared none for that step and scope.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="derivationStep" /> is not a member of the set.</exception>
    public long CeilingFor(AttachmentDerivationStep derivationStep, bool forUser) => derivationStep switch
    {
        AttachmentDerivationStep.Extraction => forUser
            ? this.MaxInputOctetsPerPeriodPerUser
            : this.MaxInputOctetsPerPeriod,
        AttachmentDerivationStep.Description => forUser
            ? this.MaxDescriptionsPerPeriodPerUser
            : this.MaxDescriptionsPerPeriod,
        _ => throw new ArgumentOutOfRangeException(nameof(derivationStep), derivationStep, "The step names no attachment derivation ceiling."),
    };

    /// <summary>Finds the start of the period an instant falls in, which is the key a consumed total is counted under.</summary>
    /// <param name="instant">The moment to place in a period.</param>
    /// <returns>The period's start, in UTC.</returns>
    public DateTimeOffset PeriodStartAt(DateTimeOffset instant) => EpochAnchoredPeriod.StartAt(this.Period, instant);
}
