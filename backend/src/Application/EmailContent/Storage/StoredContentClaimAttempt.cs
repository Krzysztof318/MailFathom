// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Storage;

/// <summary>What asking the ceilings for room produced: the claim, or the bound that refused it.</summary>
/// <remarks>
/// The refusal names its bound rather than being an absent claim, because what a caller records about a deferred
/// message and what an operator has to act on both depend on which of the two ceilings was met. A granted attempt names
/// no bound, so the two states cannot both be read as true.
/// </remarks>
public readonly record struct StoredContentClaimAttempt
{
    private StoredContentClaimAttempt(StoredContentClaim? claim, StoredContentBound reachedBound)
    {
        this.Claim = claim;
        this.ReachedBound = reachedBound;
    }

    /// <summary>Gets the room that was held, or <see langword="null" /> where neither ceiling had any.</summary>
    /// <remarks>A granted claim must be disposed, which is what returns whatever the payload did not use.</remarks>
    public StoredContentClaim? Claim { get; }

    /// <summary>Gets which ceiling refused, or <see cref="StoredContentBound.None" /> where the claim was granted.</summary>
    public StoredContentBound ReachedBound { get; }

    /// <summary>Reports room held for one payload.</summary>
    /// <param name="claim">The room that was taken.</param>
    /// <returns>The attempt.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="claim" /> is <see langword="null" />.</exception>
    public static StoredContentClaimAttempt Granted(StoredContentClaim claim)
    {
        ArgumentNullException.ThrowIfNull(claim);

        return new StoredContentClaimAttempt(claim, StoredContentBound.None);
    }

    /// <summary>Reports that one of the ceilings had no room.</summary>
    /// <param name="reachedBound">Which of them refused.</param>
    /// <returns>The attempt.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when no bound is named.</exception>
    public static StoredContentClaimAttempt Refused(StoredContentBound reachedBound)
    {
        if (reachedBound is StoredContentBound.None || !Enum.IsDefined(reachedBound))
        {
            throw new ArgumentOutOfRangeException(
                nameof(reachedBound),
                reachedBound,
                "A refusal names which of the two stored-content ceilings it reached.");
        }

        return new StoredContentClaimAttempt(claim: null, reachedBound);
    }
}
