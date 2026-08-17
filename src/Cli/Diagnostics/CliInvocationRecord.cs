// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Cli.Diagnostics;

/// <summary>Collects what one invocation turns out to have done, across the layers that each know part of it.</summary>
/// <remarks>
/// <para>
/// Mutable, and deliberately so: the runner knows which command was named and what it exited with, and only the access
/// seam knows which deployment was reached. Neither can produce the whole line, and threading a return value up through
/// every command for the sake of one field would put the log into signatures that have nothing to do with it.
/// </para>
/// <para>
/// It starts timing as the context is composed rather than as the parser finishes, so a slow invocation is slow by the
/// measure the operator experienced. The elapsed time comes from <see cref="TimeProvider.GetTimestamp" /> rather than
/// from subtracting two wall-clock readings, so a clock adjusted mid-command cannot produce a negative duration.
/// </para>
/// </remarks>
internal sealed class CliInvocationRecord
{
    /// <summary>The greatest length any recorded text may have, so one line of the log stays one line of it.</summary>
    /// <remarks>
    /// Every value this records is written for an operator to read — a failure sentence, a type's name, a profile name
    /// they chose — so the ceiling is far above what any of them reaches. It exists because a log is appended to on
    /// every invocation and nothing else bounds what a value could grow into: a message is composed rather than
    /// declared, a closed generic type's name carries its arguments recursively, and a profile name is refused only for
    /// being blank or for parsing as an address.
    /// </remarks>
    internal const int MaximumFailureLength = 512;

    private readonly TimeProvider clock;
    private readonly DateTimeOffset startedAt;
    private readonly long startedTimestamp;

    /// <summary>Starts a record, timing from now.</summary>
    /// <param name="clock">What the start and the elapsed time are read from.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="clock" /> is <see langword="null" />.</exception>
    internal CliInvocationRecord(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        this.clock = clock;
        this.startedAt = clock.GetUtcNow();
        this.startedTimestamp = clock.GetTimestamp();
    }

    private string? Deployment { get; set; }

    /// <summary>Notes which deployment the command settled on, once it has one.</summary>
    /// <param name="profileName">The operator's own name for the deployment.</param>
    /// <remarks>
    /// Two things call this and neither calls it twice. Every command that reaches a deployment goes through
    /// <see cref="Administration.DeploymentAccess" />, which is where the option, the variable, and the stored default
    /// are reconciled; and <c>login</c> calls it directly, because it establishes a profile rather than resolving one
    /// and would otherwise be the command with no deployment on its record while being the command that names them.
    /// The last one still wins, so a command that ever grew a second deployment would report the one it ended on.
    /// </remarks>
    internal void ReachedDeployment(string profileName) => this.Deployment = profileName;

    /// <summary>Closes the record for an invocation that reported an exit code.</summary>
    /// <param name="command">The command that ran, as the declared names from <c>mfctl</c> down.</param>
    /// <param name="exitCode">What it reported.</param>
    /// <param name="failure">The operator-readable failure it printed, or <see langword="null" />.</param>
    /// <returns>The line to append.</returns>
    internal CliInvocationEntry Ended(string command, int exitCode, string? failure) => this.Entry(
        command,
        exitCode == CliExitCode.Success ? CliInvocationOutcome.Completed : CliInvocationOutcome.Failed) with
    {
        ExitCode = exitCode,
        Failure = Bounded(failure),
    };

    /// <summary>Closes the record for an invocation that raised something rather than reporting an exit code.</summary>
    /// <param name="command">The command that ran, as the declared names from <c>mfctl</c> down.</param>
    /// <param name="fault">What it raised, whose type is recorded and whose message and stack are not.</param>
    /// <returns>The line to append.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="fault" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// This is the invocation the log is most worth having for — a crash puts its stack on the terminal and nowhere
    /// else, so once the scrollback is gone the log is all there is. <see cref="CliInvocationEntry.Fault" /> holds what
    /// can be recorded of it safely.
    /// </remarks>
    internal CliInvocationEntry Faulted(string command, Exception fault)
    {
        ArgumentNullException.ThrowIfNull(fault);

        // Bounded like the failure and for the same reason: a closed generic type's full name carries every argument's
        // name recursively, so the one field that looks too short to need a ceiling is the one that has no other.
        return this.Entry(command, CliInvocationOutcome.Faulted) with { Fault = Bounded(fault.GetType().FullName) };
    }

    /// <summary>Closes the record for an invocation the operator stopped.</summary>
    /// <param name="command">The command that ran, as the declared names from <c>mfctl</c> down.</param>
    /// <returns>The line to append.</returns>
    /// <remarks>Its own ending rather than a fault, because Ctrl+C is the operator doing something deliberate and a log that read it as a crash would send somebody looking for one.</remarks>
    internal CliInvocationEntry Cancelled(string command) => this.Entry(command, CliInvocationOutcome.Cancelled);

    /// <summary>Cuts a recorded value to the ceiling without splitting a character in half.</summary>
    /// <remarks>
    /// A value can carry an alias or a profile name an operator chose, so a character outside the basic plane can
    /// straddle the ceiling — and cutting between the two halves of a surrogate pair leaves a string that is not valid
    /// UTF-16. The JSON writer refuses one outright, which would put an exception into the append that runs in the
    /// runner's <c>finally</c> and mask whatever the invocation was already reporting.
    /// </remarks>
    private static string? Bounded(string? value)
    {
        if (value is not { Length: > MaximumFailureLength })
        {
            return value;
        }

        var length = char.IsHighSurrogate(value[MaximumFailureLength - 1])
            ? MaximumFailureLength - 1
            : MaximumFailureLength;

        return value[..length];
    }

    private CliInvocationEntry Entry(string command, CliInvocationOutcome outcome) => new(
        this.startedAt,
        command,
        outcome,
        (long)this.clock.GetElapsedTime(this.startedTimestamp).TotalMilliseconds)
    {
        // Bounded like the other two, and for the reason neither of them shows: this one is the operator's own text
        // rather than the command's, and a profile name is refused only for being blank or for parsing as an address.
        Deployment = Bounded(this.Deployment),
    };
}
