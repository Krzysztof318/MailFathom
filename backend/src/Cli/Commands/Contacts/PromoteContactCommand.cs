// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;

namespace MailFathom.Cli.Commands.Contacts;

/// <summary>Takes on a contact the deployment collected from arriving mail.</summary>
/// <remarks>
/// The one act that changes an origin, and it runs one way. A collected record is an address that appeared in mail
/// rather than a person somebody wrote down, so it is not amended in place; promoting it is the owner saying this is
/// their record now, after which every other command here works on it. Nothing turns an asserted contact back.
/// </remarks>
internal static class PromoteContactCommand
{
    /// <summary>Builds the <c>contact promote</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var identityOption = ContactOptions.Identity();

        Command command = new("promote", "Take on a contact the deployment collected, so it becomes one you asserted.")
        {
            identityOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(identityOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        Guid contactId,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var promoted = await new AdminApiClient(transport, context.Console)
            .PromoteContactAsync(profile.Token, contactId, cancellationToken);

        return ContactOutput.ReportOutcome(context, promoted, contactId, "Took on");
    }
}
