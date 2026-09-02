// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.EmailContent.Storage;

/// <summary>What asking the ceilings for room produced: the claim, or the bound that refused it.</summary>
/// <remarks>
/// <para>
/// The refusal names its bound rather than being an absent claim, because what a caller records about a deferred
/// message and what an operator has to act on both depend on which of the two ceilings was met. A granted attempt names
/// no bound, so the two states cannot both be read as true.
/// </para>
/// <para>
/// Both factories refuse the arguments that would produce a nonsense attempt, and being a struct leaves one shape they
/// cannot reach: <see langword="default" />, which holds no claim and names no bound. Nothing here produces it — this
/// type is returned by <see cref="StoredContentCeiling.TryClaim" /> and read at the call site — and a reader who does
/// reach one is holding an attempt that was never made rather than a granted or a refused one. It is documented rather
/// than designed away, exactly as <see cref="MailOwnerId" /> documents its own, because the alternative is an
/// allocation per stored message to describe a state no code path constructs.
/// </para>
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
