// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Spectre.Console;

namespace MailFathom.Cli.Output;

/// <summary>Draws what a command asked to be written, and is the one place that decides how.</summary>
/// <remarks>
/// <para>
/// Every command states what it means — a line, a caution, a failure, a listing, a record — and the drawing happens
/// here. That is the whole reason the shapes beside this file carry no widths and no colours: a listing laid out in the
/// command that read it is a layout decided again per command, which is what the indented blocks this replaced were.
/// </para>
/// <para>
/// Nothing here writes markup. Content reaches the drawing as <see cref="Text" />, which carries no syntax of its own,
/// so a subject, an address, or a rule name holding a bracket is drawn as the operator wrote it rather than parsed as
/// an instruction to the renderer. It also means no escaping step exists to be forgotten at one call site.
/// </para>
/// </remarks>
internal sealed class CliRenderer
{
    /// <summary>How far one column is set from the next, which is what replaces the padding a command used to write into its own strings.</summary>
    private const int ColumnGap = 2;

    private static readonly Style CautionStyle = new(Color.Yellow);
    private static readonly Style FailureStyle = new(Color.Red);
    private static readonly Style LabelStyle = new(Color.Teal);

    private readonly IAnsiConsole console;

    /// <summary>Initializes a new instance of the <see cref="CliRenderer" /> class.</summary>
    /// <param name="writer">The stream to draw on.</param>
    /// <param name="terminal">What that stream accepts.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// The console is built over the writer it was given rather than over the process's own, so what is drawn is
    /// assertable: a test hands it a string writer and reads back the bytes an operator would have seen. Interaction is
    /// off because nothing here prompts — a question is asked by <see cref="ICliConsole.Confirm" />, which reads the
    /// answer as a line and needs no cursor.
    /// </remarks>
    internal CliRenderer(TextWriter writer, CliTerminal terminal)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(terminal);

        this.console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = terminal.PermitsColour ? AnsiSupport.Detect : AnsiSupport.No,
            ColorSystem = terminal.PermitsColour ? ColorSystemSupport.Detect : ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(new TrimmedLineWriter(writer)),
        });

        this.console.Profile.Width = terminal.Width;
    }

    /// <summary>Draws one line.</summary>
    /// <param name="message">The line.</param>
    /// <param name="emphasis">What the line reports about itself.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="message" /> is <see langword="null" />.</exception>
    internal void WriteLine(string message, CliEmphasis emphasis)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.Length == 0)
        {
            this.console.WriteLine();
            this.Flush();

            return;
        }

        this.console.Write(new Text(message, StyleFor(emphasis)));
        this.console.WriteLine();
        this.Flush();
    }

    /// <summary>Draws a listing under its column headings.</summary>
    /// <param name="table">The listing.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="table" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The headings are marked and the values are not, which is the same distinction a record draws between a label and
    /// what is under it, and it is the whole of the structure: the heading row and the alignment under it are what make
    /// this a listing, and a border around it would be decoration. Leaving it off is also what keeps a piped reading
    /// free of glyphs a script would have to strip.
    /// </remarks>
    internal void Write(CliTable table)
    {
        ArgumentNullException.ThrowIfNull(table);

        Table drawing = new()
        {
            Border = TableBorder.None,
            ShowHeaders = true,
            Expand = false,
        };

        var lastColumn = table.Headings.Count - 1;

        foreach (var (heading, column) in table.Headings.Select((heading, column) => (heading, column)))
        {
            // The gap sits to the right of every column but the last, so a row carries no trailing whitespace. That is
            // for the redirected reading rather than for the terminal: trailing spaces are invisible on a screen and are
            // a difference in a file somebody diffs.
            drawing.AddColumn(new TableColumn(new Text(heading, LabelStyle))
            {
                Padding = new Padding(0, 0, column == lastColumn ? 0 : ColumnGap, 0),
            });
        }

        foreach (var row in table.Rows)
        {
            drawing.AddRow([.. row.Select(cell => new Text(cell))]);
        }

        this.console.Write(drawing);
        this.Flush();
    }

    /// <summary>Draws one record as its values under the labels that name them.</summary>
    /// <param name="details">The record.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="details" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A label carrying several values states the label once and sets the rest under it, which is what an operator
    /// reading a person's addresses is looking at: one label, and a column of what is under it.
    /// </remarks>
    internal void Write(CliDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);

        // A record is drawn as a headingless listing of two columns rather than as a grid, because the two shapes pad
        // their last column differently: a grid sets every value out to the width of the widest, which puts trailing
        // spaces on a redirected reading, and a listing does not.
        Table drawing = new()
        {
            Border = TableBorder.None,
            ShowHeaders = false,
            Expand = false,
        };

        drawing.AddColumn(new TableColumn(string.Empty) { Padding = new Padding(0, 0, ColumnGap, 0) });
        drawing.AddColumn(new TableColumn(string.Empty) { Padding = new Padding(0, 0, 0, 0) });

        foreach (var detail in details.Details)
        {
            WriteDetail(drawing, detail);
        }

        this.console.Write(drawing);
        this.Flush();
    }

    /// <summary>Pushes the finished drawing out of the line buffer the trimming writer keeps.</summary>
    /// <remarks>
    /// Every drawing above ends with a newline, so the buffer is already empty by the time this runs. It runs anyway
    /// because that is a property of the drawing library rather than of this code, and a shape that one day ends without
    /// one should reach the operator late rather than not at all.
    /// </remarks>
    private void Flush() => this.console.Profile.Out.Writer.Flush();

    private static void WriteDetail(Table drawing, CliDetail detail)
    {
        if (detail.Values.Count == 0)
        {
            drawing.AddRow(new Text($"{detail.Label}:", LabelStyle), new Text(string.Empty));

            return;
        }

        foreach (var (value, position) in detail.Values.Select((value, position) => (value, position)))
        {
            var label = position == 0 ? new Text($"{detail.Label}:", LabelStyle) : new Text(string.Empty);

            drawing.AddRow(label, new Text(value));
        }
    }

    private static Style StyleFor(CliEmphasis emphasis) => emphasis switch
    {
        CliEmphasis.Caution => CautionStyle,
        CliEmphasis.Failure => FailureStyle,
        _ => Style.Plain,
    };
}
