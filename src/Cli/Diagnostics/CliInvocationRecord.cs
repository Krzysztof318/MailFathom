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
    /// <summary>The greatest length a recorded failure may have, so one line of the log stays one line of it.</summary>
    /// <remarks>
    /// Every failure this records is a sentence written for an operator to read, so the ceiling is far above what any
    /// of them reaches. It exists because a log is appended to on every invocation and nothing else bounds what a
    /// message could grow into.
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
    /// The last one wins, which is the right answer for the one command that reaches two: a sign-in talks to an
    /// authorization server before it talks to the deployment, and the deployment is what the invocation was about.
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

    /// <summary>Closes the record for an invocation that never reported an exit code.</summary>
    /// <param name="command">The command that ran, as the declared names from <c>mfctl</c> down.</param>
    /// <returns>The line to append.</returns>
    /// <remarks>
    /// What the command raised is deliberately not read. A defect's message is written for a developer and quotes
    /// whatever it was working on, which for this command is mail — so the fact that it faulted is recorded and the
    /// text of it is left to the stack trace the operator already saw.
    /// </remarks>
    internal CliInvocationEntry Faulted(string command) => this.Entry(command, CliInvocationOutcome.Faulted);

    private static string? Bounded(string? failure) =>
        failure is { Length: > MaximumFailureLength } ? failure[..MaximumFailureLength] : failure;

    private CliInvocationEntry Entry(string command, CliInvocationOutcome outcome) => new(
        this.startedAt,
        command,
        outcome,
        (long)this.clock.GetElapsedTime(this.startedTimestamp).TotalMilliseconds)
    {
        Deployment = this.Deployment,
    };
}
