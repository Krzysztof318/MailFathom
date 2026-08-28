// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using AngleSharp.Dom;
using MailFathom.Application.EmailContent.Rendering.Document;
using MailFathom.Application.EmailContent.Rendering.Document.Blocks;

namespace MailFathom.Infrastructure.Mail.Mime.Rendering;

/// <summary>Reduces one table, which in mail is as often the layout as it is data.</summary>
/// <remarks>
/// <para>
/// Rows are taken from this table alone rather than from every descendant, which is the one thing a naive reduction
/// gets wrong: mail nests tables three and four deep to lay a newsletter out, and a query for every <c>tr</c> beneath a
/// table would pull the inner tables' rows up into the outer one and produce a shape nobody sent.
/// </para>
/// <para>
/// A column's width is resolved into a share of the table here, because that is the only form the document admits. A
/// percentage is a share already; a set of pixel widths is normalized against its own total, which reproduces the
/// proportions the sender drew without carrying the sizes they assumed. Anything else leaves the column with no width
/// and the pane distributes it.
/// </para>
/// </remarks>
internal static class MailTableReducer
{
    /// <summary>Reduces one table element.</summary>
    /// <param name="element">The table as the message wrote it.</param>
    /// <param name="context">What the table inherits.</param>
    /// <param name="reducer">The reduction the cells' own content is produced by.</param>
    /// <returns>The table, or <see langword="null" /> where it held nothing to draw.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    internal static MailDocumentBlock? Reduce(
        IElement element,
        MailReductionContext context,
        MailBodyReducer reducer)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(reducer);

        var bounds = reducer.Bounds;
        var rows = new List<MailTableRow>();

        foreach (var row in RowsOf(element))
        {
            if (rows.Count >= bounds.MaximumTableRows)
            {
                reducer.NoteTruncated();

                break;
            }

            var reduced = ReduceRow(row, context, reducer, bounds);
            if (reduced.Cells.Count > 0)
            {
                rows.Add(reduced);
            }
        }

        return rows.Count == 0 ? null : new MailTableBlock(ColumnsOf(element, rows, reducer), rows);
    }

    /// <summary>Names the rows that belong to this table rather than to one nested inside it.</summary>
    private static IEnumerable<IElement> RowsOf(IElement table) =>
        table.Children.SelectMany(RowsUnder);

    private static IEnumerable<IElement> RowsUnder(IElement child)
    {
        if (IsNamed(child, "tr"))
        {
            return [child];
        }

        return IsNamed(child, "thead") || IsNamed(child, "tbody") || IsNamed(child, "tfoot")
            ? child.Children.Where(grandchild => IsNamed(grandchild, "tr"))
            : [];
    }

    private static MailTableRow ReduceRow(
        IElement row,
        MailReductionContext context,
        MailBodyReducer reducer,
        MailDocumentBounds bounds)
    {
        var rowStyle = MailStyleReader.Read(row);
        if (rowStyle.Hidden)
        {
            // A row that asked not to be drawn is dropped exactly as a hidden cell and a hidden element on the
            // ordinary walk are. Reading only the cells' styles, which is what this did, left the row the one place a
            // message could hide something from every renderer but this one.
            return new MailTableRow(IsHeader: false, []);
        }

        var cells = new List<MailTableCell>();
        var headerSection = row.ParentElement is { } parent && IsNamed(parent, "thead");

        // A row and a cell never pass through the element walk — this reducer reads them directly — so what that walk
        // counts about any other element is counted here instead. A cell is where a message most often puts a
        // reference to somebody else's server, since a background attribute on one is how mail has drawn a picture
        // behind text for twenty years.
        reducer.NoteRemoteReferences(row);

        foreach (var cell in row.Children.Where(child => IsNamed(child, "td") || IsNamed(child, "th")))
        {
            if (cells.Count >= bounds.MaximumTableCells)
            {
                reducer.NoteTruncated();

                break;
            }

            reducer.NoteRemoteReferences(cell);

            var style = MailStyleReader.Read(cell);
            if (style.Hidden)
            {
                continue;
            }

            cells.Add(new MailTableCell(
                SpanOf(cell, "colspan", bounds.MaximumTableCells),
                SpanOf(cell, "rowspan", bounds.MaximumTableRows),
                style.Alignment,
                style.Background,
                reducer.ReduceBlocks(cell, context.Inside(style))));
        }

        var headerRow = headerSection
            || (cells.Count > 0 && row.Children.Where(IsCell).All(child => IsNamed(child, "th")));

        return new MailTableRow(headerRow, cells);
    }

    /// <summary>Describes each column, resolving whatever width the message wrote into a share of the table.</summary>
    /// <remarks>
    /// The count is what the spans declare, and spans multiply: a row of the permitted number of cells each claiming
    /// the permitted span declares that number squared, out of markup a message writes in a kilobyte. So the count is
    /// held to the same bound one row's cells are, because a column is an object on the answer and a definition on the
    /// thread that draws, and the reader is told the table was cut rather than handed the amplification.
    /// </remarks>
    private static IReadOnlyList<MailTableColumn> ColumnsOf(
        IElement table,
        IReadOnlyList<MailTableRow> rows,
        MailBodyReducer reducer)
    {
        var declaredCount = rows.Max(row => row.Cells.Sum(cell => cell.ColumnSpan));
        var columnCount = Math.Min(declaredCount, reducer.Bounds.MaximumTableCells);

        if (columnCount < declaredCount)
        {
            reducer.NoteTruncated();
        }

        return [.. Shares(DeclaredWidths(table, columnCount)).Select(share => new MailTableColumn(share))];
    }

    /// <summary>Reads what each column asked for, preferring an explicit column element to the first row's cells.</summary>
    private static MailNodeStyle[] DeclaredWidths(IElement table, int columnCount)
    {
        var columns = table.Children
            .SelectMany(ColumnCandidatesUnder)
            .Where(child => IsNamed(child, "col"))
            .Take(columnCount)
            .Select(MailStyleReader.Read)
            .ToArray();

        if (columns.Length > 0)
        {
            return Padded(columns, columnCount);
        }

        var firstRow = RowsOf(table).FirstOrDefault();

        return firstRow is null
            ? Padded([], columnCount)
            : Padded(
                [.. firstRow.Children.Where(IsCell).Take(columnCount).Select(MailStyleReader.Read)],
                columnCount);
    }

    private static IEnumerable<IElement> ColumnCandidatesUnder(IElement child) =>
        IsNamed(child, "colgroup") ? child.Children : [child];

    private static MailNodeStyle[] Padded(MailNodeStyle[] declared, int columnCount) =>
    [
        .. Enumerable.Range(0, columnCount)
            .Select(index => index < declared.Length ? declared[index] : MailNodeStyle.None),
    ];

    /// <summary>Turns what the columns asked for into shares, or into nothing where they asked for nothing usable.</summary>
    /// <remarks>
    /// Percentages are taken as written and clamped, because that is already a share of the parent. Pixel widths are
    /// normalized against their own total, which is the only reading of them that cannot resolve to a size the pane
    /// does not own — and it is used only when no column asked in percent, so the two notations are never mixed into
    /// one proportion.
    /// </remarks>
    private static IEnumerable<double?> Shares(MailNodeStyle[] declared)
    {
        if (declared.Any(column => column.WidthShare is not null))
        {
            return declared.Select(column => column.WidthShare is { } share ? Math.Clamp(share, 0, 1) : (double?)null);
        }

        var total = declared.Sum(column => column.PixelWidth ?? 0);

        return total <= 0
            ? declared.Select(_ => (double?)null)
            : declared.Select(column => column.PixelWidth is { } pixels ? pixels / total : (double?)null);
    }

    /// <summary>Reads a span a cell declared, which is a count in the markup rather than a number in anybody's locale.</summary>
    private static int SpanOf(IElement cell, string attribute, int maximum) =>
        int.TryParse(
            cell.GetAttribute(attribute),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var span)
            ? Math.Clamp(span, 1, maximum)
            : 1;

    private static bool IsCell(IElement element) => IsNamed(element, "td") || IsNamed(element, "th");

    private static bool IsNamed(IElement element, string name) =>
        string.Equals(element.LocalName, name, StringComparison.OrdinalIgnoreCase);
}
