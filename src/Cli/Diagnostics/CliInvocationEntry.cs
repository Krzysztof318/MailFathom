// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Cli.Diagnostics;

/// <summary>One line of the invocation log: what was run, against which deployment, and how it ended.</summary>
/// <remarks>
/// <para>
/// <strong>No credential and no mail reaches this type.</strong> That is the line, and it holds by construction rather
/// than by filtering: a credential is never in a <see cref="CliFailure" /> message, because the failure rules forbid it
/// there for the terminal's sake already, and nothing a command printed is offered to this type at all — a contact, an
/// address, a subject, or a message is the answer to the command rather than a fact about running it.
/// </para>
/// <para>
/// <strong>The operator's own deployment can be named here, and is.</strong> <see cref="Command" /> is the path of
/// names <c>mfctl</c> declares and carries no argument value, but the other two fields are not blind to where a
/// deployment is: <see cref="Deployment" /> is the operator's name for a profile, which for a sign-in that passed no
/// <c>--name</c> is the deployment's own host; and <see cref="Failure" /> is the line the command already printed to
/// the terminal, which for several failures quotes the address or the alias that was typed. Scrubbing them was
/// considered and rejected — the file sits beside a credential store that records every profile's endpoint in clear, so
/// a log that named none of them would be protecting an address the directory it lives in already holds, at the cost of
/// the field an operator reads the log for.
/// </para>
/// </remarks>
/// <param name="At">When the invocation started.</param>
/// <param name="Command">The command that ran, as the declared names from <c>mfctl</c> down.</param>
/// <param name="Outcome">How it ended.</param>
/// <param name="DurationMilliseconds">How long it took, from the process composing its context to the exit code being decided.</param>
internal sealed record CliInvocationEntry(
    DateTimeOffset At,
    string Command,
    CliInvocationOutcome Outcome,
    long DurationMilliseconds)
{
    /// <summary>Gets the code the invocation reported, and <see langword="null" /> when it faulted before reporting one.</summary>
    public int? ExitCode { get; init; }

    /// <summary>Gets the operator's name for the deployment the command reached, and <see langword="null" /> when it reached none.</summary>
    public string? Deployment { get; init; }

    /// <summary>Gets the operator-readable failure the command printed, and <see langword="null" /> when it printed none.</summary>
    public string? Failure { get; init; }
}
