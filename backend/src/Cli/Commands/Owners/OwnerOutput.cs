// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Cli.Administration.Owners;

namespace MailFathom.Cli.Commands.Owners;

/// <summary>How the owner commands print a roster and report what a write to a record did.</summary>
/// <remarks>
/// One reading of a write outcome for every command that performs one, so a refusal reads the same whichever act
/// produced it and the one refusal a command can repair names the repair in one place. Nothing here prints a mail
/// server, a user name, or anything a credential is resolved from: what a record publishes is what the deployment
/// already redacted, and what a refusal publishes is a sentence about a setting.
/// </remarks>
internal static class OwnerOutput
{
    /// <summary>Writes the owners a deployment holds, one to a line.</summary>
    /// <param name="console">Where the listing is written.</param>
    /// <param name="owners">The owners, in the deployment's own order.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal static void WriteRoster(ICliConsole console, IReadOnlyList<MailOwnerRosterEntry> owners)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(owners);

        foreach (var owner in owners)
        {
            console.WriteLine(owner.Describe());
            console.WriteLine($"    mail accounts: {DescribeSource(owner)}");

            if (!owner.Served)
            {
                console.WriteLine(
                    "    not served by the running deployment; its mail is neither read nor refreshed until a restart");
            }
        }
    }

    /// <summary>Reports what one write to an owner's record did, and returns what the command exits with.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <param name="answer">What the deployment said.</param>
    /// <returns>Success where the record committed or there was nothing to change, and failure where the deployment refused.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal static int ReportWrite(CliContext context, OwnerRecordWriteAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(answer);

        if (answer.Committed)
        {
            context.Console.WriteLine(
                $"Committed owner record version {answer.Version.ToString(CultureInfo.InvariantCulture)}.");
            context.Console.WriteNotice(
                "The replica this request reached is using this account set now. A synchronization run already in "
                + "flight finishes against the previous version; the next run uses this one. Other replicas pick up "
                + "the change after their next owner write or restart.");

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

        if (answer.Code == OwnerRecordWriteAnswer.RecordReadFromConfiguration)
        {
            context.Console.WriteNotice(
                "Run 'mfctl owner adopt' to move this owner's mail accounts out of the deployment's files and into their own record. Every change afterwards is an ordinary one.");
        }

        return refused ? CliExitCode.Failure : CliExitCode.Success;
    }

    /// <summary>Says where one owner's mail accounts are read from, in the words an operator edits.</summary>
    private static string DescribeSource(MailOwnerRosterEntry owner) => owner.RecordIsTheirOwn
        ? "their own record, maintained with 'mfctl owner account'"
        : "a configuration source; 'mfctl owner adopt' moves them into their own record";
}
