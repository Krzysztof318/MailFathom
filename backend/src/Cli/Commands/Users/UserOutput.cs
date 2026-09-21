// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Cli.Administration.Users;

namespace MailFathom.Cli.Commands.Users;

/// <summary>How the user commands print a roster and report what a write to a record did.</summary>
/// <remarks>
/// One reading of a write outcome for every command that performs one, so a refusal reads the same whichever act
/// produced it. Nothing here prints a mail server, a user name, or anything a credential is resolved from: what a
/// record publishes is what the deployment already redacted, and what a refusal publishes is a sentence about a setting.
/// </remarks>
internal static class UserOutput
{
    /// <summary>Writes the users a deployment holds, each followed by the endpoints they are served on.</summary>
    /// <param name="console">Where the listing is written.</param>
    /// <param name="users">The users, in the deployment's own order.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal static void WriteRoster(ICliConsole console, IReadOnlyList<UserRosterEntry> users)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(users);

        foreach (var user in users)
        {
            console.WriteLine(user.Describe());

            if (!user.Served)
            {
                console.WriteLine(
                    "    not served by the running deployment; its mail is neither read nor refreshed until a restart");
            }

            console.WriteLine(DescribeEndpoints(user.McpEndpoint, user.ClientEndpoint));
        }
    }

    /// <summary>States which of the two mail-serving endpoints a user is served on, as one indented line.</summary>
    /// <param name="mcpEndpoint">Whether the user is served on the MCP endpoint.</param>
    /// <param name="clientEndpoint">Whether the user is served on the client endpoint.</param>
    /// <returns>The line.</returns>
    internal static string DescribeEndpoints(bool mcpEndpoint, bool clientEndpoint) =>
        $"    MCP endpoint: {OnOrOff(mcpEndpoint)}; client endpoint: {OnOrOff(clientEndpoint)}";

    private static string OnOrOff(bool enabled) => enabled ? "on" : "off";

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

            foreach (var standingProblem in answer.Messages ?? [])
            {
                context.Console.WriteNotice(standingProblem);
            }

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

        return refused ? CliExitCode.Failure : CliExitCode.Success;
    }
}
