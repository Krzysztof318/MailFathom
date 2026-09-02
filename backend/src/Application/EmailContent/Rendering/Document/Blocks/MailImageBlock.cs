// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Rendering.Document.Blocks;

/// <summary>A picture the message displays, and where following it goes.</summary>
/// <remarks>
/// A picture is a block rather than a run because that is what it is in mail: a banner, a logo, or a photograph on a
/// line of its own. The link is here because a clickable banner is the ordinary shape of a newsletter, and a picture
/// that goes somewhere has to say where before it is followed exactly as a worded link does.
/// </remarks>
public sealed record MailImageBlock : MailDocumentBlock
{
    /// <summary>Initializes a picture.</summary>
    /// <param name="image">The picture itself.</param>
    /// <param name="link">Where following the picture goes, or <see langword="null" /> where it goes nowhere.</param>
    /// <param name="alignment">How the picture sits across the width it was given.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="image" /> is <see langword="null" />.</exception>
    public MailImageBlock(MailInlineImage image, MailDocumentLink? link, MailBlockAlignment alignment)
        : base(MailDocumentBlockType.Image)
    {
        ArgumentNullException.ThrowIfNull(image);

        this.Image = image;
        this.Link = link;
        this.Alignment = alignment;
    }

    /// <summary>Gets the picture itself.</summary>
    public MailInlineImage Image { get; }

    /// <summary>Gets where following the picture goes, or <see langword="null" /> where it goes nowhere.</summary>
    public MailDocumentLink? Link { get; }

    /// <summary>Gets how the picture sits across the width it was given.</summary>
    public MailBlockAlignment Alignment { get; }
}
