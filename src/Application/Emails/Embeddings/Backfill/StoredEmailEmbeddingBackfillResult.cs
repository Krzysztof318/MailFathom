// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Embeddings.Backfill;

/// <summary>What one run of the embedding backfill did, and why it ended.</summary>
/// <param name="Outcome">Why the run ended.</param>
/// <param name="ChunkedEmailCount">How many messages this run had to cut into passages before anything could be embedded.</param>
/// <param name="EmbeddedEmailCount">How many messages this run brought up to date, or spent a provider call on.</param>
/// <param name="EmbeddedChunkCount">How many passages this run committed vectors for.</param>
/// <param name="CallBudgetExhaustedEmailCount">How many messages this run left part-way through because one turn spent every provider call a turn is allowed.</param>
/// <param name="OutstandingEmailCountAtSweepStart">How many messages awaited embedding when this sweep began, or <see langword="null" /> when the run resumed a sweep somebody else measured.</param>
/// <param name="Failure">Why a provider call produced nothing, present exactly when <paramref name="Outcome" /> is <see cref="StoredEmailEmbeddingBackfillOutcome.ProviderFailed" />.</param>
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
/// </remarks>
public sealed record StoredEmailEmbeddingBackfillResult(
    StoredEmailEmbeddingBackfillOutcome Outcome,
    int ChunkedEmailCount,
    int EmbeddedEmailCount,
    int EmbeddedChunkCount,
    int CallBudgetExhaustedEmailCount,
    int? OutstandingEmailCountAtSweepStart,
    EmbeddingGenerationFailure? Failure)
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
        OutstandingEmailCountAtSweepStart: null,
        Failure: null);

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
