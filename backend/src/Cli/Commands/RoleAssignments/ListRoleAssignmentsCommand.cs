// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Grants;
using MailFathom.Cli.Output;

namespace MailFathom.Cli.Commands.RoleAssignments;

/// <summary>Reads the role assignments the caller's scope covers.</summary>
/// <remarks>The listing <c>assignment revoke</c> takes its identifier out of.</remarks>
internal static class ListRoleAssignmentsCommand
{
    /// <summary>Builds the <c>assignment list</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();

        Command command = new("list", "Read the roles given to users and groups within your scope.")
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

        var listing = await deployment.ReadRoleAssignmentsAsync(profile.Token, cancellationToken);

        if (listing.Assignments is not { Count: > 0 } assignments)
        {
            context.Console.WriteLine(
                "This deployment holds no role assignments within your scope. Give a role with 'assignment add'.");

            return CliExitCode.Success;
        }

        context.Console.Write(Draw(assignments));

        return CliExitCode.Success;
    }

    private static CliTable Draw(IReadOnlyList<RoleAssignmentEntry> assignments)
    {
        CliTable listing = new("Assignment", "Role", "Given to", "Scope", "Assigned");

        foreach (var assignment in assignments)
        {
            listing.AddRow(
                $"{assignment.Id:D}",
                $"{assignment.RoleId:D}",
                assignment.Principal?.Describe() ?? "unreported",
                assignment.Scope?.Describe() ?? "unreported",
                $"{assignment.AssignedAt:u}");
        }

        return listing;
    }
}
