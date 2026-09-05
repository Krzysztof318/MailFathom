// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Attachments;

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
/// Reading an attachment is the largest attack surface this system has, so an implementation runs it as background work
/// and never on a path a caller waits on — not a synchronization transaction, not an MCP request, not a client read.
/// Everything it consumes is bounded by <see cref="AttachmentTextExtractionOptions" />, nothing it reads is written to
/// the file system, and nothing a document declares as a macro, a script, an embedded object, or any other active
/// content is executed, evaluated, or handed to anything that would.
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
    Task<AttachmentTextExtractionResult> ExtractTextAsync(
        IOpenedEmailAttachment attachment,
        CancellationToken cancellationToken);
}
