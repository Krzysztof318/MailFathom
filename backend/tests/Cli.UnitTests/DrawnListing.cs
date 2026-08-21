// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Cli.UnitTests;

/// <summary>Reads a listing a command drew, cell by cell, under the headings an operator reads them under.</summary>
/// <remarks>
/// <para>
/// A listing puts a whole record on one line, so an assertion that a value appears somewhere in the row proves only that
/// the command mentioned it. What a column order is worth is that the value appears under the heading naming it, and two
/// columns swapped leave every value still present in the row — which is exactly the regression a row-wide match cannot
/// see and this reader can.
/// </para>
/// <para>
/// The offsets come from the drawn heading row rather than from a count agreed with the command, so a column inserted
/// between two others moves the reader with it instead of breaking every test that used it.
/// </para>
/// </remarks>
internal sealed class DrawnListing
{
    private readonly IReadOnlyList<(string Heading, int Offset)> columns;

    private DrawnListing(IReadOnlyList<(string Heading, int Offset)> columns, IReadOnlyList<string> rows)
    {
        this.columns = columns;
        this.Rows = rows;
    }

    /// <summary>Gets the rows under the headings, in the order they were drawn.</summary>
    internal IReadOnlyList<string> Rows { get; }

    /// <summary>Finds the listing whose first heading is the one given, and reads its headings and rows.</summary>
    /// <param name="lines">Everything the command wrote to standard output.</param>
    /// <param name="headings">The headings the listing is expected to carry, in order.</param>
    /// <returns>The listing.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no line carries every heading in order.</exception>
    internal static DrawnListing ReadFrom(IReadOnlyList<string> lines, params string[] headings)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(headings);

        var headingRow = lines
            .Select((line, position) => (line, position))
            .First(candidate => Carries(candidate.line, headings));

        var columns = Offsets(headingRow.line, headings);
        var rows = lines
            .Skip(headingRow.position + 1)
            .TakeWhile(line => line.Length > 0);

        return new DrawnListing(columns, [.. rows]);
    }

    /// <summary>Reads one row's cell under the named heading.</summary>
    /// <param name="row">The row, as it was drawn.</param>
    /// <param name="heading">The heading naming the column.</param>
    /// <returns>The cell, empty where the row is shorter than the column starts.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the listing carries no such heading.</exception>
    internal string Cell(string row, string heading)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(heading);

        var column = this.columns
            .Select((candidate, position) => (candidate, position))
            .First(candidate => candidate.candidate.Heading == heading);

        var start = column.candidate.Offset;

        if (row.Length <= start)
        {
            return string.Empty;
        }

        var end = column.position + 1 < this.columns.Count
            ? Math.Min(this.columns[column.position + 1].Offset, row.Length)
            : row.Length;

        return row[start..end].TrimEnd();
    }

    private static bool Carries(string line, IReadOnlyList<string> headings)
    {
        var reached = 0;

        return headings.All(heading =>
        {
            var found = line.IndexOf(heading, reached, StringComparison.Ordinal);
            reached = found < 0 ? reached : found + heading.Length;

            return found >= 0;
        });
    }

    private static List<(string Heading, int Offset)> Offsets(string headingRow, IReadOnlyList<string> headings)
    {
        List<(string Heading, int Offset)> offsets = [];
        var reached = 0;

        foreach (var heading in headings)
        {
            var found = headingRow.IndexOf(heading, reached, StringComparison.Ordinal);
            offsets.Add((heading, found));
            reached = found + heading.Length;
        }

        return offsets;
    }
}
