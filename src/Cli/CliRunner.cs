// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using System.CommandLine.Parsing;
using MailFathom.Cli.Commands;
using MailFathom.Cli.Diagnostics;

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
    /// However the invocation ends, it is recorded in the local log, which is why the append is in a <c>finally</c>
    /// rather than beside each <c>return</c>: an invocation that faulted or was cancelled is the one an operator most
    /// wants a line for afterwards, and it reaches neither of the two paths that report an exit code.
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
        var command = CommandPathOf(parseResult);

        CliInvocationEntry? entry = null;

        try
        {
            var exitCode = await parseResult.InvokeAsync(invocation, cancellationToken);

            entry = context.Invocation.Ended(command, exitCode, RefusalOf(parseResult, exitCode));

            return exitCode;
        }
        catch (CliFailure failure)
        {
            context.Console.WriteError(failure.Message);

            entry = context.Invocation.Ended(command, CliExitCode.Failure, failure.Message);

            return CliExitCode.Failure;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            entry = context.Invocation.Cancelled(command);

            throw;
        }
        catch (Exception fault)
        {
            entry = context.Invocation.Faulted(command, fault);

            throw;
        }
        finally
        {
            // Every path above closes the record, so the guard covers only a catch block that itself raised — writing
            // the failure line to a terminal that has gone away. Nothing left to say about that invocation is true.
            if (entry is { } closed)
            {
                Record(context, parseResult, closed);
            }
        }
    }

    /// <summary>Appends the invocation to the local log, unless this invocation asked for none.</summary>
    /// <remarks>
    /// A log that could not be written is said once and changes nothing else. The command's job is the command, so a
    /// read-only home directory or a full disk must not turn an invocation that did what it was asked into one that
    /// reports a failure — and an operator who is never told would go on believing there is a record to go back to.
    /// </remarks>
    private static void Record(CliContext context, ParseResult parseResult, CliInvocationEntry entry)
    {
        if (context.Log is not { } log
            || !CliOptions.RecordsInvocation(parseResult, context.Variable(CliOptions.LogVariable)))
        {
            return;
        }

        if (log.TryAppend(entry))
        {
            return;
        }

        try
        {
            context.Console.WriteError($"This invocation could not be recorded in {log.Location}.");
        }
        catch (IOException)
        {
            // The terminal this would have gone to has gone away. This method runs in the runner's finally, so raising
            // here would replace the exit code or the exception the invocation was already reporting with a complaint
            // about a log — which is the failure the whole seam is written to make impossible.
        }
    }

    /// <summary>Reads back why the parser refused an invocation, for the record of one that raised nothing.</summary>
    /// <remarks>
    /// A parse error is reported by the library and returns a code rather than raising, so it reaches neither of the
    /// paths that carry a message. Without this a refused invocation would be recorded as a failure with nothing said
    /// about it, which is the one shape the log's own table promises never to have.
    /// </remarks>
    private static string? RefusalOf(ParseResult parseResult, int exitCode) =>
        exitCode != CliExitCode.Success && parseResult.Errors.Count > 0
            ? string.Join(' ', parseResult.Errors.Select(error => error.Message))
            : null;

    /// <summary>Names the command that was invoked, as the path of declared names from the root down.</summary>
    /// <remarks>
    /// Read from the parse result rather than from the argument list, because an argument list is where a deployment
    /// address, an account alias, a folder alias, a message identity and — for a sign-in — a credential are, and none
    /// of that may reach a file. An unparsable invocation resolves to the root alone, which is the honest answer: no
    /// subcommand ran.
    /// </remarks>
    private static string CommandPathOf(ParseResult parseResult) =>
        string.Join(
            ' ',
            [CliRootCommand.CommandName, .. SubcommandNamesUpFrom(parseResult.CommandResult).Reverse()]);

    /// <summary>Walks from the invoked command up to the root, naming each subcommand on the way.</summary>
    /// <remarks>
    /// The root itself is left out and supplied as the declared constant instead, because the parser names the root
    /// after the running executable — which is the published binary's name on an operator's machine and the test host's
    /// name under a test, and only one of those is a name this repository chose.
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
