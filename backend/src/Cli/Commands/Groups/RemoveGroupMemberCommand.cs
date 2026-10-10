// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;

namespace MailFathom.Cli.Commands.Groups;

/// <summary>Ends one user's membership of one group, which takes away every role they held through it.</summary>
/// <remarks>Asking for a user who does not belong is accepted and changes nothing, and the deployment refuses the one membership whose end would leave nobody administering it.</remarks>
internal static class RemoveGroupMemberCommand
{
    /// <summary>Builds the <c>group remove-member</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var groupOption = GroupOptions.Group();
        var userOption = GroupOptions.Member();

        Command command = new("remove-member", "End one user's membership of one group, taking away every role it gave them.")
        {
            groupOption,
            userOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(groupOption),
            result.GetValue(userOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        Guid group,
        Guid user,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        await deployment.RemoveGroupMemberAsync(profile.Token, group, user, cancellationToken);

        context.Console.WriteLine($"User {user:D} no longer belongs to group {group:D}.");

        return CliExitCode.Success;
    }
}
