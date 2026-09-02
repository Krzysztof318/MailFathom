// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Cli.Output;

/// <summary>A listing of records, named by its column headings and holding one row per record.</summary>
/// <remarks>
/// <para>
/// A command states what its records are and this states nothing about how they are drawn: the column widths, the rule
/// under the headings, and whether anything is coloured are decided once, by <see cref="CliRenderer" />. That is what
/// keeps the layout out of the command files, where it was previously decided again for every listing.
/// </para>
/// <para>
/// Every cell is a string the command already formatted, because what a value means is the command's to say and how
/// wide the column holding it turns out to be is not.
/// </para>
/// </remarks>
internal sealed class CliTable
{
    private readonly List<IReadOnlyList<string>> rows = [];

    /// <summary>Initializes a new instance of the <see cref="CliTable" /> class.</summary>
    /// <param name="headings">What each column holds, in order.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="headings" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when no heading was given.</exception>
    internal CliTable(params string[] headings)
    {
        ArgumentNullException.ThrowIfNull(headings);

        if (headings.Length == 0)
        {
            throw new ArgumentException("A listing carries at least one column.", nameof(headings));
        }

        this.Headings = headings;
    }

    /// <summary>Gets what each column holds, in order.</summary>
    internal IReadOnlyList<string> Headings { get; }

    /// <summary>Gets the records, in the order they are read.</summary>
    internal IReadOnlyList<IReadOnlyList<string>> Rows => this.rows;

    /// <summary>Adds one record.</summary>
    /// <param name="cells">The record's values, one per heading and in the same order.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="cells" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the record does not carry one value per heading.</exception>
    /// <remarks>
    /// The count is checked here rather than at the point of drawing, because a row that is short by one is a command
    /// having got its own columns wrong, and the failure is worth reporting where the row was written.
    /// </remarks>
    internal void AddRow(params string[] cells)
    {
        ArgumentNullException.ThrowIfNull(cells);

        if (cells.Length != this.Headings.Count)
        {
            throw new ArgumentException(
                $"This listing has {this.Headings.Count} column(s) and the record carries {cells.Length} value(s).",
                nameof(cells));
        }

        this.rows.Add(cells);
    }
}
