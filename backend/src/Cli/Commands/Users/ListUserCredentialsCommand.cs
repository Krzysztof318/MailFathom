// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;

namespace MailFathom.Cli.Commands.Users;

/// <summary>Reads the credentials one user's clients present.</summary>
/// <remarks>
/// What an administrator answers "who can reach this mailbox" from, and the listing every other command here takes its
/// identifiers out of. It reports which credentials exist, how each is presented, what each may do, whether each still
/// works, and how old its material is — each a fact about the record rather than about the secret, so the listing is
/// safe to print, capture, and keep. The one value it withholds is a key's digest, which verifies a presented key.
/// </remarks>
internal static class ListUserCredentialsCommand
{
    /// <summary>Builds the <c>credential list</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var userOption = UserOptions.User();

        Command command = new("list", "Read the credentials one user's clients present.")
        {
            userOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(userOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        Guid? requestedUser,
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

        var listing = await deployment.ReadUserCredentialsAsync(profile.Token, user, cancellationToken);

        if (listing.Credentials is not { Count: > 0 } credentials)
        {
            context.Console.WriteLine(
                $"User {user:D} holds no credentials. Nothing provisions one on its own, so there are none until "
                + "'credential create' writes one.");

            return CliExitCode.Success;
        }

        UserCredentialOutput.WriteListing(context.Console, credentials);

        return CliExitCode.Success;
    }
}
