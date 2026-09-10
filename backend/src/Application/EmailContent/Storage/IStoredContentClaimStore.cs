// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.EmailContent.Storage;

/// <summary>Holds the room every replica of one deployment has claimed for payloads it is about to store.</summary>
/// <remarks>
/// <para>
/// A claim has to be visible to the other replicas before the payload it covers is fetched, which is the whole reason
/// this is a port rather than a field. A process holding its own reservations bounds itself and nothing else: the
/// occupancy each replica measures describes what storage held a moment ago, and the bytes the others have reserved
/// since are exactly what a ceiling worded as the deployment's would then be overshot by.
/// </para>
/// <para>
/// Taking a claim is one act rather than a measurement followed by a decision, because the two cannot be separated
/// without reopening the gap this exists to close. The implementation reads what storage occupies, adds what is
/// claimed and unexpired, and inserts the claim, and it does all of it where a concurrent claim cannot interleave.
/// </para>
/// <para>
/// Every claim expires. A claim covers one message's fetch and store, so a holder that stopped answering leaves bytes
/// reserved for at most that long rather than until an operator notices — which is what makes an expiry the release of
/// last resort beside <see cref="ReleaseAsync" />, exactly as
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0031-dividing-singleton-work-between-replicas-with-a-leased-scope.md">ADR 0031</see>
/// makes it for a leased scope.
/// </para>
/// </remarks>
public interface IStoredContentClaimStore
{
    /// <summary>Claims room for one payload against both ceilings, or reports which of them had none.</summary>
    /// <param name="user">The user whose mail the payload is.</param>
    /// <param name="bytes">What the payload is expected to occupy.</param>
    /// <param name="ceilings">What the deployment and any one user may occupy, where either is bounded at all.</param>
    /// <param name="claimLifetime">How long the claim binds before it expires unreleased.</param>
    /// <param name="cancellationToken">Cancels the claim.</param>
    /// <returns>The claim that was taken, or the bound that refused it.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="bytes" /> is not positive, or the lifetime is not.</exception>
    Task<StoredContentClaimRecord> ClaimAsync(
        MailUserId user,
        long bytes,
        StoredContentCeilings ceilings,
        TimeSpan claimLifetime,
        CancellationToken cancellationToken);

    /// <summary>Gives one claim's room back, whether or not the payload it covered was ever stored.</summary>
    /// <param name="claimId">The claim that was taken.</param>
    /// <param name="cancellationToken">Cancels the release.</param>
    /// <returns>A task that completes when the claim no longer binds.</returns>
    /// <remarks>
    /// Releasing a claim that has already expired and been swept is not an error. The claim binds nothing either way,
    /// and a holder that was slow enough to be swept must not fail the run it was storing for.
    /// </remarks>
    Task ReleaseAsync(Guid claimId, CancellationToken cancellationToken);
}
