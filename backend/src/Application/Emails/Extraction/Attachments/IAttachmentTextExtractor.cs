// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Attachments;
using MailFathom.Application.Emails.AttachmentText;

namespace MailFathom.Application.Emails.Extraction.Attachments;

/// <summary>Reads one attachment's plain text, or says exactly why it read none.</summary>
/// <remarks>
/// <para>
/// The port exists so that the document parsers stay inside their adapter. No parser type and no third-party document
/// model crosses it: what comes back is characters and a closed set of reasons, and a caller never interprets an
/// exception a parser raised.
/// </para>
/// <para>
/// It is handed an attachment already opened from stored content, so extraction reaches no mail server and no network
/// and cannot affect a remote <c>\Seen</c> flag. The opened attachment stays the caller's to dispose; an implementation
/// reads it and owns nothing.
/// </para>
/// <para>
/// Reading an attachment is the largest attack surface this system has, so an implementation never runs it inside a
/// synchronization transaction, where a hostile document would stall a checkpoint and hold a database transaction open
/// for as long as it took. It does run on paths a caller waits on: a message being screened before it is sent or saved
/// as a draft, and an attachment being screened before it is streamed to whoever asked for it. Both are acts that must
/// be judged on the whole of what would leave, and neither can be answered later. What bounds the delay a caller pays
/// there is <see cref="AttachmentTextExtractionOptions" />, which every implementation applies per attachment, beside
/// <see cref="EmailAttachmentTextBounds" />, which such a caller applies across the message so that one message costs
/// the same whichever path met it. Nothing an implementation reads is written to the file system, and
/// nothing a document declares as a macro, a script, an embedded object, or any other active content is executed,
/// evaluated, or handed to anything that would.
/// </para>
/// <para>
/// <b>One of those bounds does not hold on a single unit of work, and a caller-waited path is where that matters.</b>
/// <see cref="AttachmentTextExtractionOptions.Timeout" /> is observed between units — a page, an archive part, an
/// element — because no parser here takes a cancellation token, so a PDF whose one page inflates enormously is bounded
/// by <see cref="AttachmentTextExtractionOptions.MaxInputOctets" /> on the octets that arrived and by nothing else. On
/// a synchronization stage that costs a worker; on a send being screened or an attachment being served it costs the
/// caller, on a request nothing here will cut short. It is recorded rather than defended, and issue #1684 is where the
/// gap is tracked.
/// </para>
/// <para>
/// An attachment an antivirus pass has judged infected is not excluded here, because no such pass exists yet. When one
/// lands, this is the port it gates: an infected attachment is skipped before a parser is offered its bytes.
/// </para>
/// </remarks>
public interface IAttachmentTextExtractor
{
    /// <summary>Reads the text of one opened attachment.</summary>
    /// <param name="attachment">The attachment, opened from stored content and owned by the caller.</param>
    /// <param name="cancellationToken">Cancels the extraction.</param>
    /// <returns>The text the attachment yielded, or the reason it yielded none.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="attachment" /> is <see langword="null" />.</exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken" /> is cancelled. The configured timeout elapsing is reported as
    /// <see cref="AttachmentTextExtractionOutcome.TimedOut" /> instead, because that is a fact about the attachment
    /// rather than about the caller.
    /// </exception>
    /// <remarks>
    /// A failure reading the attachment's stored content propagates as whatever the content store raised, rather than
    /// becoming an outcome. Every outcome here is a durable fact about the document — the caller may record one against
    /// the attachment and never look again — and a database or object-storage fault is a transient fact about this
    /// attempt, so reporting one as <see cref="AttachmentTextExtractionOutcome.Malformed" /> would write a broken
    /// document into the record on the strength of a connection that dropped. Only what a parser does with the octets
    /// once they have arrived becomes a reason.
    /// </remarks>
    Task<AttachmentTextExtractionResult> ExtractTextAsync(
        IOpenedEmailAttachment attachment,
        CancellationToken cancellationToken);
}
