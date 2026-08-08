// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;

namespace MailFathom.Cli;

/// <summary>The terminal a command reads a credential from and reports to.</summary>
internal interface ICliConsole
{
    /// <summary>Writes a line an operator reads, which is never part of a command's machine-readable output.</summary>
    /// <param name="message">The line.</param>
    void WriteLine(string message);

    /// <summary>Writes a line reporting a failure.</summary>
    /// <param name="message">The line.</param>
    void WriteError(string message);

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
/// Ordinary output goes to standard output and everything else to standard error, so a command whose output is
/// redirected captures the result alone and the operator still sees the prompts and the diagnostics.
/// </remarks>
internal sealed class SystemCliConsole : ICliConsole
{
    /// <inheritdoc />
    public void WriteLine(string message) => Console.Out.WriteLine(message);

    /// <inheritdoc />
    public void WriteError(string message) => Console.Error.WriteLine(message);

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
        Console.Error.Write(question);

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

        Console.Error.Write(prompt);

        var credential = new StringBuilder();

        while (true)
        {
            var key = Console.ReadKey(intercept: true);

            if (key.Key == ConsoleKey.Enter)
            {
                Console.Error.WriteLine();

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
