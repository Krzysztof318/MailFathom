// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Storage;

/// <summary>Holds room under <see cref="StoredContentCeiling" /> while one payload is being fetched and stored.</summary>
/// <remarks>
/// A claim is taken against what a message is expected to occupy, before it is fetched, and settled against what was
/// actually written. Disposing it gives back whatever was never settled, so a fetch that was abandoned and a commit
/// that was rolled back both leave the ceiling where they found it rather than consuming room nothing occupies.
/// </remarks>
public sealed class StoredContentClaim : IDisposable
{
    private readonly StoredContentCeiling ceiling;
    private long heldBytes;

    internal StoredContentClaim(StoredContentCeiling ceiling, long bytes)
    {
        this.ceiling = ceiling;
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

        this.ceiling.Release(released - Math.Min(released, storedBytes));
    }

    /// <summary>Gives back whatever the claim still holds.</summary>
    public void Dispose()
    {
        var released = Interlocked.Exchange(ref this.heldBytes, 0);

        this.ceiling.Release(released);
    }
}
