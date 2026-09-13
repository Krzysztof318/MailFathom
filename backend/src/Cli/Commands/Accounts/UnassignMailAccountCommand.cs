// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Accounts;
using MailFathom.Cli.Commands.Users;

namespace MailFathom.Cli.Commands.Accounts;

/// <summary>Ends one user's assignment to a mail account.</summary>
/// <remarks>
/// Confirmed, because ending the last assignment erases the account and every message this deployment stored for it,
/// and ending any other takes that user's copy of the account's mail with it. Nothing undoes either.
/// </remarks>
internal static class UnassignMailAccountCommand
{
    /// <summary>Builds the <c>account unassign</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var accountOption = MailAccountOptions.Account();
        var userOption = UserOptions.User();
        var confirmationOption = CliOptions.Confirmed("erasure");

        Command command = new("unassign", "End one user's assignment to a mail account, erasing the account when it was their last.")
        {
            accountOption,
            userOption,
            confirmationOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(accountOption),
            result.GetValue(userOption),
            result.GetValue(confirmationOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        Guid accountId,
        Guid? requestedUser,
        bool confirmedUpFront,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        var user = await UserOptions.ResolveUserAsync(deployment, profile.Token, requestedUser, cancellationToken);

        if (!CliConfirmation.Agreed(
            context,
            confirmedUpFront,
            $"Ending the assignment of mail account {accountId:D} erases that user's copy of its mail, and the account itself when nobody else is assigned it, and there is nobody at the terminal to confirm it. Re-run with --yes to state the agreement in the command.",
            $"End user {user:D}'s assignment to mail account {accountId:D}, erasing its mail for them? [y/N] "))
        {
            context.Console.WriteError("Nothing was erased.");

            return CliExitCode.Failure;
        }

        var unassignment = await deployment.UnassignMailAccountAsync(
            profile.Token,
            accountId,
            new MailAccountAssignmentRequest(user),
            cancellationToken);

        context.Console.WriteLine(unassignment switch
        {
            { Unassigned: false } => $"User {user:D} is not assigned mail account {accountId:D}, so nothing was changed.",
            { AccountErased: true } => $"Ended the last assignment of mail account {accountId:D}, and erased the account and every message this deployment held for it.",
            _ => $"Ended user {user:D}'s assignment to mail account {accountId:D}.",
        });

        return CliExitCode.Success;
    }
}
