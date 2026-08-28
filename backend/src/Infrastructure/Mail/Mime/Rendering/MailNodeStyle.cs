// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Rendering.Document;

namespace MailFathom.Infrastructure.Mail.Mime.Rendering;

/// <summary>What one element of a message asked for, reduced to the closed property set the document admits.</summary>
/// <param name="Hidden">Whether the element asked not to be drawn at all, which drops it and everything inside it.</param>
/// <param name="Foreground">The text colour it asked for, or <see langword="null" /> where it asked for none.</param>
/// <param name="Background">The background colour it asked for, or <see langword="null" /> where it asked for none.</param>
/// <param name="Alignment">How it asked its content to sit across the width it was given.</param>
/// <param name="AddedEmphasis">The emphasis it added to whatever it inherited.</param>
/// <param name="RemovedEmphasis">The emphasis it took away from whatever it inherited.</param>
/// <param name="WidthShare">The share of its parent's width it asked for, or <see langword="null" /> where it asked for none or asked in pixels.</param>
/// <param name="PixelWidth">The pixel width it asked for, which a table resolves into a share against its siblings.</param>
internal sealed record MailNodeStyle(
    bool Hidden,
    MailDocumentColour? Foreground,
    MailDocumentColour? Background,
    MailBlockAlignment Alignment,
    MailTextEmphasis AddedEmphasis,
    MailTextEmphasis RemovedEmphasis,
    double? WidthShare,
    double? PixelWidth)
{
    /// <summary>Gets the style of an element that asked for nothing.</summary>
    public static MailNodeStyle None { get; } = new(
        Hidden: false,
        Foreground: null,
        Background: null,
        MailBlockAlignment.Inherited,
        MailTextEmphasis.None,
        MailTextEmphasis.None,
        WidthShare: null,
        PixelWidth: null);
}
