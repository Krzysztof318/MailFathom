// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;

namespace MailFathom.Application.EmailContent.Storage;

/// <summary>The object backend named directly, for the one use case whose subject is the object backend itself.</summary>
/// <remarks>
/// <para>
/// Every other caller reaches content through <see cref="IEmailContentStore" />, which is the port's promise: a use case
/// stores and reads mail and never learns which store answered. The move of already-stored content is the exception
/// that proves it — what it does is precisely to put a payload in the bucket and check that the bucket has it, so a port
/// that hid the bucket would hide its subject.
/// </para>
/// <para>
/// It is a second port rather than the adapter's own, because an adapter's interface lives in the adapter: the
/// infrastructure that speaks S3 is not visible from here, and the move is a use case rather than infrastructure. What
/// this adds over the content store is the pair of operations addressed by an object key instead of by an owning row —
/// which is exactly what a copy needs and what nothing else may have.
/// </para>
/// <para>
/// It is registered only where the deployment selected the object backend, so its absence is a fact a use case reads
/// rather than a failure: a deployment writing to the database has nothing to move content into, and the move refuses to
/// start rather than copying mail nowhere.
/// </para>
/// </remarks>
public interface IEmailContentObjectBackend
{
    /// <summary>Writes one payload under a key minted for this write, and answers with where it went.</summary>
    /// <param name="kind">Which of the four payload kinds is being written, which reaches the key as a segment.</param>
    /// <param name="rawMime">The raw RFC 822 bytes.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The placement, naming the whole key and what was measured over the payload.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="rawMime" /> is empty.</exception>
    /// <exception cref="MailFathomException">Thrown when the endpoint did not accept the object.</exception>
    Task<PlacedEmailContent> PlaceAsync(
        EmailContentKind kind,
        ReadOnlyMemory<byte> rawMime,
        CancellationToken cancellationToken);

    /// <summary>Reads back the object one key names, so what was written can be checked against what a row records.</summary>
    /// <param name="objectLocator">The whole key, exactly as the placement produced it.</param>
    /// <param name="maximumByteLength">The length the row records, past which the endpoint's answer stops being read.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The bytes, or <see langword="null" /> when the endpoint holds no object under that key.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maximumByteLength" /> is not positive.</exception>
    /// <exception cref="MailFathomException">Thrown when the endpoint could not answer.</exception>
    /// <remarks>
    /// An absent object is answered rather than raised, because the two are different findings: an endpoint that cannot
    /// answer says to try the same payload again later, and an endpoint that answers with nothing says this payload must
    /// not be repointed at all.
    /// <para>
    /// The ceiling is what keeps a remote answer from deciding how much this process holds. A caller states the length
    /// it expects, the read stops one byte past it, and an endpoint answering with more is met as a payload that
    /// disagrees with its row rather than as a buffer grown to fit whatever arrived.
    /// </para>
    /// </remarks>
    Task<ReadOnlyMemory<byte>?> ReadBackAsync(
        string objectLocator,
        long maximumByteLength,
        CancellationToken cancellationToken);
}
