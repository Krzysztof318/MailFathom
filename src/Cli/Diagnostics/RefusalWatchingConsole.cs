// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Cli.Diagnostics;

/// <summary>The terminal, remembering the last line a command wrote to standard error.</summary>
/// <remarks>
/// <para>
/// Most commands report a refusal by raising a <see cref="CliFailure" />, which carries its message to the runner. A
/// dozen do not: a contact the book does not hold, a job no longer dead-lettered, a confirmation declined, an erasure
/// interrupted — each is an ordinary outcome rather than a defect, so the command writes one sentence and returns a
/// failing code. Nothing about that reaches the runner, which would record those invocations as failures with nothing
/// said about them, and that is the one shape the log's own table promises never to have.
/// </para>
/// <para>
/// Standard error and not standard output, which is what makes this safe to record rather than merely convenient. The
/// split is the one the whole command already keeps: what was asked for goes to standard output, and a contact, an
/// address, a subject, or a message is only ever that. What goes to standard error is written to be read beside a
/// failing exit code, and every such sentence already avoids naming a person — <see cref="Commands.Contacts.ContactOutput" />
/// states the rule for the commands that handle people, and the reason it gives is this one: a failure message ends up
/// in a log wherever the command is run from a script.
/// </para>
/// </remarks>
internal sealed class RefusalWatchingConsole : ICliConsole
{
    private readonly ICliConsole terminal;

    /// <summary>Wraps the terminal a command actually reports to.</summary>
    /// <param name="terminal">The terminal, which receives everything unchanged.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="terminal" /> is <see langword="null" />.</exception>
    internal RefusalWatchingConsole(ICliConsole terminal)
    {
        ArgumentNullException.ThrowIfNull(terminal);

        this.terminal = terminal;
    }

    /// <summary>Gets the last sentence written to standard error, and <see langword="null" /> when none was.</summary>
    /// <remarks>
    /// The last rather than the first, because a command that writes several ends on the one that says what to do next,
    /// and the blank lines the interactive sign-in writes for spacing are skipped so a refusal cannot be displaced by
    /// one.
    /// </remarks>
    internal string? LastRefusal { get; private set; }

    /// <inheritdoc />
    public void WriteLine(string message) => this.terminal.WriteLine(message);

    /// <inheritdoc />
    public void WriteError(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            this.LastRefusal = message;
        }

        this.terminal.WriteError(message);
    }

    /// <inheritdoc />
    public string ReadSecret(string prompt) => this.terminal.ReadSecret(prompt);

    /// <inheritdoc />
    public bool CanConfirm => this.terminal.CanConfirm;

    /// <inheritdoc />
    public bool Confirm(string question) => this.terminal.Confirm(question);
}
