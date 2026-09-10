// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Storage;

/// <summary>Holds room under both <see cref="StoredContentCeiling" /> levels while one payload is fetched and stored.</summary>
/// <remarks>
/// <para>
/// A claim is taken against what a message is expected to occupy, before it is fetched, and given back once the payload
/// has reached storage or been abandoned. Disposing it is what gives it back, so a fetch that was abandoned and a
/// commit that was rolled back both leave the ceilings where they found them rather than reserving room nothing
/// occupies. It covers the deployment's population and its user's together, because a payload occupies both and giving
/// one back without the other would leave a ceiling describing storage that is not there.
/// </para>
/// <para>
/// There is no settling step, and its absence is the point. What a stored payload occupies is read from storage itself
/// on the next claim rather than carried in a level between measurements, so a claim released after the commit leaves
/// nothing behind to reconcile — the bytes are in what the next reading measures. Releasing it before the commit would
/// be the mistake, which is why the release is disposal at the end of the store rather than a call the caller places.
/// </para>
/// <para>
/// A claim taken where the deployment bounds nothing names no reservation and releases none. It exists so a caller
/// reads a granted attempt the same way whether or not a ceiling was declared.
/// </para>
/// </remarks>
public sealed class StoredContentClaim : IAsyncDisposable
{
    private readonly IStoredContentClaimStore claimStore;
    private readonly Guid? claimId;
    private int released;

    internal StoredContentClaim(IStoredContentClaimStore claimStore, Guid? claimId, long bytes)
    {
        this.claimStore = claimStore;
        this.claimId = claimId;
        this.ClaimedBytes = bytes;
    }

    /// <summary>Gets what was claimed before the payload was fetched.</summary>
    public long ClaimedBytes { get; }

    /// <summary>Gives back whatever the claim still holds.</summary>
    /// <returns>A task that completes once the room is available to every replica again.</returns>
    /// <remarks>
    /// Releasing twice releases once. Disposal is what the ordinary path uses and the expiry is what covers a holder
    /// that never reached it, so a second release meeting nothing is the expected shape rather than a fault.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        if (this.claimId is not { } claim || Interlocked.Exchange(ref this.released, 1) == 1)
        {
            return;
        }

        // Not the caller's token: a claim released only because the run was cancelled is exactly the claim that must
        // not be left binding until it expires.
        await this.claimStore.ReleaseAsync(claim, CancellationToken.None);
    }
}
