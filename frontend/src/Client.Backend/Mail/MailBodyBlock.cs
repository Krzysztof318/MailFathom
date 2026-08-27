// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Client.Backend.Mail;

/// <summary>One typed part of a message body as the deployment reduced it.</summary>
/// <remarks>
/// <para>
/// The client's own statement of the contract rather than a type shared with the service, as everything crossing this
/// boundary is. What makes it safe to draw is that no member anywhere below is markup, an expression, or a reference to
/// code: every one of them is text, a number, a colour, an identity, or a value from a closed set, and the pane draws
/// them with typed controls it already has.
/// </para>
/// <para>
/// A block the deployment sends that this build does not implement — a type it does not know, or a revision of a type
/// it does — arrives as <see cref="MailBodyUnsupportedBlock" /> and costs the reader that block rather than the
/// message. Reading it as the revision this build knows would be worse than either: the members the newer revision
/// added would be dropped and the block would present as though nothing were missing.
/// </para>
/// </remarks>
[JsonConverter(typeof(MailBodyBlockJsonConverter))]
public abstract record MailBodyBlock
{
    private protected MailBodyBlock()
    {
    }
}

/// <summary>One run of a message's body text, drawn as a paragraph.</summary>
/// <param name="Content">The runs the paragraph holds, in reading order.</param>
/// <param name="Alignment">How the paragraph places its content across the width it was given.</param>
public sealed record MailBodyParagraphBlock(
    IReadOnlyList<MailBodyRun> Content,
    MailBodyAlignment Alignment) : MailBodyBlock;

/// <summary>A heading the message wrote, at the level it wrote it.</summary>
/// <param name="Level">The heading level, from one to six.</param>
/// <param name="Content">The runs the heading holds, in reading order.</param>
/// <param name="Alignment">How the heading places its content across the width it was given.</param>
/// <remarks>
/// The level is what a screen reader announces the structure of a message from, and it is drawn with the pane's own
/// typography for that level rather than with a size the sender chose.
/// </remarks>
public sealed record MailBodyHeadingBlock(
    int Level,
    IReadOnlyList<MailBodyRun> Content,
    MailBodyAlignment Alignment) : MailBodyBlock;

/// <summary>A bulleted or numbered list.</summary>
/// <param name="Ordered">Whether the list numbers its items rather than bulleting them.</param>
/// <param name="Items">The items, in the order the message wrote them.</param>
public sealed record MailBodyListBlock(
    bool Ordered,
    IReadOnlyList<MailBodyListItem> Items) : MailBodyBlock;

/// <summary>One item of a list, and everything it holds.</summary>
/// <param name="Blocks">What the item holds, in reading order.</param>
public sealed record MailBodyListItem(IReadOnlyList<MailBodyBlock> Blocks);

/// <summary>A table, which in mail is as often the layout as it is data.</summary>
/// <param name="Columns">One entry per column, in order.</param>
/// <param name="Rows">The rows, in the order the message wrote them.</param>
public sealed record MailBodyTableBlock(
    IReadOnlyList<MailBodyTableColumn> Columns,
    IReadOnlyList<MailBodyTableRow> Rows) : MailBodyBlock;

/// <summary>What is true of a whole column of a table.</summary>
/// <param name="WidthShare">The share of the table's width the column asked for, or <see langword="null" /> where it asked for none.</param>
public sealed record MailBodyTableColumn(double? WidthShare);

/// <summary>One row of a table.</summary>
/// <param name="IsHeader">Whether the row labels the columns rather than holding data.</param>
/// <param name="Cells">The cells, in order.</param>
public sealed record MailBodyTableRow(bool IsHeader, IReadOnlyList<MailBodyTableCell> Cells);

/// <summary>One cell of a table, and everything it holds.</summary>
/// <param name="ColumnSpan">How many columns the cell covers.</param>
/// <param name="RowSpan">How many rows the cell covers.</param>
/// <param name="Alignment">How the cell places its content across its own width.</param>
/// <param name="Background">The colour the message asked the cell to be, in <c>#rrggbb</c>, or <see langword="null" /> where it asked for none.</param>
/// <param name="Blocks">What the cell holds, in reading order.</param>
public sealed record MailBodyTableCell(
    int ColumnSpan,
    int RowSpan,
    MailBodyAlignment Alignment,
    string? Background,
    IReadOnlyList<MailBodyBlock> Blocks);

/// <summary>Quoted history, at the depth the message quoted it.</summary>
/// <param name="Depth">How deep the quotation is, counting from one.</param>
/// <param name="Blocks">What the quotation holds, in reading order.</param>
public sealed record MailBodyQuoteBlock(
    int Depth,
    IReadOnlyList<MailBodyBlock> Blocks) : MailBodyBlock;

/// <summary>A picture the message displays, and where following it goes.</summary>
/// <param name="Image">The picture itself.</param>
/// <param name="Link">Where following the picture goes, or <see langword="null" /> where it goes nowhere.</param>
/// <param name="Alignment">How the picture sits across the width it was given.</param>
public sealed record MailBodyImageBlock(
    MailBodyImage Image,
    MailBodyLink? Link,
    MailBodyAlignment Alignment) : MailBodyBlock;

/// <summary>A horizontal rule the message drew between two parts of itself.</summary>
public sealed record MailBodySeparatorBlock : MailBodyBlock;

/// <summary>Text whose own line breaks and spacing are part of what it says.</summary>
/// <param name="Text">The text as the message wrote it, whitespace included.</param>
/// <remarks>
/// The pane must not re-wrap it. Collapsing the whitespace of a code sample or a diff would change what the message
/// says, which is the whole reason this is a block of its own rather than monospaced runs.
/// </remarks>
public sealed record MailBodyPreformattedBlock(string Text) : MailBodyBlock;

/// <summary>A block this build does not implement, which is drawn as an absence rather than guessed at.</summary>
/// <param name="Identity">The block's own identity as the deployment named it, or <see langword="null" /> where it named none.</param>
/// <param name="Version">The revision the deployment claimed for it.</param>
/// <remarks>
/// It is the client's own value rather than something the wire carries. A deployment and a desktop head are updated
/// separately, so a client meeting a block from a service ahead of it is ordinary rather than exceptional — and the
/// answer to it is to say so in the pane and go on drawing the rest of the message.
/// </remarks>
public sealed record MailBodyUnsupportedBlock(string? Identity, int Version) : MailBodyBlock;
