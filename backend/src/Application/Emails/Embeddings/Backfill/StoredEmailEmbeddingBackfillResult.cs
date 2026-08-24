// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings.Limits;

namespace MailFathom.Application.Emails.Embeddings.Backfill;

/// <summary>What one run of the embedding backfill did, and why it ended.</summary>
/// <param name="Outcome">Why the run ended.</param>
/// <param name="ChunkedEmailCount">How many messages this run had to cut into passages before anything could be embedded.</param>
/// <param name="EmbeddedEmailCount">How many messages this run brought up to date, or spent a provider call on.</param>
/// <param name="EmbeddedChunkCount">How many passages this run committed vectors for.</param>
/// <param name="CallBudgetExhaustedEmailCount">How many messages this run left part-way through because one turn spent every provider call a turn is allowed.</param>
/// <param name="OwnerSpendCeilingEmailCount">How many messages this run stepped past because the owner they belong to had spent what one period admits for them.</param>
/// <param name="OwnerSpendPeriodEndsAt">When the period those owners had spent rolls over, present exactly when <paramref name="OwnerSpendCeilingEmailCount" /> is greater than zero.</param>
/// <param name="OutstandingEmailCountAtSweepStart">How many messages awaited embedding when this sweep began, or <see langword="null" /> when the run resumed a sweep somebody else measured.</param>
/// <param name="Failure">Why a provider call produced nothing, present exactly when <paramref name="Outcome" /> is <see cref="StoredEmailEmbeddingBackfillOutcome.ProviderFailed" />.</param>
/// <param name="SpendPeriodEndsAt">When the budget period rolls over, present exactly when <paramref name="Outcome" /> is <see cref="StoredEmailEmbeddingBackfillOutcome.SpendCeilingReached" />.</param>
/// <param name="ReachedSpendBound">Which of the two spend ceilings ended the run, <see cref="EmbeddingSpendBound.None" /> unless one did.</param>
/// <remarks>
/// <para>
/// Counts and classifications only. Every message this run touched, every passage it cut, and every vector it stored is
/// mail content or derived from it, and none of that belongs in something a worker logs.
/// </para>
/// <para>
/// The exhausted count is carried separately rather than folded into the ordinary progress, because it is the one thing
/// a run does that an operator has to act on and that no other number here would show: the walk steps past such a
/// message and keeps going, so without its own count a mailbox needing several sweeps to finish one message would look
/// exactly like one that is finishing them.
/// </para>
/// <para>
/// The owner-ceiling count carries the period it belongs to for a different reason: the run does not end on it, so the
/// worker reporting it sees the same fact on every pass until the period rolls over. Naming the period is what lets the
/// worker say it once, the way the live worker already does.
/// </para>
/// </remarks>
public sealed record StoredEmailEmbeddingBackfillResult(
    StoredEmailEmbeddingBackfillOutcome Outcome,
    int ChunkedEmailCount,
    int EmbeddedEmailCount,
    int EmbeddedChunkCount,
    int CallBudgetExhaustedEmailCount,
    int OwnerSpendCeilingEmailCount,
    DateTimeOffset? OwnerSpendPeriodEndsAt,
    int? OutstandingEmailCountAtSweepStart,
    EmbeddingGenerationFailure? Failure,
    DateTimeOffset? SpendPeriodEndsAt,
    EmbeddingSpendBound ReachedSpendBound = EmbeddingSpendBound.None)
{
    /// <summary>The pass an instance that has registered no generation performs, which reaches nothing and spends nothing.</summary>
    /// <remarks>
    /// Reported by whatever decides which generation a pass walks towards rather than by the walk, which is never
    /// started without one. It is a result rather than an absence of one because the worker records and paces every
    /// pass alike, and a pass that found no generation is a fact about the instance an operator reads here.
    /// </remarks>
    public static StoredEmailEmbeddingBackfillResult NoActiveProfile { get; } = new(
        StoredEmailEmbeddingBackfillOutcome.NoActiveProfile,
        ChunkedEmailCount: 0,
        EmbeddedEmailCount: 0,
        EmbeddedChunkCount: 0,
        CallBudgetExhaustedEmailCount: 0,
        OwnerSpendCeilingEmailCount: 0,
        OwnerSpendPeriodEndsAt: null,
        OutstandingEmailCountAtSweepStart: null,
        Failure: null,
        SpendPeriodEndsAt: null);

    /// <summary>Gets whether running again shortly would reach work this run could not.</summary>
    /// <remarks>
    /// <para>
    /// A completed sweep has nothing in front of it, and the two profile refusals are settled by an operator rather than
    /// by asking again, so none of the three is improved by a shorter wait.
    /// </para>
    /// <para>
    /// A provider failure depends on which one. A rate limit, a timeout, and a transport fault are remote conditions to
    /// wait out, and the pause before the next run is the backoff. The other three are terminal: a rejected credential,
    /// a refused request, and a vector the declared geometry does not describe all answer a repetition identically while
    /// counting against the account's request budget, so asking again in half a minute buys the same refusal at the same
    /// price. The sweep still reaches those messages later, because it starts again from the beginning.
    /// </para>
    /// <para>
    /// A reached spend ceiling answers <see langword="false" /> here and is paced by neither interval. It is the one
    /// ending that names the instant it stops applying, so the worker waits for that instead — a short interval would
    /// re-read a ceiling already known to bind, and the long one would leave a rolled-over period idle for as much as a
    /// quarter of an hour.
    /// </para>
    /// </remarks>
    public bool MoreWorkIsWorthTryingSoon => this.Outcome switch
    {
        StoredEmailEmbeddingBackfillOutcome.BatchBudgetSpent => true,
        StoredEmailEmbeddingBackfillOutcome.ProviderFailed => this.Failure
            is EmbeddingGenerationFailure.RateLimited
            or EmbeddingGenerationFailure.RequestTimedOut
            or EmbeddingGenerationFailure.TransportFaulted,
        _ => false,
    };
}
