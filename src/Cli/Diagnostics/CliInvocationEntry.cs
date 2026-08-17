// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Cli.Diagnostics;

/// <summary>One line of the invocation log: what was run, against which deployment, and how it ended.</summary>
/// <remarks>
/// <para>
/// <strong>Nothing the operator typed beyond a declared command name is here.</strong> An argument list is where a
/// deployment address, an account alias, a folder alias, a message identity and — for a sign-in — a credential are, so
/// <see cref="Command" /> is the path of names <c>mfctl</c> itself declares and no argument value reaches this type at
/// all. Neither does any row the command printed: a contact, an address, a subject, or a message is the answer to the
/// command rather than a fact about running it, and this file is exactly the one that gets pasted into a support
/// conversation.
/// </para>
/// <para>
/// <see cref="Deployment" /> is the operator's own name for a profile rather than the address behind it, which is what
/// distinguishes two deployments to the person reading the log and says nothing about where either one is.
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
