// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Rendering.Document;

namespace MailFathom.Infrastructure.Mail.Mime.Rendering;

/// <summary>What an element inherits from the elements it sits inside.</summary>
/// <param name="Depth">How deep the walk is, which is what bounds it.</param>
/// <param name="QuoteDepth">How many quotations the walk is inside.</param>
/// <param name="Emphasis">The emphasis in force.</param>
/// <param name="Foreground">The text colour in force, or <see langword="null" /> where the pane's own decides.</param>
/// <param name="Alignment">The alignment in force.</param>
/// <param name="Link">The link in force, so text inside an anchor carries where it goes.</param>
/// <remarks>
/// Inheritance is carried explicitly rather than resolved from a style engine, which keeps it to the four things the
/// document actually admits. Everything else an element could say about itself stops at that element.
/// </remarks>
internal sealed record MailReductionContext(
    int Depth,
    int QuoteDepth,
    MailTextEmphasis Emphasis,
    MailDocumentColour? Foreground,
    MailBlockAlignment Alignment,
    MailDocumentLink? Link)
{
    /// <summary>Gets the context the body itself is walked in.</summary>
    public static MailReductionContext Root { get; } = new(
        Depth: 0,
        QuoteDepth: 0,
        MailTextEmphasis.None,
        Foreground: null,
        MailBlockAlignment.Inherited,
        Link: null);

    /// <summary>Derives the context one step further in, applying what the element asked for.</summary>
    /// <param name="style">What the element asked for.</param>
    /// <returns>The derived context.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="style" /> is <see langword="null" />.</exception>
    public MailReductionContext Inside(MailNodeStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);

        return this with
        {
            Depth = this.Depth + 1,
            Emphasis = (this.Emphasis | style.AddedEmphasis) & ~style.RemovedEmphasis,
            Foreground = style.Foreground ?? this.Foreground,
            Alignment = style.Alignment is MailBlockAlignment.Inherited ? this.Alignment : style.Alignment,
        };
    }
}
