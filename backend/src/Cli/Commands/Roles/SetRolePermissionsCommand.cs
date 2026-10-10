// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Grants;

namespace MailFathom.Cli.Commands.Roles;

/// <summary>Replaces the whole list of permissions one role grants.</summary>
/// <remarks>
/// The list is replaced rather than edited, so what an invocation names is what the role grants afterwards and nothing
/// it listed before survives by being left out. Everybody the role is assigned to holds the new list from their next
/// request, which is why the empty list is a flag of its own rather than what an invocation naming nothing means.
/// </remarks>
internal static class SetRolePermissionsCommand
{
    /// <summary>Builds the <c>role set-permissions</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var roleOption = RoleOptions.Role();
        var permissionOption = RoleOptions.Permission();
        var noPermissionsOption = RoleOptions.NoPermissions();

        Command command = new("set-permissions", "Replace the whole list of permissions one role grants.")
        {
            roleOption,
            permissionOption,
            noPermissionsOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(roleOption),
            new RolePermissionsRequest(
                RoleOptions.ResolvePermissions(result.GetValue(permissionOption), result.GetValue(noPermissionsOption))),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        Guid role,
        RolePermissionsRequest request,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        await deployment.ReplaceRolePermissionsAsync(profile.Token, role, request, cancellationToken);

        context.Console.WriteLine(
            request.Permissions.Count == 0
                ? $"Role {role:D} now grants nothing."
                : $"Role {role:D} now grants {string.Join(", ", request.Permissions)}.");
        context.Console.WriteNotice(
            "Everybody it is assigned to holds the new list from their next request. Read one user's grant with "
            + "'mfctl user permissions'.");

        return CliExitCode.Success;
    }
}
