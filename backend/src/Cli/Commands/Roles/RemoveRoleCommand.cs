// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;

namespace MailFathom.Cli.Commands.Roles;

/// <summary>Removes a role nobody is assigned.</summary>
/// <remarks>
/// The deployment refuses to remove a role an assignment still gives and names how many, so what this can take away is
/// a definition nobody holds. It still asks first, as every command removing a record here does, and the refusal is
/// repeated as the deployment wrote it.
/// </remarks>
internal static class RemoveRoleCommand
{
    /// <summary>Builds the <c>role remove</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var roleOption = RoleOptions.Role();
        var confirmedOption = CliOptions.Confirmed("removal");

        Command command = new("remove", "Remove a role nobody is assigned.")
        {
            roleOption,
            confirmedOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(roleOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            result.GetValue(confirmedOption),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        Guid role,
        string? requestedDeployment,
        bool confirmedUpFront,
        CancellationToken cancellationToken)
    {
        if (!CliConfirmation.Agreed(
                context,
                confirmedUpFront,
                "There is nobody at the terminal to agree to this, and a removed role cannot be put back. Pass --yes to remove without being asked.",
                $"Remove role {role:D}? [y/N] "))
        {
            context.Console.WriteError("Nothing was removed.");

            return CliExitCode.Failure;
        }

        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        await deployment.RemoveRoleAsync(profile.Token, role, cancellationToken);

        context.Console.WriteLine($"Removed role {role:D}.");

        return CliExitCode.Success;
    }
}
