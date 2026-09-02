// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Rendering.Document.Blocks;

/// <summary>A heading the message wrote, at the level it wrote it.</summary>
/// <remarks>
/// The level is kept rather than flattened into an emphasis, because it is what a screen reader announces the structure
/// of a message from. A pane draws it with the typography its own design system gives that level, so a sender cannot
/// decide how large a heading is in somebody else's reading pane.
/// </remarks>
public sealed record MailHeadingBlock : MailDocumentBlock
{
    /// <summary>The shallowest heading level, which is the one <c>h1</c> reduces to.</summary>
    public const int ShallowestLevel = 1;

    /// <summary>The deepest heading level, which is the one <c>h6</c> reduces to.</summary>
    public const int DeepestLevel = 6;

    /// <summary>Initializes a heading.</summary>
    /// <param name="level">The heading level, between <see cref="ShallowestLevel" /> and <see cref="DeepestLevel" />.</param>
    /// <param name="content">The runs the heading holds, in reading order.</param>
    /// <param name="alignment">How the heading places its content across the width it was given.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="content" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the level names no heading this contract holds.</exception>
    public MailHeadingBlock(int level, IReadOnlyList<MailInlineRun> content, MailBlockAlignment alignment)
        : base(MailDocumentBlockType.Heading)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentOutOfRangeException.ThrowIfLessThan(level, ShallowestLevel);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(level, DeepestLevel);

        this.Level = level;
        this.Content = content;
        this.Alignment = alignment;
    }

    /// <summary>Gets the heading level the message wrote.</summary>
    public int Level { get; }

    /// <summary>Gets the runs the heading holds, in reading order.</summary>
    public IReadOnlyList<MailInlineRun> Content { get; }

    /// <summary>Gets how the heading places its content across the width it was given.</summary>
    public MailBlockAlignment Alignment { get; }
}
