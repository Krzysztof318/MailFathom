// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Storage;

/// <summary>Holds room under both <see cref="StoredContentCeiling" /> levels while one payload is fetched and stored.</summary>
/// <remarks>
/// A claim is taken against what a message is expected to occupy, before it is fetched, and settled against what was
/// actually written. Disposing it gives back whatever was never settled, so a fetch that was abandoned and a commit
/// that was rolled back both leave the ceilings where they found them rather than consuming room nothing occupies. It
/// holds the deployment's level and its owner's together, because a payload occupies both and giving one back without
/// the other would leave a level describing storage that is not there.
/// </remarks>
public sealed class StoredContentClaim : IDisposable
{
    private readonly StoredContentCeiling.ContentLevel deployment;
    private readonly StoredContentCeiling.ContentLevel owner;
    private long heldBytes;

    internal StoredContentClaim(
        StoredContentCeiling.ContentLevel deployment,
        StoredContentCeiling.ContentLevel owner,
        long bytes)
    {
        this.deployment = deployment;
        this.owner = owner;
        this.ClaimedBytes = bytes;
        this.heldBytes = bytes;
    }

    /// <summary>Gets what was claimed before the payload was fetched.</summary>
    public long ClaimedBytes { get; }

    /// <summary>Keeps room for what was actually stored and gives back the rest.</summary>
    /// <param name="storedBytes">How many bytes reached local storage.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="storedBytes" /> is negative.</exception>
    /// <remarks>
    /// A payload larger than the claim keeps the whole claim and nothing more: the extra bytes are past the ceiling by
    /// definition, and taking them back out of a level the next measurement is about to correct anyway would report a
    /// deployment as having room it does not have.
    /// </remarks>
    public void Settle(long storedBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(storedBytes);

        var released = Interlocked.Exchange(ref this.heldBytes, 0);

        this.ReleaseBoth(released - Math.Min(released, storedBytes));
    }

    /// <summary>Gives back whatever the claim still holds.</summary>
    public void Dispose()
    {
        var released = Interlocked.Exchange(ref this.heldBytes, 0);

        this.ReleaseBoth(released);
    }

    private void ReleaseBoth(long bytes)
    {
        this.deployment.Release(bytes);
        this.owner.Release(bytes);
    }
}
