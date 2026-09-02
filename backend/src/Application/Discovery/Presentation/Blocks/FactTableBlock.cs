// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Presentation.Citations;

namespace MailFathom.Application.Discovery.Presentation.Blocks;

/// <summary>Values compared across a known set of columns, where the comparison is the answer.</summary>
/// <remarks>
/// <para>
/// The block for "what did each of them quote", "how do the two versions differ". Its columns come from a closed
/// catalogue, so the client draws headings in the reader's own language and a producer cannot invent a column nobody
/// can label.
/// </para>
/// <para>
/// Every row holds one cell per column, in the columns' order, which the constructor refuses to let drift. A table
/// whose rows disagreed with its header is a rendering nobody can draw and a comparison nobody can trust, and it is the
/// one structural mistake this block can make.
/// </para>
/// </remarks>
public sealed record FactTableBlock : PresentationBlock
{
    /// <summary>The greatest number of columns one table may compare across.</summary>
    public const int MaxColumns = 8;

    /// <summary>The greatest number of rows one table may hold.</summary>
    public const int MaxRows = 50;

    /// <summary>Initializes a comparison across a known set of columns.</summary>
    /// <param name="evidence">What the correspondence does for the table as a whole.</param>
    /// <param name="columns">The columns compared across, in the order they are drawn.</param>
    /// <param name="rows">The rows, in the order the answer is read in.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="evidence" />, <paramref name="columns" />, or <paramref name="rows" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when a column is unspecified or repeated, when either list is empty or oversized, or when a row's cell count does not match the column count.</exception>
    public FactTableBlock(
        PresentationEvidence evidence,
        IReadOnlyList<FactTableColumn> columns,
        IReadOnlyList<FactTableRow> rows)
        : base(PresentationBlockType.FactTable, evidence)
    {
        var declaredColumns = PresentationRequirement.RequiredItems(columns, MaxColumns, nameof(columns));

        if (declaredColumns.Any(column => !column.IsSpecified))
        {
            throw new ArgumentException("A column cannot be the unspecified default.", nameof(columns));
        }

        if (declaredColumns.Distinct().Count() != declaredColumns.Count)
        {
            throw new ArgumentException("A table compares across each column once.", nameof(columns));
        }

        var declaredRows = PresentationRequirement.RequiredItems(rows, MaxRows, nameof(rows));

        if (declaredRows.Any(row => row.Cells.Count != declaredColumns.Count))
        {
            throw new ArgumentException(
                "Every row holds one cell per column, in the columns' order.",
                nameof(rows));
        }

        this.Columns = declaredColumns;
        this.Rows = declaredRows;
    }

    /// <summary>Gets the columns compared across, in the order they are drawn.</summary>
    public IReadOnlyList<FactTableColumn> Columns { get; }

    /// <summary>Gets the rows, in the order the answer is read in.</summary>
    public IReadOnlyList<FactTableRow> Rows { get; }

    /// <inheritdoc />
    public override IEnumerable<PresentationCitationId> ReferencedCitations => base.ReferencedCitations
        .Concat(this.Rows.SelectMany(row => row.Cells).SelectMany(cell => cell.Sources));
}

/// <summary>One row of a fact table, holding one cell per column of the table it belongs to.</summary>
public sealed record FactTableRow
{
    /// <summary>Initializes one row.</summary>
    /// <param name="cells">The cells, in the table's column order.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="cells" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the row holds no cell or more than <see cref="FactTableBlock.MaxColumns" /> of them.</exception>
    public FactTableRow(IReadOnlyList<FactTableCell> cells) =>
        this.Cells = PresentationRequirement.RequiredItems(cells, FactTableBlock.MaxColumns, nameof(cells));

    /// <summary>Gets the cells, in the table's column order.</summary>
    public IReadOnlyList<FactTableCell> Cells { get; }
}

/// <summary>One cell of a fact table: the value as the correspondence wrote it, and what it was read from.</summary>
/// <remarks>
/// A cell the correspondence says nothing about carries no value, which is different from a cell whose value is blank.
/// Presenting the two alike is how a comparison quietly asserts that one side offered nothing when nobody asked them.
/// </remarks>
public sealed record FactTableCell
{
    /// <summary>Initializes one cell.</summary>
    /// <param name="value">The value as the correspondence wrote it, or <see langword="null" /> where the correspondence says nothing.</param>
    /// <param name="sources">The citations this cell rests on.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sources" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when a citation is unspecified or named twice, or when a cell with no value names a source.</exception>
    public FactTableCell(PresentationText? value, IReadOnlyList<PresentationCitationId> sources)
    {
        if (value is { } stated)
        {
            PresentationRequirement.Specified(stated, nameof(value));
        }

        var citedSources = PresentationRequirement.Sources(sources, nameof(sources));

        if (value is null && citedSources.Count != 0)
        {
            throw new ArgumentException(
                "A cell the correspondence says nothing about rests on nothing.",
                nameof(sources));
        }

        this.Value = value;
        this.Sources = citedSources;
    }

    /// <summary>Gets the value as the correspondence wrote it, or <see langword="null" /> where the correspondence says nothing.</summary>
    public PresentationText? Value { get; }

    /// <summary>Gets the citations this cell rests on.</summary>
    public IReadOnlyList<PresentationCitationId> Sources { get; }
}
