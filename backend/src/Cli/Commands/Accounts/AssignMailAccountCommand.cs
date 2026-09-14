// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Accounts;
using MailFathom.Cli.Commands.Users;

namespace MailFathom.Cli.Commands.Accounts;

/// <summary>Assigns a mail account nobody holds to a user.</summary>
/// <remarks>An account is served to one user at a time, so the deployment refuses one another user already holds; the account is judged against the user's own accounts first, so one whose display name they already use is refused too.</remarks>
internal static class AssignMailAccountCommand
{
    /// <summary>Builds the <c>account assign</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var accountOption = MailAccountOptions.Account();
        var userOption = UserOptions.User();

        Command command = new("assign", "Assign a mail account nobody holds to a user.")
        {
            accountOption,
            userOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(accountOption),
            result.GetValue(userOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        Guid accountId,
        Guid? requestedUser,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        var user = await UserOptions.ResolveUserAsync(deployment, profile.Token, requestedUser, cancellationToken);

        var answer = await deployment.AssignMailAccountAsync(
            profile.Token,
            accountId,
            new MailAccountAssignmentRequest(user),
            cancellationToken);

        return MailAccountOutput.ReportWrite(context, answer);
    }
}
