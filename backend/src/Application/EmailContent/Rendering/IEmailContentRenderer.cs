// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;

namespace MailFathom.Application.EmailContent.Rendering;

/// <summary>Turns stored raw MIME into the headers, body, and attachments a reader is shown.</summary>
/// <remarks>
/// <para>
/// The port is separate from the reader that feeds the lexical index, because the two want different readings of the
/// same bytes: the index wants text with quoted history and signature blocks removed, and a reader wants the message as
/// it was written. Sharing one contract would force one of them to undo what the other did.
/// </para>
/// <para>
/// It exists at all so the MIME parser and the HTML sanitizer stay inside their adapter. Implementations reach no mail
/// server, no network, and no file system: they are handed bytes that were already stored, and rendering them can
/// therefore neither affect a remote <c>\Seen</c> flag nor fetch a remote image an HTML body points at.
/// </para>
/// </remarks>
public interface IEmailContentRenderer
{
    /// <summary>Renders one stored message.</summary>
    /// <param name="content">The stored raw MIME.</param>
    /// <param name="bounds">What to produce, and how many characters of it a reader may be handed.</param>
    /// <param name="cancellationToken">Cancels the parse.</param>
    /// <returns>The rendering, or the fact that the bytes yielded nothing a reader could be shown.</returns>
    /// <exception cref="ArgumentNullException">Thrown when either argument is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// The HTML representation costs a sanitization pass over untrusted markup, so it is produced only when it was
    /// asked for. A message with no HTML body part returns none of it whether or not it was asked for, which is what
    /// keeps "the caller did not want HTML" and "this message has no HTML" from being reported as the same thing.
    /// </para>
    /// <para>
    /// An implementation applies both bounds and reports which one cut each representation. The plain text is bounded
    /// before the markup, because the caller's default representation must not be the one a shared budget starves.
    /// </para>
    /// <para>
    /// No attachment octet is ever materialized here. An implementation describes every part a message carries — its
    /// name, its media type, and the size it decodes to — and keeps none of what it decoded, so rendering a message
    /// costs the same whether it carries a file or ten.
    /// </para>
    /// </remarks>
    Task<EmailContentRenderingResult> RenderAsync(
        StoredEmailContent content,
        EmailContentRenderingBounds bounds,
        CancellationToken cancellationToken);
}
