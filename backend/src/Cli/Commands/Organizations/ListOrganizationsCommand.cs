// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using System.Globalization;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Organizations;
using MailFathom.Cli.Output;

namespace MailFathom.Cli.Commands.Organizations;

/// <summary>Reads the organizations this deployment holds.</summary>
/// <remarks>
/// The listing every other organization command takes its identifiers out of, and the one place an operator reads the
/// short name a member's login begins with and whether an organization can be removed yet.
/// </remarks>
internal static class ListOrganizationsCommand
{
    /// <summary>Builds the <c>organization list</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();

        Command command = new("list", "Read the organizations this deployment holds.")
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

        var listing = await deployment.ReadOrganizationsAsync(profile.Token, cancellationToken);

        if (listing.Organizations is not { Count: > 0 } organizations)
        {
            context.Console.WriteLine(
                "This deployment holds no organizations. Record one with 'organization add', then move a user into it "
                + "with 'user set-organization'.");

            return CliExitCode.Success;
        }

        context.Console.Write(Draw(organizations));

        return CliExitCode.Success;
    }

    private static CliTable Draw(IReadOnlyList<OrganizationEntry> organizations)
    {
        CliTable listing = new("Organization", "Short name", "Display name", "Members", "Recorded");

        foreach (var organization in organizations)
        {
            listing.AddRow(
                $"{organization.Id:D}",
                organization.ShortName ?? "unreported",
                organization.DisplayName ?? "unreported",
                organization.Members.ToString(CultureInfo.InvariantCulture),
                $"{organization.CreatedAt:u}");
        }

        return listing;
    }
}
