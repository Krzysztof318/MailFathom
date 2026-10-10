// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Output;

namespace MailFathom.Cli.Commands.Groups;

/// <summary>Reads who belongs to one group.</summary>
internal static class ListGroupMembersCommand
{
    /// <summary>Builds the <c>group members</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var groupOption = GroupOptions.Group();

        Command command = new("members", "Read who belongs to one group.")
        {
            groupOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(groupOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        Guid group,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        var listing = await deployment.ReadGroupMembersAsync(profile.Token, group, cancellationToken);

        if (listing.Members is not { Count: > 0 } members)
        {
            context.Console.WriteLine($"Nobody belongs to group {group:D}. Add a member with 'group add-member'.");

            return CliExitCode.Success;
        }

        CliTable table = new("User");

        foreach (var member in members)
        {
            table.AddRow($"{member:D}");
        }

        context.Console.Write(table);

        return CliExitCode.Success;
    }
}
