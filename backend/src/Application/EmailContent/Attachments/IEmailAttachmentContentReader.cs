// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;

namespace MailFathom.Application.EmailContent.Attachments;

/// <summary>Opens one attachment of a stored message, by the position the message's walk gave it.</summary>
/// <remarks>
/// <para>
/// A separate port from <see cref="Rendering.IEmailContentRenderer" /> because it answers a different question about the
/// same bytes: the renderer produces everything a reader is shown and holds nothing open afterwards, while this holds
/// one part open so its octets can be streamed out. Sharing one contract would make every read of a body carry the
/// lifetime of a file nobody asked for.
/// </para>
/// <para>
/// An implementation classifies the message exactly as the renderer does, so the position a link names is the position
/// the read that issued the link published. It reaches no mail server and no network: it is handed bytes that were
/// already stored.
/// </para>
/// </remarks>
public interface IEmailAttachmentContentReader
{
    /// <summary>Opens the attachment at one position of a stored message.</summary>
    /// <param name="content">The stored raw MIME.</param>
    /// <param name="attachmentPosition">The zero-based position in the walk order.</param>
    /// <param name="cancellationToken">Cancels the parse.</param>
    /// <returns>The opened attachment, or the reason there is none to open.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="content" /> is <see langword="null" />.</exception>
    Task<OpenedEmailAttachmentResult> OpenAsync(
        StoredEmailContent content,
        int attachmentPosition,
        CancellationToken cancellationToken);
}
