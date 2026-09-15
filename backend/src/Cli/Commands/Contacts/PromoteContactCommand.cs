// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Commands.Users;

namespace MailFathom.Cli.Commands.Contacts;

/// <summary>Writes one user's own copy of a contact one of their mail accounts collected.</summary>
/// <remarks>
/// The one crossing between the two books, and it copies rather than moves. A collected record is an address that
/// appeared in mail rather than a person somebody wrote down, and it belongs to the account it arrived on — so
/// promoting leaves it there for whoever else is assigned that account, and what this user gains is a record of their
/// own, under a new identity, which every other command here then works on and which hides the collected one from their
/// reads. Nothing turns an asserted contact back.
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
        var userOption = UserOptions.User();
        var identityOption = ContactOptions.Identity();

        Command command = new("promote", "Write a user's own copy of a contact one of their mail accounts collected.")
        {
            userOption,
            identityOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(userOption),
            result.GetValue(identityOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        Guid? requestedUser,
        Guid contactId,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        var user = await UserOptions.ResolveUserAsync(deployment, profile.Token, requestedUser, cancellationToken);
        var promoted = await deployment.PromoteContactAsync(profile.Token, contactId, user, cancellationToken);

        return ContactOutput.ReportOutcome(context, promoted, contactId, "Took on");
    }
}
