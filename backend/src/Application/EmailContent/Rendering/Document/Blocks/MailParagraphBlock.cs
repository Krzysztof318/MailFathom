// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Rendering.Document.Blocks;

/// <summary>One run of a message's body text, drawn as a paragraph.</summary>
/// <remarks>
/// The block most of a message reduces to. Everything the sender wrote about how the words look travels on the runs
/// rather than here, so a paragraph carries only what is true of the whole of it.
/// </remarks>
public sealed record MailParagraphBlock : MailDocumentBlock
{
    /// <summary>Initializes a paragraph.</summary>
    /// <param name="content">The runs the paragraph holds, in reading order.</param>
    /// <param name="alignment">How the paragraph places its content across the width it was given.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="content" /> is <see langword="null" />.</exception>
    public MailParagraphBlock(IReadOnlyList<MailInlineRun> content, MailBlockAlignment alignment)
        : base(MailDocumentBlockType.Paragraph)
    {
        ArgumentNullException.ThrowIfNull(content);

        this.Content = content;
        this.Alignment = alignment;
    }

    /// <summary>Gets the runs the paragraph holds, in reading order.</summary>
    public IReadOnlyList<MailInlineRun> Content { get; }

    /// <summary>Gets how the paragraph places its content across the width it was given.</summary>
    public MailBlockAlignment Alignment { get; }
}
