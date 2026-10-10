// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Grants;

namespace MailFathom.Cli.Commands.Roles;

/// <summary>Replaces the name an operator reads one role by.</summary>
/// <remarks>Every assignment names the role by its identifier, so a rename changes what a listing reads like and nothing anybody holds.</remarks>
internal static class RenameRoleCommand
{
    /// <summary>Builds the <c>role rename</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var roleOption = RoleOptions.Role();
        var nameOption = RoleOptions.Name("The name this role is read by from now on.");

        Command command = new("rename", "Replace the name one role is read by.")
        {
            roleOption,
            nameOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(roleOption),
            result.GetValue(nameOption) ?? string.Empty,
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        Guid role,
        string name,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        await deployment.RenameRoleAsync(profile.Token, role, new GrantRecordNameRequest(name), cancellationToken);

        context.Console.WriteLine($"Role {role:D} is now named {name}.");

        return CliExitCode.Success;
    }
}
