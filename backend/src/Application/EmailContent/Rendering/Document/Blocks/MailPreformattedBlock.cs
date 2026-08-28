// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Rendering.Document.Blocks;

/// <summary>Text whose own line breaks and spacing are part of what it says.</summary>
/// <remarks>
/// What a <c>&lt;pre&gt;</c> reduces to, which in mail is a code sample, a diff, or terminal output. It is a block of
/// its own rather than a paragraph of monospaced runs because a pane must not re-wrap it: collapsing its whitespace
/// would change what the message says, which is precisely the thing this block exists to prevent.
/// </remarks>
public sealed record MailPreformattedBlock : MailDocumentBlock
{
    /// <summary>Initializes preformatted text.</summary>
    /// <param name="text">The text as the message wrote it, whitespace included.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="text" /> is <see langword="null" />.</exception>
    public MailPreformattedBlock(string text)
        : base(MailDocumentBlockType.Preformatted)
    {
        ArgumentNullException.ThrowIfNull(text);

        this.Text = text;
    }

    /// <summary>Gets the text as the message wrote it, whitespace included.</summary>
    public string Text { get; }
}
