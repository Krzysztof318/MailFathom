// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;

namespace MailFathom.Cli.Commands.Owners;

/// <summary>Reads the people this deployment holds records for.</summary>
/// <remarks>
/// The listing every other owner-scoped command takes its identifiers out of, and the one place an operator sees the
/// two states that decide what to do next: whether an owner's mail accounts have been moved into their own record, and
/// whether the running deployment is serving them at all.
/// </remarks>
internal static class ListOwnersCommand
{
    /// <summary>Builds the <c>owner list</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();

        Command command = new("list", "Read the owners this deployment holds records for.")
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
        var deployment = new AdminApiClient(transport, context.Console);

        var roster = await deployment.ReadOwnersAsync(profile.Token, cancellationToken);

        if (roster.Owners is not { Count: > 0 } owners)
        {
            context.Console.WriteLine(
                "This deployment holds no owner records. One is written when it first composes its settings and "
                + "'owner add' records another, so an empty roster is a deployment that has not started successfully "
                + "yet, or one whose owners have all been removed.");

            return CliExitCode.Success;
        }

        OwnerOutput.WriteRoster(context.Console, owners);

        return CliExitCode.Success;
    }
}
