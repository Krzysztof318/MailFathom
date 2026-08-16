// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using System.CommandLine.Parsing;
using MailFathom.Cli.Commands;
using MailFathom.Cli.Observability;

namespace MailFathom.Cli;

/// <summary>Runs one invocation of the command, from an argument list to an exit code.</summary>
/// <remarks>
/// The entry point delegates to this rather than doing it itself, because top-level statements cannot be called: a
/// failure path written there could never be exercised, and how a refused credential turns into an exit code and one
/// line on standard error is exactly the part worth asserting.
/// </remarks>
internal static class CliRunner
{
    /// <summary>Parses the arguments and runs whichever command they name.</summary>
    /// <param name="context">What the commands need from their surroundings.</param>
    /// <param name="args">The argument list.</param>
    /// <param name="cancellationToken">Cancels the invocation.</param>
    /// <returns>The exit code the process reports.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> or <paramref name="args" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// A <see cref="CliFailure" /> is reported as one line rather than as a stack trace, because every failure of that
    /// kind is something the operator can act on. Anything else propagates, because a stack trace is the right answer
    /// to a defect.
    /// </para>
    /// <para>
    /// The arguments are parsed before the span is opened, so the span is named after the command MailFathom declares
    /// rather than after whatever the operator typed. Every request the invocation issues is then made inside that
    /// span, which is what carries the trace context to the deployment;
    /// <see cref="CliTelemetry" /> holds what that buys and what it deliberately does not.
    /// </para>
    /// </remarks>
    internal static async Task<int> RunAsync(
        CliContext context,
        string[] args,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);

        // The library's own exception handler is turned off, because it prints a stack trace and swallows the failure
        // before this method sees it. What an operator should read for a refused credential is one line, and deciding
        // that is this method's job rather than the parser's.
        var invocation = new InvocationConfiguration { EnableDefaultExceptionHandler = false };
        var parseResult = CliRootCommand.Create(context).Parse(args);

        // Both are held for the length of the invocation: the listener is what makes the span exist at all, and the
        // span is what makes every request the command issues carry the trace context to the deployment.
        using var listening = CliTelemetry.ListenForSpans();
        using var command = CliTelemetry.BeginCommand(CommandPathOf(parseResult));

        var exitCode = await InvokeAsync(context, parseResult, invocation, cancellationToken);

        CliTelemetry.EndCommand(command, exitCode);

        return exitCode;
    }

    /// <summary>Runs the parsed invocation and turns an actionable failure into one line and an exit code.</summary>
    private static async Task<int> InvokeAsync(
        CliContext context,
        ParseResult parseResult,
        InvocationConfiguration invocation,
        CancellationToken cancellationToken)
    {
        try
        {
            return await parseResult.InvokeAsync(invocation, cancellationToken);
        }
        catch (CliFailure failure)
        {
            context.Console.WriteError(failure.Message);

            return CliExitCode.Failure;
        }
    }

    /// <summary>Names the command that was invoked, as the path of declared names from the root down.</summary>
    /// <remarks>
    /// Read from the parse result rather than from the argument list, so what the span carries is a name this command
    /// declares. An unparsable invocation resolves to the root alone, which is the honest answer: no subcommand ran.
    /// </remarks>
    private static string CommandPathOf(ParseResult parseResult) =>
        string.Join(
            ' ',
            [CliRootCommand.CommandName, .. SubcommandNamesUpFrom(parseResult.CommandResult).Reverse()]);

    /// <summary>Walks from the invoked command up to the root, naming each subcommand on the way.</summary>
    /// <remarks>
    /// The root itself is left out and supplied as the declared constant instead, because the parser names the root
    /// after the running executable — which is the published binary's name in a deployment and the test host's name
    /// under a test, and only one of those is a name this repository chose.
    /// </remarks>
    private static IEnumerable<string> SubcommandNamesUpFrom(SymbolResult? result)
    {
        for (var current = result; current is not null; current = current.Parent)
        {
            if (current is CommandResult { Parent: not null } command)
            {
                yield return command.Command.Name;
            }
        }
    }
}
