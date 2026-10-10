// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;

namespace MailFathom.Cli.Commands.Groups;

/// <summary>Removes a group nothing is assigned to, and its memberships with it.</summary>
/// <remarks>
/// The deployment refuses to remove a group an assignment still names and says how many, so what this takes away is a
/// group granting nothing — but its memberships go with it and nothing puts them back, which is why it asks first.
/// </remarks>
internal static class RemoveGroupCommand
{
    /// <summary>Builds the <c>group remove</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var groupOption = GroupOptions.Group();
        var confirmedOption = CliOptions.Confirmed("removal");

        Command command = new("remove", "Remove a group nothing is assigned to, and its memberships with it.")
        {
            groupOption,
            confirmedOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(groupOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            result.GetValue(confirmedOption),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        Guid group,
        string? requestedDeployment,
        bool confirmedUpFront,
        CancellationToken cancellationToken)
    {
        if (!CliConfirmation.Agreed(
                context,
                confirmedUpFront,
                "There is nobody at the terminal to agree to this, and a removed group and its memberships cannot be put back. Pass --yes to remove without being asked.",
                $"Remove group {group:D} and every membership of it? [y/N] "))
        {
            context.Console.WriteError("Nothing was removed.");

            return CliExitCode.Failure;
        }

        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        await deployment.RemoveGroupAsync(profile.Token, group, cancellationToken);

        context.Console.WriteLine($"Removed group {group:D} and its memberships.");

        return CliExitCode.Success;
    }
}
