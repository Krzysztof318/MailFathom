// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Cli.Administration.Users;

namespace MailFathom.Cli.Commands.Users;

/// <summary>How the user commands print a roster and report what a write to a record did.</summary>
/// <remarks>
/// One reading of a write outcome for every command that performs one, so a refusal reads the same whichever act
/// produced it and the one refusal a command can repair names the repair in one place. Nothing here prints a mail
/// server, a user name, or anything a credential is resolved from: what a record publishes is what the deployment
/// already redacted, and what a refusal publishes is a sentence about a setting.
/// </remarks>
internal static class UserOutput
{
    /// <summary>What a command says about a record a configuration source still supplies, before it does anything with it.</summary>
    /// <remarks>
    /// One sentence for the two commands that read a record and find that state: <c>user show</c> reports it beside the
    /// empty document it just printed, and <c>user edit</c> refuses with it rather than opening a buffer over a write
    /// that cannot be accepted. It is not what <see cref="ReportWrite" /> says about the same state, which is about a
    /// write the deployment has already refused.
    /// </remarks>
    internal const string RecordSuppliedByAConfigurationSource =
        "A configuration source supplies this user's mail accounts, so their record is empty and every change to it is "
        + "refused. Change them where they are declared.";

    /// <summary>Writes the users a deployment holds, one to a line.</summary>
    /// <param name="console">Where the listing is written.</param>
    /// <param name="users">The users, in the deployment's own order.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal static void WriteRoster(ICliConsole console, IReadOnlyList<MailUserRosterEntry> users)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(users);

        foreach (var user in users)
        {
            console.WriteLine(user.Describe());
            console.WriteLine($"    mail accounts: {DescribeSource(user)}");

            if (!user.Served)
            {
                console.WriteLine(
                    "    not served by the running deployment; its mail is neither read nor refreshed until a restart");
            }
        }
    }

    /// <summary>Reports what one write to a user's record did, and returns what the command exits with.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <param name="answer">What the deployment said.</param>
    /// <returns>Success where the record committed or there was nothing to change, and failure where the deployment refused.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal static int ReportWrite(CliContext context, UserRecordWriteAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(answer);

        if (answer.Committed)
        {
            context.Console.WriteLine(
                $"Committed user record version {answer.Version.ToString(CultureInfo.InvariantCulture)}.");
            context.Console.WriteNotice(
                "The replica this request reached is using this account set now. A synchronization run already in "
                + "flight finishes against the previous version; the next run uses this one. Other replicas pick up "
                + "the change after their next user write or restart.");

            return CliExitCode.Success;
        }

        var refused = answer.Code is not null;

        foreach (var message in answer.DescribeRefusal())
        {
            if (refused)
            {
                context.Console.WriteError(message);
            }
            else
            {
                context.Console.WriteLine(message);
            }
        }

        if (answer.Code == UserRecordWriteAnswer.RecordReadFromConfiguration)
        {
            context.Console.WriteNotice(
                "Change this user's mail accounts in MailSynchronization:Accounts. Nothing moves them into their record for you: clear that section, restart, and state their mailboxes again with 'mfctl user account add'.");
        }

        return refused ? CliExitCode.Failure : CliExitCode.Success;
    }

    /// <summary>Says where one user's mail accounts are read from, in the words an operator edits.</summary>
    private static string DescribeSource(MailUserRosterEntry user) => user.RecordIsTheirOwn
        ? "their own record, maintained with 'mfctl user account'"
        : "this deployment's own MailSynchronization:Accounts, which is where they are changed";
}
