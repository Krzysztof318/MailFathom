// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
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

    /// <summary>Reads back the object one row points at, up to the length that row records.</summary>
    /// <param name="objectLocator">The whole key, exactly as the row carries it.</param>
    /// <param name="maximumByteLength">The length the row records, past which the endpoint's answer stops being read.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The bytes, or <see langword="null" /> when the endpoint holds no object under that key.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maximumByteLength" /> is not positive.</exception>
    /// <exception cref="ObjectStorageUnavailableException">Thrown when the endpoint could not answer.</exception>
    /// <remarks>
    /// An absent object is answered rather than raised, because it is a content defect the caller grades: the read that
    /// meets it reports the same content-unavailable outcome a missing database payload produces and raises a repair
    /// request, which is a different thing from an endpoint that could not be reached at all.
    /// <para>
    /// The ceiling is a bound on this process rather than an assertion about the object: the read stops one byte past
    /// what the row records, so an endpoint answering with more hands back something longer than the row describes and
    /// every caller already holds the length and the digest that says so. Nothing here decides what that means.
    /// </para>
    /// </remarks>
    Task<ReadOnlyMemory<byte>?> FindAsync(
        string objectLocator,
        long maximumByteLength,
        CancellationToken cancellationToken);

    /// <summary>Removes one object, which is what carries a committed deletion of its row through to the endpoint.</summary>
    /// <param name="objectLocator">The whole key, exactly as the row that was deleted carried it.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>A task that completes once the endpoint holds no object under that key.</returns>
    /// <exception cref="ObjectStorageUnavailableException">Thrown when the endpoint did not answer.</exception>
    /// <remarks>
    /// Removing a key nothing holds succeeds, which is what makes the operation safe to repeat: the deletion path and
    /// the reclamation can both reach one object, and an attempt after a crash meets a key the attempt before it
    /// already removed.
    /// </remarks>
    Task DeleteAsync(string objectLocator, CancellationToken cancellationToken);

    /// <summary>Reads one page of the objects held beneath this deployment's own key prefix.</summary>
    /// <param name="continuationToken">The token a previous page answered with, or <see langword="null" /> to begin the listing.</param>
    /// <param name="maxObjects">How many objects the page may name at most.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The page, carrying the token the next one is asked for with or none when the listing ended.</returns>
    /// <exception cref="ObjectStorageUnavailableException">Thrown when the endpoint did not answer.</exception>
    /// <remarks>
    /// <b>The prefix is the whole of what this may see.</b> A deployment sharing a bucket with another one is separated
    /// from it by prefix alone, so a listing that reached outside it would let reclamation delete somebody else's mail.
    /// The prefix is applied here rather than by a caller for exactly that reason.
    /// </remarks>
    Task<ObjectStorageListingPage> ListAsync(
        string? continuationToken,
        int maxObjects,
        CancellationToken cancellationToken);
}
