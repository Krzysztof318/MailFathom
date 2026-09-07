// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;

namespace MailFathom.Cli.Commands.Users;

/// <summary>Erases one user and everything this deployment recorded for them.</summary>
/// <remarks>
/// <para>
/// The one command here that destroys mail. It takes the user's record, their mail accounts, and every message,
/// folder, attachment, and derived index hanging off them, because that is what erasing a person from a system that
/// holds their correspondence means — a row left behind would be exactly the copy an erasure was asked to end.
/// </para>
/// <para>
/// It is confirmed for that reason, and the confirmation names the person rather than only the identifier: an
/// identifier copied out of the wrong listing looks the same either way, and the label is what an operator recognizes.
/// Nothing undoes it and no backup this tool can reach holds what it removed.
/// </para>
/// </remarks>
internal static class RemoveUserCommand
{
    /// <summary>Builds the <c>user remove</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var userOption = UserOptions.User();
        var confirmationOption = CliOptions.Confirmed("erasure");

        Command command = new("remove", "Erase one user and every message this deployment holds for them.")
        {
            userOption,
            confirmationOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(userOption),
            result.GetValue(confirmationOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        Guid? requestedUser,
        bool confirmedUpFront,
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

        // The roster rather than the identifier alone, so the confirmation names the person about to be erased. It is
        // read whether or not the invocation supplied a user, because the case worth guarding is exactly the one
        // where an operator typed an identifier and believes it names somebody else.
        var roster = await deployment.ReadUsersAsync(profile.Token, cancellationToken);
        var held = roster.Users?.FirstOrDefault(candidate => candidate.Id == user);

        if (held is null)
        {
            context.Console.WriteLine($"This deployment holds no user {user:D}, so there is nothing to erase.");

            return CliExitCode.Success;
        }

        context.Console.WriteNotice(
            $"Erasing {held.Describe()} takes their record, their mail accounts, and every message, folder, attachment, "
            + "and index this deployment holds for them. Nothing here undoes it.");

        if (!CliConfirmation.Agreed(
            context,
            confirmedUpFront,
            $"Erasing {held.Describe()} destroys every message this deployment holds for them, and there is nobody at the terminal to confirm it. Re-run with --yes to state the agreement in the command.",
            $"Erase {held.Describe()} and everything this deployment holds for them? [y/N] "))
        {
            // Reported on standard error and with a failing code, which is what every other command does when it did
            // not do what it was asked: a wrapper reading exit 0 could not tell a declined erasure from one that ran.
            context.Console.WriteError("Nothing was erased.");

            return CliExitCode.Failure;
        }

        var erasure = await deployment.EraseUserAsync(profile.Token, user, cancellationToken);

        if (!erasure.Erased)
        {
            context.Console.WriteLine(
                $"This deployment holds no user {user:D}, so nothing was erased. Somebody else may have removed them first.");

            return CliExitCode.Success;
        }

        context.Console.WriteLine($"Erased {held.Describe()}.");

        if (erasure.WasServed)
        {
            context.Console.WriteNotice(
                "The replica this request reached has stopped serving this user. A synchronization run already in "
                + "flight is allowed to finish what it started; no new run is scheduled. Other replicas pick up the "
                + "change after their next user write or restart.");
        }

        return CliExitCode.Success;
    }
}
