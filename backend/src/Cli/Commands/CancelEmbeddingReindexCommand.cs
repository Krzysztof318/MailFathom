// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;

namespace MailFathom.Cli.Commands;

/// <summary>Stops the reindex a deployment has under way, leaving the generation that is serving where it is.</summary>
/// <remarks>
/// <para>
/// It exists because a reindex is a decision an operator can regret while it is still running — the wrong model, a bill
/// growing faster than expected — and the honest answer to that is to stop rather than to wait for the switch and pay
/// for a second one. Nothing about search results changes: the generation being abandoned was never read.
/// </para>
/// <para>
/// It is also what makes a refused activation actionable, since one reindex runs at a time and a different one under way
/// is what refuses the next.
/// </para>
/// </remarks>
internal static class CancelEmbeddingReindexCommand
{
    /// <summary>Builds the <c>embedding cancel-reindex</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();

        Command command = new(
            "cancel-reindex",
            "Stop the reindex under way, abandoning the generation it was filling.")
        {
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var cancellation = await new AdminApiClient(transport, context.Console)
            .CancelEmbeddingReindexAsync(profile.Token, cancellationToken);

        context.Console.WriteLine(cancellation.Describe());

        return CliExitCode.Success;
    }
}
