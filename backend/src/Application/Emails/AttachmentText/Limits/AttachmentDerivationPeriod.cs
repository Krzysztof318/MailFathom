// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.AttachmentText.Limits;

/// <summary>What one budget period has spent on one step of reading attachments, and what it still admits.</summary>
/// <param name="Step">Which step the figures below belong to, which is what says the unit they are counted in.</param>
/// <param name="StartsAt">When the period began, which is the key its total is counted under.</param>
/// <param name="EndsAt">When it rolls over, which is the instant paused work resumes at.</param>
/// <param name="ConsumedUnitCount">What the step has already consumed inside this period, in that step's own unit.</param>
/// <param name="CeilingUnitCount">What the period admits, or <see langword="null" /> where the deployment declared no ceiling.</param>
/// <remarks>
/// <para>
/// The shape <c>EmbeddingSpendPeriod</c> already has, kept apart from it rather than shared because the unit differs by
/// step: a member named for characters would be lying about octets and about calls. The step travels with the figures
/// for exactly that reason — a count with no unit is a number an operator cannot act on.
/// </para>
/// <para>
/// Counts and instants only. No message, attachment, file name, or word is describable from it, which is what makes it
/// safe to serve from the administrative endpoint and to write into a log line.
/// </para>
/// </remarks>
public sealed record AttachmentDerivationPeriod(
    AttachmentDerivationStep Step,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    long ConsumedUnitCount,
    long? CeilingUnitCount)
{
    /// <summary>Gets what this period still admits, or <see langword="null" /> where nothing is being counted against.</summary>
    /// <remarks>Never negative: a message that crossed the ceiling is paid for, and what is left after it is nothing rather than a debt.</remarks>
    public long? RemainingUnitCount => this.CeilingUnitCount is { } ceiling
        ? Math.Max(0, ceiling - this.ConsumedUnitCount)
        : null;

    /// <summary>Gets whether this period has reached its ceiling and admits no further work.</summary>
    public bool IsExhausted => this.CeilingUnitCount is { } ceiling && this.ConsumedUnitCount >= ceiling;

    /// <summary>Gets whether the step may take another unit of work under this period.</summary>
    /// <remarks>
    /// Asked of the period rather than of the work, exactly as the embedding ceiling is asked: a message is admitted
    /// whenever anything at all is left and is then paid for whole. Weighing the message against what remains instead
    /// would stall a deployment whose ceiling is smaller than one message for ever, refusing the same message at every
    /// roll-over, and the overshoot the simpler rule allows is bounded by the per-message ceiling that already applies.
    /// </remarks>
    public bool AdmitsWork => !this.IsExhausted;
}
