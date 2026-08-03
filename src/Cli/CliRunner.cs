// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;

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
    /// A <see cref="CliFailure" /> is reported as one line rather than as a stack trace, because every failure of that
    /// kind is something the operator can act on. Anything else propagates, because a stack trace is the right answer
    /// to a defect.
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

        try
        {
            return await CliRootCommand.Create(context).Parse(args).InvokeAsync(invocation, cancellationToken);
        }
        catch (CliFailure failure)
        {
            context.Console.WriteError(failure.Message);

            return CliExitCode.Failure;
        }
    }
}
