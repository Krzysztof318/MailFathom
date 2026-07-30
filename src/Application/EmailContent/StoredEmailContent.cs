// Copyright © 2026 Krzysztof Kasprowicz

using System.Security.Cryptography;

namespace MailMcp.Application.EmailContent;

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
    /// <summary>Finds the defect that makes the stored payload differ from what was recorded for it.</summary>
    /// <returns>The defect, or <see langword="null" /> when the payload is exactly what was written.</returns>
    /// <remarks>
    /// The length is checked before the digest because it is the cheaper answer to the same question and because it
    /// names the more likely fault precisely: a truncated payload is what a partial write leaves behind, and reporting
    /// it as a hash mismatch would describe a repaired copy as a corrupted one.
    /// </remarks>
    public EmailContentDefect? FindIntegrityDefect()
    {
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
