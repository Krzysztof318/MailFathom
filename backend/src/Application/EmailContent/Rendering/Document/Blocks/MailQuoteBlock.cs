// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Rendering.Document.Blocks;

/// <summary>Quoted history, at the depth the message quoted it.</summary>
/// <remarks>
/// <para>
/// Depth is carried rather than left to nesting alone, so a pane can draw the fourth level of a long exchange as the
/// fourth level without walking back up to count. Nothing is trimmed here: a reader asked to read the message, and
/// quoted history is part of what they were sent.
/// </para>
/// <para>
/// The depth a pane draws is bounded by the pane rather than by the message, which is what keeps a body quoted a
/// hundred deep from indenting itself off the side of the reading column.
/// </para>
/// </remarks>
public sealed record MailQuoteBlock : MailDocumentBlock
{
    /// <summary>Initializes a quotation.</summary>
    /// <param name="depth">How deep the quotation is, counting from one for the message's own first level.</param>
    /// <param name="blocks">What the quotation holds, in reading order.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="blocks" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the depth is below one.</exception>
    public MailQuoteBlock(int depth, IReadOnlyList<MailDocumentBlock> blocks)
        : base(MailDocumentBlockType.Quote)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentOutOfRangeException.ThrowIfLessThan(depth, 1);

        this.Depth = depth;
        this.Blocks = blocks;
    }

    /// <summary>Gets how deep the quotation is, counting from one for the message's own first level.</summary>
    public int Depth { get; }

    /// <summary>Gets what the quotation holds, in reading order.</summary>
    public IReadOnlyList<MailDocumentBlock> Blocks { get; }
}
