// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;

namespace MailFathom.Cli.Commands.Groups;

/// <summary>Makes one user a member of one group, which gives them every role the group is assigned.</summary>
/// <remarks>Asking again for a member who already belongs is accepted and changes nothing, so a script can state a membership rather than check for it first.</remarks>
internal static class AddGroupMemberCommand
{
    /// <summary>Builds the <c>group add-member</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var groupOption = GroupOptions.Group();
        var userOption = GroupOptions.Member();

        Command command = new("add-member", "Make one user a member of one group, giving them every role it holds.")
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

        await deployment.AddGroupMemberAsync(profile.Token, group, user, cancellationToken);

        context.Console.WriteLine($"User {user:D} belongs to group {group:D}.");
        context.Console.WriteNotice(
            "They hold every role the group is assigned from their next request. Read what that gives them with "
            + "'mfctl user permissions'.");

        return CliExitCode.Success;
    }
}
