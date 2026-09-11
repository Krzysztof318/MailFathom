// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Storage;

/// <summary>What the claim store made of one request for room: the claim it wrote, or the bound that refused it.</summary>
/// <param name="ClaimId">The claim that now binds for every replica, or <see langword="null" /> where none was taken.</param>
/// <param name="ReachedBound">Which ceiling refused, or <see cref="StoredContentBound.None" /> where room was found.</param>
/// <remarks>
/// The store's own answer rather than <see cref="StoredContentClaimAttempt" />, because a claim identifier is what an
/// adapter can produce and a releasable claim is not: the disposable belongs to the ceiling that knows which store to
/// give the room back to.
/// </remarks>
public readonly record struct StoredContentClaimRecord(Guid? ClaimId, StoredContentBound ReachedBound)
{
    /// <summary>Gets the record a deployment that bounds nothing receives, which takes no room and refuses nobody.</summary>
    /// <remarks>
    /// It names no claim, because there is nothing to release: a claim exists to make a reservation visible to the
    /// replicas that are measuring against it, and nothing is measuring where nothing is bounded.
    /// </remarks>
    public static StoredContentClaimRecord Unbounded { get; } = new(ClaimId: null, StoredContentBound.None);

    /// <summary>Gets whether room was found, which is what separates an unbounded grant from a refusal.</summary>
    public bool IsGranted => this.ReachedBound is StoredContentBound.None;
}
