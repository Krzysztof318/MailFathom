// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Users;

namespace MailFathom.Cli.Commands.Users;

/// <summary>Replaces the label an administrator tells one user apart by.</summary>
/// <remarks>
/// <para>
/// A label is the operator's own text and the identity is not: no mail account, stored message, or job hangs on it, so
/// this changes what a roster reads like and nothing else. It asks for no confirmation for that reason, unlike the two
/// commands beside it that move a decision out of a file or destroy mail.
/// </para>
/// <para>
/// A label written here lasts, for every user this deployment serves. No configuration source names a user any more,
/// so no start puts a label back and there is no kind of user for whom this reports a change the deployment undoes.
/// </para>
/// </remarks>
internal static class RenameUserCommand
{
    /// <summary>Builds the <c>user rename</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var userOption = UserOptions.User();

        Option<string> displayNameOption = new("--display-name")
        {
            Description = "The label this user is told apart by from now on, unique across the deployment.",
            Required = true,
        };

        Command command = new("rename", "Replace the label one user is told apart by.")
        {
            userOption,
            displayNameOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(userOption),
            result.GetValue(displayNameOption) ?? string.Empty,
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        Guid? requestedUser,
        string displayName,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        var user = await UserOptions.ResolveUserAsync(
            deployment,
            profile.Token,
            requestedUser,
            cancellationToken);

        await deployment.RelabelUserAsync(
            profile.Token,
            user,
            new UserRelabelRequest(displayName),
            cancellationToken);

        context.Console.WriteLine($"User {user:D} is now labelled {displayName}.");

        return CliExitCode.Success;
    }
}
