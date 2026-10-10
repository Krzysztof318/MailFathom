// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using System.Globalization;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Grants;
using MailFathom.Cli.Output;

namespace MailFathom.Cli.Commands.Groups;

/// <summary>Reads the groups the caller's scope covers.</summary>
/// <remarks>The listing every other group and assignment command takes a group's identifier out of.</remarks>
internal static class ListGroupsCommand
{
    /// <summary>Builds the <c>group list</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();

        Command command = new("list", "Read the groups this deployment holds within your scope.")
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

        var listing = await deployment.ReadGroupsAsync(profile.Token, cancellationToken);

        if (listing.Groups is not { Count: > 0 } groups)
        {
            context.Console.WriteLine("This deployment holds no groups within your scope. Record one with 'group add'.");

            return CliExitCode.Success;
        }

        context.Console.Write(Draw([.. groups.OrderBy(group => group.Name, StringComparer.Ordinal)]));

        return CliExitCode.Success;
    }

    private static CliTable Draw(IReadOnlyList<GroupEntry> groups)
    {
        CliTable listing = new("Group", "Name", "Organization", "Members", "Recorded");

        foreach (var group in groups)
        {
            listing.AddRow(
                $"{group.Id:D}",
                ConsoleSafeText.Sanitize(group.Name) ?? "unreported",
                group.OrganizationId is { } organization ? $"{organization:D}" : "none",
                group.Members.ToString(CultureInfo.InvariantCulture),
                $"{group.CreatedAt:u}");
        }

        return listing;
    }
}
