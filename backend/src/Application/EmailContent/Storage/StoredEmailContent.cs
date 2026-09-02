// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using MailFathom.Application.EmailContent.Repair;

namespace MailFathom.Application.EmailContent.Storage;

/// <summary>Carries the raw MIME stored for one email together with what was recorded about it when it was written.</summary>
/// <param name="RawMime">The stored RFC 822 bytes.</param>
/// <param name="RecordedByteLength">How many bytes the writer said it stored.</param>
/// <param name="RecordedSha256Hash">The SHA-256 digest the writer computed over those bytes.</param>
/// <remarks>
/// <para>
/// The two recorded values travel with the payload rather than staying inside the store, because the caller that reads
/// content is the one that has to decide what a mismatch means. A store that checked them itself could only throw, and
/// a damaged local copy is something a read answers with a stable failure and a repair request, not with an exception
/// nobody above it can interpret.
/// </para>
/// <para>
/// The payload is mail content and personal data by default. Nothing here may be logged, and neither may its length or
/// digest be used as a substitute for identifying a message in a log line.
/// </para>
/// </remarks>
public sealed record StoredEmailContent(
    ReadOnlyMemory<byte> RawMime,
    long RecordedByteLength,
    ReadOnlyMemory<byte> RecordedSha256Hash)
{
    /// <summary>Gets whether the payload came from the database copy retained beside a moved payload's object.</summary>
    /// <remarks>
    /// <para>
    /// False for every ordinary read, including one the object backend answered. It is true only in the state
    /// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0017-object-storage-content-backend-consistency-and-object-identity.md">ADR 0017</see> § 6
    /// defines: the move has carried this payload into the bucket and an operator has not yet released the copy the
    /// database was holding, so both stores hold it and the object — the authoritative one — could not be read or did
    /// not match what the row records.
    /// </para>
    /// <para>
    /// The read succeeded, which is why this is a property of content that was served rather than a failure. What it
    /// says is that the deployment is one release away from that same read answering with nothing at all, so a caller
    /// that can record a repair does.
    /// </para>
    /// </remarks>
    public bool WasServedFromRetainedCopy { get; init; }

    /// <summary>Gets whether the store already checked this payload against the length and digest recorded for it.</summary>
    /// <remarks>
    /// Set by the one caller that has run the check and found nothing — the object-backed read, which has to grade the
    /// object itself before it can decide whether to reach for a retained copy. Everything else leaves it false, so a
    /// payload the store handed over unchecked is still checked by whoever reads it.
    /// </remarks>
    public bool WasVerifiedIntact { get; init; }

    /// <summary>Finds the defect that makes the stored payload differ from what was recorded for it.</summary>
    /// <returns>The defect, or <see langword="null" /> when the payload is exactly what was written.</returns>
    /// <remarks>
    /// <para>
    /// The length is checked before the digest because it is the cheaper answer to the same question and because it
    /// names the more likely fault precisely: a truncated payload is what a partial write leaves behind, and reporting
    /// it as a hash mismatch would describe a repaired copy as a corrupted one.
    /// </para>
    /// <para>
    /// A payload carrying <see cref="WasVerifiedIntact" /> answers without hashing again. The digest is computed over
    /// the whole message, so a mailbox read that hashed once in the store and once in the reader would pay for the same
    /// answer twice; the flag is set only where the check has actually run, and the record is immutable, so no later
    /// hand can invalidate it.
    /// </para>
    /// </remarks>
    public EmailContentDefect? FindIntegrityDefect()
    {
        if (this.WasVerifiedIntact)
        {
            return null;
        }

        if (this.RawMime.Length != this.RecordedByteLength)
        {
            return EmailContentDefect.ByteLengthMismatch;
        }

        Span<byte> computedHash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(this.RawMime.Span, computedHash);

        // An ordinary comparison rather than a fixed-time one: the digest is an integrity record over mail this caller
        // is already entitled to read, so nothing here is a secret whose comparison could leak one.
        return computedHash.SequenceEqual(this.RecordedSha256Hash.Span)
            ? null
            : EmailContentDefect.HashMismatch;
    }
}
