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
