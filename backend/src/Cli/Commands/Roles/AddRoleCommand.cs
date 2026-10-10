// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Grants;

namespace MailFathom.Cli.Commands.Roles;

/// <summary>Defines a role this deployment did not define.</summary>
/// <remarks>
/// A role grants nothing to anybody until an assignment gives it, so defining one changes what a listing reads like and
/// nothing else. The identifier is the deployment's to mint.
/// </remarks>
internal static class AddRoleCommand
{
    /// <summary>Builds the <c>role add</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var nameOption = RoleOptions.Name("The name an operator reads this role by. Unique across every role and group.");
        var permissionOption = RoleOptions.Permission();
        var noPermissionsOption = RoleOptions.NoPermissions();

        Command command = new("add", "Define a role and the permissions it grants.")
        {
            nameOption,
            permissionOption,
            noPermissionsOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            new RoleProvisioningRequest(
                result.GetValue(nameOption) ?? string.Empty,
                RoleOptions.ResolvePermissions(result.GetValue(permissionOption), result.GetValue(noPermissionsOption))),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        RoleProvisioningRequest request,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        var recorded = await deployment.ProvisionRoleAsync(profile.Token, request, cancellationToken);

        context.Console.WriteLine($"Defined role {request.Name} as {recorded.Id:D}.");
        context.Console.WriteNotice("Nobody holds it yet. Give it to a user or a group with 'mfctl assignment add'.");

        return CliExitCode.Success;
    }
}
