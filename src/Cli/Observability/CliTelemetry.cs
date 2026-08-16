// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics;
using MailFathom.Common.Observability;

namespace MailFathom.Cli.Observability;

/// <summary>Opens the span one administrative command runs inside, so the deployment's own spans continue one trace.</summary>
/// <remarks>
/// <para>
/// The command holds no exporter and no telemetry configuration, and this does not give it one. What it gives it is a
/// trace context: a span open for the duration of an invocation is what makes <c>HttpClient</c> send <c>traceparent</c>
/// with every request the command issues, so the administrative endpoint continues this trace instead of starting one
/// of its own per call. A command that signs in, performs an action, and reads a status back is then one trace on the
/// deployment's collector rather than three unrelated ones, and which command caused them is answerable at all.
/// </para>
/// <para>
/// The span itself is exported nowhere, because nothing here is configured to export anything. That is a deliberate
/// asymmetry rather than a gap: the operator's own workstation is not part of the deployment's trust boundary, and the
/// deployment already records what it did with the request. A collector therefore sees the server spans under a parent
/// it never received, which is what a trace begun outside the collected system always looks like.
/// </para>
/// <para>
/// Nothing about the invocation reaches the span beyond the command's own name and the exit code. The arguments are
/// where a deployment address, an account alias, a folder alias, a message identity, and — for a sign-in — a credential
/// would be, so none of them is read: what is published is the command MailFathom declares and what it returned.
/// </para>
/// </remarks>
internal static class CliTelemetry
{
    /// <summary>The name one invocation of the administration command opens its span under.</summary>
    internal const string CommandSpanName = "run_mfctl_command";

    /// <summary>The command that ran, as the space-separated path of MailFathom's own declared command names.</summary>
    internal const string CommandTagName = "mailfathom.cli.command";

    /// <summary>The exit code the invocation reported, which is the whole of what it says about how it ended.</summary>
    internal const string ExitCodeTagName = "mailfathom.cli.exit_code";

    /// <summary>Samples the activity source for as long as the returned listener is held.</summary>
    /// <returns>The listener, which the invocation disposes when it ends.</returns>
    /// <remarks>
    /// <see cref="ActivitySource.StartActivity(string, ActivityKind)" /> returns <see langword="null" /> while nothing
    /// is listening, and a null activity propagates no header — so without this the command would start no span and the
    /// deployment would go on receiving requests with no trace context. It is taken explicitly by the invocation rather
    /// than registered from a static initializer, so that what turns the command's spans on is a statement a reader
    /// meets rather than a side effect of the first call.
    /// </remarks>
    internal static ActivityListener ListenForSpans()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => StringComparer.Ordinal.Equals(source.Name, Telemetry.Name),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = static (ref ActivityCreationOptions<string> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
        };

        ActivitySource.AddActivityListener(listener);

        return listener;
    }

    /// <summary>Opens the span the invocation runs inside, and returns it for the caller to end.</summary>
    /// <param name="command">The path of declared command names that were invoked, which is never a caller's own text.</param>
    /// <returns>The activity, or <see langword="null" /> where nothing sampled it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="command" /> is <see langword="null" />.</exception>
    internal static Activity? BeginCommand(string command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var activity = Telemetry.ActivitySource.StartActivity(CommandSpanName, ActivityKind.Client);
        activity?.SetTag(CommandTagName, command);

        return activity;
    }

    /// <summary>Records the exit code the invocation reported and whether it reads as a failure.</summary>
    /// <param name="activity">The span the invocation ran inside, which is <see langword="null" /> where nothing sampled it.</param>
    /// <param name="exitCode">The code the process is about to report.</param>
    internal static void EndCommand(Activity? activity, int exitCode)
    {
        activity?.SetTag(ExitCodeTagName, exitCode);
        activity?.SetStatus(exitCode == CliExitCode.Success ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
    }
}
