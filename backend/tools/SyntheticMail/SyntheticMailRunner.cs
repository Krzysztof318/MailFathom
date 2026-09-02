// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.SyntheticMail.Commands;

namespace MailFathom.SyntheticMail;

/// <summary>Runs one invocation, from an argument list to an exit code.</summary>
/// <remarks>
/// The entry point delegates here because top-level statements cannot be called, so a failure path written there could
/// never be exercised — and how a missing credential file turns into one line and an exit code is exactly the part
/// worth asserting.
/// </remarks>
internal static class SyntheticMailRunner
{
    /// <summary>Parses the arguments and runs the command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <param name="args">The argument list.</param>
    /// <param name="cancellationToken">Cancels the invocation.</param>
    /// <returns>The exit code the process reports.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> or <paramref name="args" /> is <see langword="null" />.</exception>
    internal static async Task<int> RunAsync(
        SyntheticMailContext context,
        string[] args,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);

        // The library's own handler prints a stack trace and swallows the failure before this method sees it. What a
        // developer should read for a credential file they have not written yet is one line saying what to write.
        var invocation = new InvocationConfiguration { EnableDefaultExceptionHandler = false };

        try
        {
            return await DeliverBatchCommand.Create(context).Parse(args).InvokeAsync(invocation, cancellationToken);
        }
        catch (SyntheticMailFailure failure)
        {
            context.Console.WriteError(failure.Message);

            return SyntheticMailExitCode.Failure;
        }
    }
}
