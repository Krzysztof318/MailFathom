// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Cli.Output;

namespace MailFathom.Cli.Diagnostics;

/// <summary>The terminal, remembering the last line a command reported as a failure.</summary>
/// <remarks>
/// <para>
/// Most commands report a refusal by raising a <see cref="CliFailure" />, which carries its message to the runner. A
/// dozen do not: a contact the book does not hold, a job no longer dead-lettered, a confirmation declined, an erasure
/// interrupted — each is an ordinary outcome rather than a defect, so the command writes one sentence and returns a
/// failing code. Nothing about that reaches the runner, which would record those invocations as failures with nothing
/// said about them, and that is the one shape the log's own table promises never to have.
/// </para>
/// <para>
/// <see cref="WriteError" /> alone, which is narrower than the stream it goes to and is what makes this safe to record
/// rather than merely convenient. Standard error carries three kinds of line and the console names each: guidance
/// through <see cref="WriteNotice" />, something to weigh through <see cref="WriteWarning" />, and a failure here. Only
/// the last is a refusal, so a device-code prompt and a version caution pass through untouched — and what a command was
/// asked for never appears on any of the three, since a contact, an address, a subject, or a message is written to
/// standard output as the command's result.
/// </para>
/// <para>
/// Every such sentence is already written to be read beside a failing exit code, and already avoids naming a person:
/// <see cref="Commands.Contacts.ContactOutput" /> states the rule for the commands that handle people, and the reason
/// it gives is this one — a failure message ends up in a log wherever the command is run from a script.
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

    /// <summary>Gets the last sentence written as a failure, and <see langword="null" /> when none was.</summary>
    /// <remarks>
    /// The last rather than the first, because a command that writes several ends on the one that says what to do next,
    /// and a blank line written for spacing is skipped so a refusal cannot be displaced by one.
    /// </remarks>
    internal string? LastRefusal { get; private set; }

    /// <inheritdoc />
    public void WriteLine(string message) => this.terminal.WriteLine(message);

    /// <inheritdoc />
    public void WriteNotice(string message) => this.terminal.WriteNotice(message);

    /// <inheritdoc />
    public void WriteWarning(string message) => this.terminal.WriteWarning(message);

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
    public void Write(CliTable table) => this.terminal.Write(table);

    /// <inheritdoc />
    public void Write(CliDetails details) => this.terminal.Write(details);

    /// <inheritdoc />
    public string ReadSecret(string prompt) => this.terminal.ReadSecret(prompt);

    /// <inheritdoc />
    public bool CanConfirm => this.terminal.CanConfirm;

    /// <inheritdoc />
    public bool Confirm(string question) => this.terminal.Confirm(question);
}
