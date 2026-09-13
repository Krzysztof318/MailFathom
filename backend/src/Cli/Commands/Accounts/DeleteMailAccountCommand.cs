// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;

namespace MailFathom.Cli.Commands.Accounts;

/// <summary>Erases one mail account, every assignment to it, and every message this deployment stored for it.</summary>
/// <remarks>Confirmed, and the confirmation names the account by its display name and address rather than only the identifier: an identifier copied out of the wrong listing looks the same either way.</remarks>
internal static class DeleteMailAccountCommand
{
    /// <summary>Builds the <c>account delete</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var accountOption = MailAccountOptions.Account();
        var confirmationOption = CliOptions.Confirmed("erasure");

        Command command = new("delete", "Erase one mail account and every message this deployment holds for it.")
        {
            accountOption,
            confirmationOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(accountOption),
            result.GetValue(confirmationOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        Guid accountId,
        bool confirmedUpFront,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        var account = await deployment.ReadMailAccountAsync(profile.Token, accountId, cancellationToken);

        MailAccountOutput.WriteHeading(context.Console, account);
        context.Console.WriteNotice(
            "Erasing this account takes every assignment to it and every message, folder, attachment, and index this "
            + "deployment holds for it. Nothing here undoes it.");

        if (!CliConfirmation.Agreed(
            context,
            confirmedUpFront,
            $"Erasing mail account {accountId:D} destroys every message this deployment holds for it, and there is nobody at the terminal to confirm it. Re-run with --yes to state the agreement in the command.",
            $"Erase mail account {accountId:D} and everything this deployment holds for it? [y/N] "))
        {
            context.Console.WriteError("Nothing was erased.");

            return CliExitCode.Failure;
        }

        var erasure = await deployment.EraseMailAccountAsync(profile.Token, accountId, cancellationToken);

        context.Console.WriteLine(erasure.Erased
            ? $"Erased mail account {accountId:D}."
            : $"This deployment holds no mail account {accountId:D}, so nothing was erased. Somebody else may have removed it first.");

        return CliExitCode.Success;
    }
}
