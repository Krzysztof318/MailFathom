// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Organizations;

namespace MailFathom.Cli.Commands.Organizations;

/// <summary>Replaces the short name one organization's members sign in under.</summary>
/// <remarks>
/// The one organization act that changes what somebody types: every member's password works under the new short name
/// from the moment the deployment answers, and under the old one no longer. Nothing is re-provisioned, so the operator
/// is told that in the same breath rather than left to hear it from the members.
/// </remarks>
internal static class SetOrganizationShortNameCommand
{
    /// <summary>Builds the <c>organization set-short-name</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var organizationOption = OrganizationOptions.Organization();
        var shortNameOption = OrganizationOptions.ShortName(
            "The short name every member signs in under from now on, unique across the deployment.");

        Command command = new("set-short-name", "Replace the short name one organization's members sign in under.")
        {
            organizationOption,
            shortNameOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(organizationOption),
            result.GetValue(shortNameOption) ?? string.Empty,
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        Guid organization,
        string shortName,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        await deployment.ChangeOrganizationShortNameAsync(
            profile.Token,
            organization,
            new OrganizationShortNameRequest(shortName),
            cancellationToken);

        context.Console.WriteLine($"Organization {organization:D} now signs in under {shortName}.");
        context.Console.WriteNotice(
            "Every member's password credentials sign in under the new short name from now on, and under the previous "
            + "one no longer. Read a member's login with 'mfctl credential list'.");

        return CliExitCode.Success;
    }
}
