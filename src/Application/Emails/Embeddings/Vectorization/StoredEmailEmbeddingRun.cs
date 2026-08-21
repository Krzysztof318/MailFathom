// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Embeddings.Vectorization;

/// <summary>What one message's turn at being embedded produced.</summary>
/// <remarks>
/// Counts and classifications only. The message identity, its passages, and the vectors they became are all mail
/// content or derived from it, and none of them belongs in something a worker logs.
/// </remarks>
/// <param name="Outcome">How the turn ended.</param>
/// <param name="EmbeddedChunkCount">How many passages were given a vector and committed during this turn.</param>
/// <param name="InputCharacterCount">How many characters this turn sent to a provider, which is what its spend was counted in.</param>
/// <param name="Failure">Why a provider call produced nothing, present exactly when <paramref name="Outcome" /> is <see cref="StoredEmailEmbeddingOutcome.ProviderFailed" />.</param>
/// <param name="SpendPeriodEndsAt">When the budget period rolls over, present exactly when <paramref name="Outcome" /> is <see cref="StoredEmailEmbeddingOutcome.SpendCeilingReached" />.</param>
public sealed record StoredEmailEmbeddingRun(
    StoredEmailEmbeddingOutcome Outcome,
    int EmbeddedChunkCount,
    int InputCharacterCount,
    EmbeddingGenerationFailure? Failure,
    DateTimeOffset? SpendPeriodEndsAt)
{
    /// <summary>Reports a message every passage of which now carries a vector.</summary>
    /// <param name="embeddedChunkCount">How many passages this turn produced a vector for.</param>
    /// <param name="inputCharacterCount">How many characters the turn sent to produce them.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a count is negative.</exception>
    public static StoredEmailEmbeddingRun Embedded(int embeddedChunkCount, int inputCharacterCount = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(embeddedChunkCount);
        ArgumentOutOfRangeException.ThrowIfNegative(inputCharacterCount);

        return new StoredEmailEmbeddingRun(
            StoredEmailEmbeddingOutcome.Embedded,
            embeddedChunkCount,
            inputCharacterCount,
            Failure: null,
            SpendPeriodEndsAt: null);
    }

    /// <summary>Reports a message whose turn spent every provider call it is allowed with passages still outstanding.</summary>
    /// <param name="embeddedChunkCount">How many passages the turn did commit a vector for.</param>
    /// <param name="inputCharacterCount">How many characters the turn sent before it ran out of calls.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a count is negative.</exception>
    public static StoredEmailEmbeddingRun CallBudgetExhausted(int embeddedChunkCount, int inputCharacterCount = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(embeddedChunkCount);
        ArgumentOutOfRangeException.ThrowIfNegative(inputCharacterCount);

        return new StoredEmailEmbeddingRun(
            StoredEmailEmbeddingOutcome.CallBudgetExhausted,
            embeddedChunkCount,
            inputCharacterCount,
            Failure: null,
            SpendPeriodEndsAt: null);
    }

    /// <summary>Reports a turn stopped by the period's spend ceiling, after whatever it had already committed.</summary>
    /// <param name="embeddedChunkCount">How many passages the turn committed a vector for before the ceiling bound.</param>
    /// <param name="inputCharacterCount">How many characters the turn sent before it stopped.</param>
    /// <param name="spendPeriodEndsAt">When the period rolls over, which is when work resumes without anyone acting.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a count is negative.</exception>
    public static StoredEmailEmbeddingRun SpendCeilingReached(
        int embeddedChunkCount,
        int inputCharacterCount,
        DateTimeOffset spendPeriodEndsAt)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(embeddedChunkCount);
        ArgumentOutOfRangeException.ThrowIfNegative(inputCharacterCount);

        return new StoredEmailEmbeddingRun(
            StoredEmailEmbeddingOutcome.SpendCeilingReached,
            embeddedChunkCount,
            inputCharacterCount,
            Failure: null,
            spendPeriodEndsAt);
    }

    /// <summary>Reports an instance that has activated no profile.</summary>
    /// <returns>The result.</returns>
    public static StoredEmailEmbeddingRun NoActiveProfile() => new(
        StoredEmailEmbeddingOutcome.NoActiveProfile,
        EmbeddedChunkCount: 0,
        InputCharacterCount: 0,
        Failure: null,
        SpendPeriodEndsAt: null);

    /// <summary>Reports a generator whose vector space is not the one the active profile records.</summary>
    /// <returns>The result.</returns>
    public static StoredEmailEmbeddingRun GeneratorDisagreesWithProfile() => new(
        StoredEmailEmbeddingOutcome.GeneratorDisagreesWithProfile,
        EmbeddedChunkCount: 0,
        InputCharacterCount: 0,
        Failure: null,
        SpendPeriodEndsAt: null);

    /// <summary>Reports a provider call that ended without vectors, after whatever this turn had already committed.</summary>
    /// <param name="embeddedChunkCount">How many passages were committed before the failing call.</param>
    /// <param name="failure">Why the call produced nothing.</param>
    /// <param name="inputCharacterCount">How many characters the turn had sent before the failing call.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a count is negative, or the failure is not a defined member.</exception>
    /// <remarks>
    /// The failing call's own characters are deliberately not counted. A provider that answered with a refusal, a
    /// timeout, or a transport fault produced no vectors, and charging a budget for it would let an unreachable endpoint
    /// spend a period's ceiling on nothing.
    /// </remarks>
    public static StoredEmailEmbeddingRun ProviderFailed(
        int embeddedChunkCount,
        EmbeddingGenerationFailure failure,
        int inputCharacterCount = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(embeddedChunkCount);
        ArgumentOutOfRangeException.ThrowIfNegative(inputCharacterCount);

        if (!Enum.IsDefined(failure))
        {
            throw new ArgumentOutOfRangeException(
                nameof(failure),
                failure,
                "A provider failure is reported through a defined classification.");
        }

        return new StoredEmailEmbeddingRun(
            StoredEmailEmbeddingOutcome.ProviderFailed,
            embeddedChunkCount,
            inputCharacterCount,
            failure,
            SpendPeriodEndsAt: null);
    }
}
