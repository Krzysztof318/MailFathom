// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Embeddings.Generation;

/// <summary>What one message's turn at being embedded produced.</summary>
/// <remarks>
/// Counts and classifications only. The message identity, its passages, and the vectors they became are all mail
/// content or derived from it, and none of them belongs in something a worker logs.
/// </remarks>
/// <param name="Outcome">How the turn ended.</param>
/// <param name="EmbeddedChunkCount">How many passages were given a vector and committed during this turn.</param>
/// <param name="Failure">Why a provider call produced nothing, present exactly when <paramref name="Outcome" /> is <see cref="StoredEmailEmbeddingOutcome.ProviderFailed" />.</param>
public sealed record StoredEmailEmbeddingRun(
    StoredEmailEmbeddingOutcome Outcome,
    int EmbeddedChunkCount,
    EmbeddingGenerationFailure? Failure)
{
    /// <summary>Reports a message every passage of which now carries a vector.</summary>
    /// <param name="embeddedChunkCount">How many passages this turn produced a vector for.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the count is negative.</exception>
    public static StoredEmailEmbeddingRun Embedded(int embeddedChunkCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(embeddedChunkCount);

        return new StoredEmailEmbeddingRun(StoredEmailEmbeddingOutcome.Embedded, embeddedChunkCount, Failure: null);
    }

    /// <summary>Reports a message whose turn spent every provider call it is allowed with passages still outstanding.</summary>
    /// <param name="embeddedChunkCount">How many passages the turn did commit a vector for.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the count is negative.</exception>
    public static StoredEmailEmbeddingRun CallBudgetExhausted(int embeddedChunkCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(embeddedChunkCount);

        return new StoredEmailEmbeddingRun(
            StoredEmailEmbeddingOutcome.CallBudgetExhausted,
            embeddedChunkCount,
            Failure: null);
    }

    /// <summary>Reports an instance that has activated no profile.</summary>
    /// <returns>The result.</returns>
    public static StoredEmailEmbeddingRun NoActiveProfile() =>
        new(StoredEmailEmbeddingOutcome.NoActiveProfile, EmbeddedChunkCount: 0, Failure: null);

    /// <summary>Reports a generator whose vector space is not the one the active profile records.</summary>
    /// <returns>The result.</returns>
    public static StoredEmailEmbeddingRun GeneratorDisagreesWithProfile() =>
        new(StoredEmailEmbeddingOutcome.GeneratorDisagreesWithProfile, EmbeddedChunkCount: 0, Failure: null);

    /// <summary>Reports a provider call that ended without vectors, after whatever this turn had already committed.</summary>
    /// <param name="embeddedChunkCount">How many passages were committed before the failing call.</param>
    /// <param name="failure">Why the call produced nothing.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the count is negative, or the failure is not a defined member.</exception>
    public static StoredEmailEmbeddingRun ProviderFailed(int embeddedChunkCount, EmbeddingGenerationFailure failure)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(embeddedChunkCount);

        if (!Enum.IsDefined(failure))
        {
            throw new ArgumentOutOfRangeException(
                nameof(failure),
                failure,
                "A provider failure is reported through a defined classification.");
        }

        return new StoredEmailEmbeddingRun(StoredEmailEmbeddingOutcome.ProviderFailed, embeddedChunkCount, failure);
    }
}
