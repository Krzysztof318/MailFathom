// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Organizations;

namespace MailFathom.Cli.Commands.Organizations;

/// <summary>Records an organization this deployment did not hold.</summary>
/// <remarks>
/// The identifier is the deployment's to mint, so this states two names and nothing else. What a short name may be is
/// the deployment's rule, which it states in its own refusal; restating it here would leave two rules to keep in
/// agreement.
/// </remarks>
internal static class AddOrganizationCommand
{
    /// <summary>Builds the <c>organization add</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var shortNameOption = OrganizationOptions.ShortName(
            "The short name its members sign in under, as SHORTNAME/username. Unique across the deployment.");

        Option<string> displayNameOption = new("--display-name")
        {
            Description = "The name an operator reads this organization by.",
            Required = true,
        };

        Command command = new("add", "Record an organization this deployment does not hold.")
        {
            displayNameOption,
            shortNameOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            new OrganizationProvisioningRequest(
                result.GetValue(displayNameOption) ?? string.Empty,
                result.GetValue(shortNameOption) ?? string.Empty),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        OrganizationProvisioningRequest request,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        var recorded = await deployment.ProvisionOrganizationAsync(profile.Token, request, cancellationToken);

        context.Console.WriteLine($"Recorded {request.DisplayName} as {recorded.OrganizationId:D}.");
        context.Console.WriteNotice(
            "Nobody belongs to it yet. Move a user into it with 'mfctl user set-organization'; their password "
            + "credentials then sign in as SHORTNAME/username.");

        return CliExitCode.Success;
    }
}
