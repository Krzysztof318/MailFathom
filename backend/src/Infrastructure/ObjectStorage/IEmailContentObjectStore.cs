// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;

namespace MailFathom.Infrastructure.ObjectStorage;

/// <summary>Writes one raw MIME payload to the configured endpoint and reads one back.</summary>
/// <remarks>
/// <para>
/// The seam exists so the content store can be tested against a scripted endpoint rather than a live bucket, which is
/// what lets every crash point in the write ordering be exercised: an object written and no row committed, a row staged
/// over an object that was never written, and a read of a key nothing holds.
/// </para>
/// <para>
/// It is registered only when the deployment selected the object backend. Its absence is therefore a fact the content
/// store reads rather than a failure — a deployment writing to the database needs no endpoint, and one that took its
/// endpoint away while object-backed rows exist is reported unhealthy rather than being unable to start.
/// </para>
/// </remarks>
internal interface IEmailContentObjectStore
{
    /// <summary>Writes one payload under a key minted for this write, and answers with where it went.</summary>
    /// <param name="kind">Which of the four payload kinds is being written, which reaches the key as a segment.</param>
    /// <param name="rawMime">The raw RFC 822 bytes.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The placement, naming the whole key and what was measured over the payload.</returns>
    /// <exception cref="ObjectStorageUnavailableException">Thrown when the endpoint did not accept the object.</exception>
    Task<PlacedEmailContent> PlaceAsync(
        EmailContentKind kind,
        ReadOnlyMemory<byte> rawMime,
        CancellationToken cancellationToken);

    /// <summary>Reads back the object one row points at.</summary>
    /// <param name="objectLocator">The whole key, exactly as the row carries it.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The bytes, or <see langword="null" /> when the endpoint holds no object under that key.</returns>
    /// <exception cref="ObjectStorageUnavailableException">Thrown when the endpoint could not answer.</exception>
    /// <remarks>
    /// An absent object is answered rather than raised, because it is a content defect the caller grades: the read that
    /// meets it reports the same content-unavailable outcome a missing database payload produces and raises a repair
    /// request, which is a different thing from an endpoint that could not be reached at all.
    /// </remarks>
    Task<ReadOnlyMemory<byte>?> FindAsync(string objectLocator, CancellationToken cancellationToken);
}
