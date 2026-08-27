// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Rendering.Document.Blocks;

/// <summary>A table, which in mail is as often the layout as it is data.</summary>
/// <remarks>
/// <para>
/// The block that makes this rendering path more faithful than a reading of the words. Mail layout has been built from
/// tables for twenty years because that is what mail clients render, and a table maps onto a grid with no engine
/// behind it — so a two-column newsletter is two columns here.
/// </para>
/// <para>
/// A column's width is a share of the table rather than a measurement, which is the whole of why it is safe: a share
/// resolves inside the parent whatever the parent turns out to be, while a pixel width resolves against the sender's
/// assumption about a window they cannot see.
/// </para>
/// </remarks>
public sealed record MailTableBlock : MailDocumentBlock
{
    /// <summary>Initializes a table.</summary>
    /// <param name="columns">One entry per column, in order, describing what is true of the whole column.</param>
    /// <param name="rows">The rows, in the order the message wrote them.</param>
    /// <exception cref="ArgumentNullException">Thrown when either argument is <see langword="null" />.</exception>
    public MailTableBlock(IReadOnlyList<MailTableColumn> columns, IReadOnlyList<MailTableRow> rows)
        : base(MailDocumentBlockType.Table)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);

        this.Columns = columns;
        this.Rows = rows;
    }

    /// <summary>Gets one entry per column, in order.</summary>
    public IReadOnlyList<MailTableColumn> Columns { get; }

    /// <summary>Gets the rows, in the order the message wrote them.</summary>
    public IReadOnlyList<MailTableRow> Rows { get; }
}

/// <summary>What is true of a whole column of a table.</summary>
/// <param name="WidthShare">The share of the table's width the column asked for, between zero and one, or <see langword="null" /> where it asked for none.</param>
/// <remarks>
/// A share rather than a width. Whatever the message wrote — a percentage, a pixel count against a stated table width,
/// or nothing — is resolved into a proportion during the reduction, so the one dimensional property that crosses the
/// wire cannot describe a size the pane does not own.
/// </remarks>
public sealed record MailTableColumn(double? WidthShare);

/// <summary>One row of a table.</summary>
/// <param name="IsHeader">Whether the row labels the columns rather than holding data, which is what a screen reader announces.</param>
/// <param name="Cells">The cells, in order.</param>
public sealed record MailTableRow(bool IsHeader, IReadOnlyList<MailTableCell> Cells);

/// <summary>One cell of a table, and everything it holds.</summary>
/// <param name="ColumnSpan">How many columns the cell covers, which is one unless the message said otherwise.</param>
/// <param name="RowSpan">How many rows the cell covers, which is one unless the message said otherwise.</param>
/// <param name="Alignment">How the cell places its content across its own width.</param>
/// <param name="Background">The colour the message asked the cell to be, or <see langword="null" /> where it asked for none.</param>
/// <param name="Blocks">What the cell holds, in reading order.</param>
/// <remarks>
/// A cell holds blocks because a mail layout table holds paragraphs, pictures, and further tables inside its cells.
/// The nesting depth that admits is bounded during the reduction rather than here.
/// </remarks>
public sealed record MailTableCell(
    int ColumnSpan,
    int RowSpan,
    MailBlockAlignment Alignment,
    MailDocumentColour? Background,
    IReadOnlyList<MailDocumentBlock> Blocks);
