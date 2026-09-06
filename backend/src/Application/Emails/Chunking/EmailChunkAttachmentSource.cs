// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Chunking;

/// <summary>Names the attachment a passage is a span of, which a passage cut from the message body is not a span of anything but.</summary>
/// <remarks>
/// <para>
/// This is the one thing
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0029-what-an-embedding-is-derived-from-and-whether-attachment-text-joins-it.md">ADR 0029</see>
/// adds to a passage: an offset names a span in a text, and an attachment-derived passage indexes a different text
/// from the body-derived passage beside it, so the row has to say which. The position is the message's own walk order —
/// the same coordinate the download route is addressed with and the same one a citation resolves — because a file name
/// is text a sender chose and is neither unique nor required.
/// </para>
/// <para>
/// Both members reach <see cref="EmailChunkContentHash" />, and that is what keeps the two kinds of passage from
/// colliding: identical words read out of the second attachment of a message and out of its body are not one passage
/// under two names, and neither are identical words read out of a file that declared itself a spreadsheet and one that
/// declared itself a document. The media type is the sender's declaration rather than a reading of the octets, which is
/// the honest thing to record — it is what decided which parser was offered the file.
/// </para>
/// </remarks>
public sealed record EmailChunkAttachmentSource
{
    private EmailChunkAttachmentSource(int position, string declaredMediaType)
    {
        this.Position = position;
        this.DeclaredMediaType = declaredMediaType;
    }

    /// <summary>Gets the zero-based place the attachment holds in the order the message's structure is walked.</summary>
    public int Position { get; }

    /// <summary>Gets the media type the part declared, which is what chose the parser its text came from.</summary>
    public string DeclaredMediaType { get; }

    /// <summary>Builds the source of a passage cut from one attachment.</summary>
    /// <param name="position">The zero-based walk position of the attachment.</param>
    /// <param name="declaredMediaType">The media type the part declared.</param>
    /// <returns>The source.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="declaredMediaType" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="position" /> is negative.</exception>
    public static EmailChunkAttachmentSource Create(int position, string declaredMediaType)
    {
        ArgumentNullException.ThrowIfNull(declaredMediaType);
        ArgumentOutOfRangeException.ThrowIfNegative(position);

        return new EmailChunkAttachmentSource(position, declaredMediaType);
    }
}
