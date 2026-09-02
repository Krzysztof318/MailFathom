// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.SensitiveContent.Derivation;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Domain.Access;

namespace MailFathom.Application.Emails.Extraction;

/// <summary>Replaces what an owner's switched-on scanner finds in a message's body before anything is derived from it.</summary>
/// <remarks>
/// <para>
/// One decorator rather than a redaction inside each writer, because this port is where every derived copy of a body
/// begins: the search document, the passages cut from it, and the vectors built from those passages all descend from
/// one <see cref="ExtractedEmailText" />, and both paths that produce one — synchronization reading a message it has
/// just fetched, and the backfill re-reading raw MIME stored before extraction existed — reach it through here. Redacting
/// at this seam is therefore what makes the placeholder the thing that is stored, chunked, embedded, and later retrieved,
/// with nothing downstream needing to know a scanner exists.
/// </para>
/// <para>
/// <b>What is looked for is the owner's, and the owner arrives with the message.</b> A deployment serves several people
/// and one of them may have switched a scanner on that the deployment left off, so the body is redacted under the
/// posture of whoever the mail belongs to rather than under one answer for the whole store. Both paths that reach here
/// already hold it, which is why the port carries it rather than resolving it.
/// </para>
/// <para>
/// <b>The body is what is redacted, and only the body.</b> A subject, an address, and a thread identifier are the
/// envelope's routing metadata rather than derived text: they are what a listing filters on and what a reply is addressed
/// to, so replacing them would remove a stored message's whole use, and what protects them on their way out is the
/// egress guard rather than the derived store. The stored raw MIME is untouched by all of this, as it is everywhere in
/// this feature.
/// </para>
/// <para>
/// <b>It fails closed.</b> A detector that cannot answer refuses the read rather than yielding text nothing scanned, and
/// the refusal reaches whichever run asked: a synchronization run stops on that message with its checkpoint where it was,
/// and a backfill batch is discarded and resumes from its last committed position. Both retry. Both readings are
/// redacted: the trimmed reading
/// is what the lexical index covers and the untrimmed one is what survives an over-aggressive trim, so a credential
/// removed from one and left in the other would be a credential in the derived store. They cost one scan where they are
/// the same string, which is the ordinary message, and two where trimming removed something.
/// </para>
/// </remarks>
public sealed class RedactingEmailMimeReader : IEmailMimeReader
{
    private readonly IEmailMimeReader inner;
    private readonly SensitiveContentDerivationGuard guard;

    /// <summary>Initializes a reader that redacts what the one it wraps extracted.</summary>
    /// <param name="inner">The reader that turns raw MIME into normalized metadata.</param>
    /// <param name="guard">The one redaction every derived write shares.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public RedactingEmailMimeReader(IEmailMimeReader inner, SensitiveContentDerivationGuard guard)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(guard);

        this.inner = inner;
        this.guard = guard;
    }

    /// <inheritdoc />
    /// <exception cref="SensitiveContentScannerUnavailableException">Thrown when a switched-on scanner could not establish what the body carries, which refuses the derivation.</exception>
    public async Task<EmailMimeExtractionResult> ReadMetadataAsync(
        RemoteEmailContent content,
        MailOwnerId owner,
        CancellationToken cancellationToken)
    {
        // Read before the scan rather than where the reading is written, and before the scan rather than after it.
        // A posture republished while this message is being scanned then leaves the row stamped with the older one,
        // which reads as stale and is re-derived — the safe direction, where a stamp taken at the write would record a
        // posture the text never went through and the row would never be revisited.
        var redactedUnder = this.guard.StampFor(owner);
        var extraction = await this.inner.ReadMetadataAsync(content, owner, cancellationToken);

        // A message nobody could parse carries no text to redact, and neither does one whose body held no words or
        // arrived inside a cryptographic envelope. Each of those reaches the derived store as the absence it already
        // is, and is still stamped: a row nothing had to redact was still derived under this posture, and one left
        // unstamped would be outstanding to every rebuild for ever.
        if (extraction.Metadata is not { Text: { OriginalText: { } original, TrimmedText: { } trimmed } text } metadata)
        {
            return extraction.Metadata is { } unredacted
                ? EmailMimeExtractionResult.Extracted(unredacted with { RedactedUnder = redactedUnder })
                : extraction;
        }

        var redactedOriginal = await this.guard.GuardAsync(owner, original, cancellationToken);

        // Most mail quotes nothing and signs off with nothing, so the two readings are one string and redaction is
        // reproducible over it — scanning it twice would spend a second budget and report a second measurement for one
        // message, which is both the cost and the figure an operator reads the derivation latency from.
        var redactedTrimmed = StringComparer.Ordinal.Equals(original, trimmed)
            ? redactedOriginal
            : await this.guard.GuardAsync(owner, trimmed, cancellationToken);

        return EmailMimeExtractionResult.Extracted(metadata with
        {
            Text = text.WithRedactedText(redactedOriginal, redactedTrimmed),
            RedactedUnder = redactedUnder,
        });
    }
}
