// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Cli.Output;

namespace MailFathom.Cli;

/// <summary>The terminal a command reads a credential from and reports to.</summary>
/// <remarks>
/// A command says what it means and never how it is drawn. Which stream a line goes to is one part of that and what the
/// line reports about itself is the other: standard error carries guidance, a caution, and a failure alike, and only the
/// last two are states an operator should be able to see without reading the words.
/// </remarks>
internal interface ICliConsole
{
    /// <summary>Writes a line an operator reads, which is never part of a command's machine-readable output.</summary>
    /// <param name="message">The line.</param>
    void WriteLine(string message);

    /// <summary>Writes a line that guides the operator through what the command is doing.</summary>
    /// <param name="message">The line.</param>
    /// <remarks>
    /// Standard error, beside the failures, because it is not the command's result: an invocation whose output is
    /// captured takes the result alone and the person at the terminal still reads the address to open and what is being
    /// waited for. It carries no mark, because guidance is not a state — marking it would leave a failure looking like
    /// one more instruction.
    /// </remarks>
    void WriteNotice(string message);

    /// <summary>Writes a line reporting something to weigh before going on.</summary>
    /// <param name="message">The line.</param>
    void WriteWarning(string message);

    /// <summary>Writes a line reporting a failure.</summary>
    /// <param name="message">The line.</param>
    void WriteError(string message);

    /// <summary>Writes a listing of records under its column headings.</summary>
    /// <param name="table">The listing.</param>
    void Write(CliTable table);

    /// <summary>Writes one record as the values it carries under the labels naming them.</summary>
    /// <param name="details">The record.</param>
    void Write(CliDetails details);

    /// <summary>Reads a credential without echoing it.</summary>
    /// <param name="prompt">What to ask for, written only when a person is there to read it.</param>
    /// <returns>The credential, empty when none was supplied.</returns>
    string ReadSecret(string prompt);

    /// <summary>Gets a value indicating whether a person is at the terminal to answer a question.</summary>
    /// <remarks>
    /// A command that would weaken a protection has to ask, and a command whose input is a pipe has nobody to ask: the
    /// answer would be read out of whatever the caller piped in, which for the credential mode is the credential itself.
    /// So the caller checks this first and requires the answer up front instead.
    /// </remarks>
    bool CanConfirm { get; }

    /// <summary>Asks a question that is refused unless it is answered yes.</summary>
    /// <param name="question">The question, written where the answer is typed rather than into a command's output.</param>
    /// <returns><see langword="true" /> only when the answer was yes.</returns>
    /// <remarks>Only called where <see cref="CanConfirm" /> reports that somebody is there; the default is no, so an empty line, an interrupted read, and anything unrecognized all decline.</remarks>
    bool Confirm(string question);
}

/// <summary>The terminal the command actually runs against.</summary>
/// <remarks>
/// <para>
/// Ordinary output goes to standard output and everything else to standard error, so a command whose output is
/// redirected captures the result alone and the operator still sees the prompts and the diagnostics.
/// </para>
/// <para>
/// Each stream is drawn through a renderer of its own, because what the two accept is decided separately: a run whose
/// result is piped into a file still has a person reading its diagnostics, and colouring one of the two has nothing to
/// say about the other.
/// </para>
/// </remarks>
internal sealed class SystemCliConsole : ICliConsole
{
    private readonly CliRenderer output;
    private readonly CliRenderer diagnostics;
    private readonly TextWriter questions;

    /// <summary>Initializes a new instance of the <see cref="SystemCliConsole" /> class.</summary>
    /// <param name="output">Where a command's result is written.</param>
    /// <param name="outputTerminal">What that stream accepts.</param>
    /// <param name="error">Where guidance, cautions, failures, and questions are written.</param>
    /// <param name="errorTerminal">What that stream accepts.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// The streams are given rather than taken from the process, which is what lets a test read back the bytes an
    /// operator would have seen — above all whether an escape sequence was written at all, which is the whole of the
    /// promise made to a redirected run and is unprovable against the process's own console.
    /// </remarks>
    internal SystemCliConsole(
        TextWriter output,
        CliTerminal outputTerminal,
        TextWriter error,
        CliTerminal errorTerminal)
    {
        ArgumentNullException.ThrowIfNull(error);

        this.output = new CliRenderer(output, outputTerminal);
        this.diagnostics = new CliRenderer(error, errorTerminal);
        this.questions = error;
    }

    /// <summary>Builds the terminal the command runs against in production.</summary>
    /// <returns>The terminal.</returns>
    internal static SystemCliConsole ForTerminal() => new(
        Console.Out,
        CliTerminal.ForStandardOutput(),
        Console.Error,
        CliTerminal.ForStandardError());

    /// <inheritdoc />
    public void WriteLine(string message) => this.output.WriteLine(message, CliEmphasis.None);

    /// <inheritdoc />
    public void WriteNotice(string message) => this.diagnostics.WriteLine(message, CliEmphasis.None);

    /// <inheritdoc />
    public void WriteWarning(string message) => this.diagnostics.WriteLine(message, CliEmphasis.Caution);

    /// <inheritdoc />
    public void WriteError(string message) => this.diagnostics.WriteLine(message, CliEmphasis.Failure);

    /// <inheritdoc />
    public void Write(CliTable table) => this.output.Write(table);

    /// <inheritdoc />
    public void Write(CliDetails details) => this.output.Write(details);

    /// <inheritdoc />
    public bool CanConfirm => !Console.IsInputRedirected;

    /// <inheritdoc />
    /// <remarks>
    /// The question goes to standard error beside the prompts, and only <c>y</c> or <c>yes</c> accepts. Reading the
    /// answer as a line rather than as a keystroke is what lets a person correct a typed letter before committing to it,
    /// which matters more here than for a credential nobody re-reads.
    /// </remarks>
    public bool Confirm(string question)
    {
        this.questions.Write(question);

        var answer = Console.In.ReadLine()?.Trim() ?? string.Empty;

        return answer.Equals("y", StringComparison.OrdinalIgnoreCase)
            || answer.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// A piped credential is read as a line, which is what lets a script supply one without a terminal. A typed one is
    /// read key by key with no echo, so it does not stay on the screen or in a scrollback buffer.
    /// </para>
    /// <para>
    /// The prompt is written only when there is a person to read it. Writing it into a pipeline would put it in
    /// whatever the caller captured.
    /// </para>
    /// </remarks>
    public string ReadSecret(string prompt)
    {
        if (Console.IsInputRedirected)
        {
            return Console.In.ReadLine()?.Trim() ?? string.Empty;
        }

        this.questions.Write(prompt);

        var credential = new StringBuilder();

        while (true)
        {
            var key = Console.ReadKey(intercept: true);

            if (key.Key == ConsoleKey.Enter)
            {
                this.questions.WriteLine();

                return credential.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (credential.Length > 0)
                {
                    credential.Length--;
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                credential.Append(key.KeyChar);
            }
        }
    }
}
