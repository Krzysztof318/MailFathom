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
/// A file that names a user names their label too, and a start puts that label back, so renaming a declared user
/// here lasts until the next restart and the declaration is where their label is actually changed. Which kind of user
/// this was handed is read from the roster rather than guessed, because nothing in the acceptance says it: the route
/// answers with no body, and a label written over a declaration is written exactly as any other is.
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

        // The roster rather than the write's own answer, which is an acceptance and nothing else. What it settles is
        // whether a configuration source declares this user, because a start rewrites a declared user's label from
        // the file — and reporting the new label without saying so would be reporting a change that is undone.
        var roster = await deployment.ReadUsersAsync(profile.Token, cancellationToken);
        var declared = roster.Users?.FirstOrDefault(candidate => candidate.Id == user)?.DeclaredInConfiguration
            ?? false;

        await deployment.RelabelUserAsync(
            profile.Token,
            user,
            new UserRelabelRequest(displayName),
            cancellationToken);

        context.Console.WriteLine($"User {user:D} is now labelled {displayName}.");

        if (declared)
        {
            context.Console.WriteNotice(
                "A configuration source declares this user, and a start reads their label from it, so this one lasts "
                + "until the deployment is restarted. Change the label in the declaration to keep it.");
        }

        return CliExitCode.Success;
    }
}
