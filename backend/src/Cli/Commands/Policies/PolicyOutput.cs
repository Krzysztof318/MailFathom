// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Cli.Administration.Policies;

namespace MailFathom.Cli.Commands.Policies;

/// <summary>How the policy commands report what a write to a settings policy did.</summary>
internal static class PolicyOutput
{
    /// <summary>Reports what one write to a settings policy did, and returns what the command exits with.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <param name="answer">What the deployment said.</param>
    /// <returns>Success where the policy committed or there was nothing to change, and failure where the deployment refused.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal static int ReportWrite(CliContext context, SettingsPolicyWriteAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(answer);

        if (answer.Committed)
        {
            context.Console.WriteLine(
                $"Committed settings policy version {answer.Version.ToString(CultureInfo.InvariantCulture)}.");

            foreach (var remark in answer.Messages ?? [])
            {
                context.Console.WriteNotice(remark);
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
