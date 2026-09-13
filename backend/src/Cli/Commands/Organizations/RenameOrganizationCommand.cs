// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Organizations;

namespace MailFathom.Cli.Commands.Organizations;

/// <summary>Replaces the name an operator reads one organization by.</summary>
/// <remarks>
/// The display name is nobody's login, so this changes what a listing reads like and nothing else, and asks for no
/// confirmation for that reason. The short name is the half of a login that changes how members sign in, which is why
/// it is <c>organization set-short-name</c> rather than an option here.
/// </remarks>
internal static class RenameOrganizationCommand
{
    /// <summary>Builds the <c>organization rename</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var organizationOption = OrganizationOptions.Organization();

        Option<string> displayNameOption = new("--display-name")
        {
            Description = "The name this organization is read by from now on.",
            Required = true,
        };

        Command command = new("rename", "Replace the name one organization is read by.")
        {
            organizationOption,
            displayNameOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(organizationOption),
            result.GetValue(displayNameOption) ?? string.Empty,
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        Guid organization,
        string displayName,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        await deployment.RenameOrganizationAsync(
            profile.Token,
            organization,
            new OrganizationDisplayNameRequest(displayName),
            cancellationToken);

        context.Console.WriteLine($"Organization {organization:D} is now named {displayName}.");

        return CliExitCode.Success;
    }
}
