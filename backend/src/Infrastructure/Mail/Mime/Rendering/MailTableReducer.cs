// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
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
    /// <summary>The elements whose presence in a cell makes the cell a box around content rather than a cell of words.</summary>
    private static readonly string[] BlockElementNames =
    [
        "table", "div", "p", "h1", "h2", "h3", "h4", "h5", "h6", "ul", "ol", "blockquote", "pre", "hr",
    ];

    /// <summary>Answers whether this table lays a message out rather than holding a table of anything.</summary>
    /// <param name="element">The table as the message wrote it.</param>
    /// <returns><see langword="true" /> where the table is the sender's layout and its content is what a reader wants.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="element" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// Two rules, both over the markup rather than over what anybody meant by it. A table that declares itself
    /// presentational is taken at its word, which is the declaration the accessibility guidance has asked mail to write
    /// for twenty years and which every serious template generator writes. Everything else is layout only where it
    /// carries no header — no <c>thead</c> section and no <c>th</c> cell — no row holds more than one cell, and the box
    /// is doing a box's work: it either sits inside another table or holds content that is itself blocks. That is the
    /// single-column wrapper a newsletter nests three and four deep to centre itself in a viewport.
    /// </para>
    /// <para>
    /// Both halves of the second rule are deliberately narrow. A table of two columns is where a schedule, a price list,
    /// and an invoice live, and unwrapping one would run the two columns of every row together into a paragraph — a
    /// loss a reader cannot recover from, against a border they can ignore. And a lone one-column table of sentences is
    /// as likely to be a list somebody drew as a wrapper, so it stays a table until the markup says otherwise. The
    /// failure this admits is a layout table still drawn as a table, which is the direction that costs the less of the
    /// two.
    /// </para>
    /// </remarks>
    internal static bool IsLayout(IElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        if (element.GetAttribute("role") is { } role
            && (role.Equals("presentation", StringComparison.OrdinalIgnoreCase)
                || role.Equals("none", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (element.Children.Any(child => IsNamed(child, "thead")))
        {
            return false;
        }

        var rows = RowsOf(element, reducer: null).ToArray();

        return rows.Length > 0
            && rows.All(row => row.Children.Count(IsCell) <= 1)
            && rows.All(row => !row.Children.Any(child => IsNamed(child, "th")))
            && (element.ParentElement?.Closest("table") is not null
                || rows.SelectMany(row => row.Children.Where(IsCell)).Any(HoldsBlocks));
    }

    /// <summary>Answers whether a cell holds content that is drawn as blocks rather than as words in a line.</summary>
    /// <remarks>
    /// This is what separates a box from a cell: a wrapper's one cell holds the next table, a heading, a paragraph, or
    /// a list, while the cell of a table somebody drew on purpose holds the words themselves. Only the cell's own
    /// children are read, because a paragraph three elements down belongs to whatever box is nearer to it.
    /// </remarks>
    private static bool HoldsBlocks(IElement cell) =>
        cell.Children.Any(child => BlockElementNames.Any(name => IsNamed(child, name)));

    /// <summary>Reduces a layout table to the blocks its cells hold, in the order the message wrote them.</summary>
    /// <param name="element">The table as the message wrote it.</param>
    /// <param name="context">What the content is drawn under, which is what the table sits inside rather than what it asked for.</param>
    /// <param name="reducer">The reduction the cells' own content is produced by.</param>
    /// <returns>The blocks the table held.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    /// <remarks>
    /// The rows and the cells are read exactly as <see cref="Reduce" /> reads them, because they still never pass
    /// through the element walk: a hidden row or cell is dropped and read for what it would have loaded, what a
    /// visible one names is counted, and the walk stops at the same row and cell bounds. Removing the table is a
    /// decision about how the content is drawn and never about what the reader is told the message would have
    /// fetched, nor about how much of a stranger's markup this is willing to walk.
    /// </remarks>
    internal static IReadOnlyList<MailDocumentBlock> Unwrap(
        IElement element,
        MailReductionContext context,
        MailBodyReducer reducer)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(reducer);

        var bounds = reducer.Bounds;
        var unwrapped = new List<MailDocumentBlock>();
        var rowsWalked = 0;

        foreach (var row in RowsOf(element, reducer))
        {
            // The same two bounds Reduce is held to, and for the same reason: the cost of a table is the sender's to
            // choose otherwise. Removing the border is a decision about how the content is drawn, and it is not a
            // reason to walk a shape this reduction would have refused to draw — a wrapper is layout by nesting
            // alone, so a message can put as many rows inside one as it likes.
            if (rowsWalked >= bounds.MaximumTableRows)
            {
                reducer.NoteTruncated();

                break;
            }

            rowsWalked++;

            if (MailStyleReader.Read(row).Hidden)
            {
                reducer.NoteHiddenReferences(row);

                continue;
            }

            reducer.NoteRemoteReferences(row);

            var cellsWalked = 0;

            foreach (var cell in row.Children.Where(IsCell))
            {
                if (cellsWalked >= bounds.MaximumTableCells)
                {
                    reducer.NoteTruncated();

                    break;
                }

                cellsWalked++;

                var style = MailStyleReader.Read(cell);
                if (style.Hidden)
                {
                    reducer.NoteHiddenReferences(cell);

                    continue;
                }

                reducer.NoteRemoteReferences(cell);

                // The cell's own alignment goes with the box it described, for the reason the caller states; its
                // colour and its emphasis are what the sender said about the words themselves and are kept.
                unwrapped.AddRange(reducer.ReduceBlocks(
                    cell,
                    context.Inside(style with { Alignment = MailBlockAlignment.Inherited })));
            }
        }

        return unwrapped;
    }

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

        foreach (var row in RowsOf(element, reducer))
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
    /// <param name="table">The table as the message wrote it.</param>
    /// <param name="reducer">
    /// The reduction, or <see langword="null" /> where the rows are being read for their declared widths alone — that
    /// pass walks the same sections a second time, so counting a section's references there would count each one twice.
    /// </param>
    private static IEnumerable<IElement> RowsOf(IElement table, MailBodyReducer? reducer) =>
        table.Children.SelectMany(child => RowsUnder(child, reducer));

    /// <summary>Names the rows one child of a table contributes, reading a section as the walk reads an element.</summary>
    /// <remarks>
    /// A section never passes through the element walk either, so what that walk does for an element it meets is done
    /// for it here: a section that asked not to be drawn contributes no row, and the references to somebody else's
    /// server it carries are counted. This is the same gap the row and the cell already close, one element further up —
    /// a <c>tbody</c> is where a message would otherwise hide a whole table from every renderer but this one.
    /// </remarks>
    private static IEnumerable<IElement> RowsUnder(IElement child, MailBodyReducer? reducer)
    {
        if (IsNamed(child, "tr"))
        {
            return [child];
        }

        if (!IsNamed(child, "thead") && !IsNamed(child, "tbody") && !IsNamed(child, "tfoot"))
        {
            return [];
        }

        if (reducer is null)
        {
            return child.Children.Where(grandchild => IsNamed(grandchild, "tr"));
        }

        if (MailStyleReader.Read(child).Hidden)
        {
            reducer.NoteHiddenReferences(child);

            return [];
        }

        reducer.NoteRemoteReferences(child);

        return child.Children.Where(grandchild => IsNamed(grandchild, "tr"));
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
            // message could hide something from every renderer but this one — and what it holds is still read for
            // what it would have loaded, because a row is where a tracking pixel fits as comfortably as anywhere.
            reducer.NoteHiddenReferences(row);

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

            var style = MailStyleReader.Read(cell);
            if (style.Hidden)
            {
                reducer.NoteHiddenReferences(cell);

                continue;
            }

            reducer.NoteRemoteReferences(cell);

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

        var firstRow = RowsOf(table, reducer: null).FirstOrDefault();

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
