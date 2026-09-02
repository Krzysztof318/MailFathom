// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;

namespace MailFathom.Infrastructure.ObjectStorage;

/// <summary>Hands the object store to the one use case whose subject is the object store.</summary>
/// <remarks>
/// A pass-through and nothing else. <see cref="IEmailContentObjectStore" /> is this adapter's own interface and is not
/// visible from a use case, so the move — which exists to put payloads in the bucket and check that the bucket has them
/// — reaches it through a port declared where use cases live. What this type adds is that boundary, which is why it
/// holds no behaviour of its own: anything it decided would be a decision taken twice, once here and once in the store
/// behind it.
/// </remarks>
internal sealed class EmailContentObjectBackend(IEmailContentObjectStore objectStore) : IEmailContentObjectBackend
{
    /// <inheritdoc />
    public Task<PlacedEmailContent> PlaceAsync(
        EmailContentKind kind,
        ReadOnlyMemory<byte> rawMime,
        CancellationToken cancellationToken) =>
        objectStore.PlaceAsync(kind, rawMime, cancellationToken);

    /// <inheritdoc />
    public Task<ReadOnlyMemory<byte>?> ReadBackAsync(
        string objectLocator,
        long maximumByteLength,
        CancellationToken cancellationToken) =>
        objectStore.FindAsync(objectLocator, maximumByteLength, cancellationToken);
}
