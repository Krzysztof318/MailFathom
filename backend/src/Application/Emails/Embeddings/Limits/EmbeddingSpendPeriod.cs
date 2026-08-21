// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Embeddings.Limits;

/// <summary>What one budget period has spent so far, and what it still admits.</summary>
/// <param name="StartsAt">When the period began, which is the key its total is counted under.</param>
/// <param name="EndsAt">When the period rolls over, which is the instant paused work resumes at.</param>
/// <param name="ConsumedInputCharacterCount">The characters already sent to a provider inside this period.</param>
/// <param name="CeilingInputCharacterCount">The characters the period admits, or <see langword="null" /> where the deployment declared no ceiling.</param>
/// <remarks>
/// <para>
/// This is the value the whole ceiling is readable through: a worker asks it whether to start, a paused one asks it
/// when to wake, and an activation asks it whether the estimate it is about to confirm fits in what is left. Counts and
/// instants only — no message, passage, or vector is describable from it.
/// </para>
/// <para>
/// A ceiling of <see langword="null" /> is a deployment that declared none, which is a supported state rather than an
/// unbounded one by omission: it is what an operator writing a ceiling of zero asked for, and the documentation says
/// what it costs.
/// </para>
/// </remarks>
public sealed record EmbeddingSpendPeriod(
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    long ConsumedInputCharacterCount,
    long? CeilingInputCharacterCount)
{
    /// <summary>Gets the characters this period still admits, or <see langword="null" /> where nothing is being counted against.</summary>
    /// <remarks>Never negative: a batch that crossed the ceiling is paid for, and what is left after it is nothing rather than a debt.</remarks>
    public long? RemainingInputCharacterCount => this.CeilingInputCharacterCount is { } ceiling
        ? Math.Max(0, ceiling - this.ConsumedInputCharacterCount)
        : null;

    /// <summary>Gets whether this period has reached its ceiling and admits no further request.</summary>
    public bool IsExhausted => this.CeilingInputCharacterCount is { } ceiling
        && this.ConsumedInputCharacterCount >= ceiling;

    /// <summary>Gets whether a request may be sent under this period.</summary>
    /// <remarks>
    /// Asked of the period rather than of the request, because a batch is admitted whenever anything at all is left and
    /// is then paid for whole. Weighing the batch against what remains instead would stall a deployment whose ceiling is
    /// smaller than one batch forever — it would refuse the same request at every roll-over — and the overshoot the
    /// simpler rule allows is bounded by one batch per concurrent call, which is the tolerance the configuration
    /// reference states.
    /// </remarks>
    public bool AdmitsRequest => !this.IsExhausted;
}
