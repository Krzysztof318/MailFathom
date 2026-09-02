// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Cli;

/// <summary>What the command reports to whatever ran it.</summary>
/// <remarks>
/// Two codes, because a script acts on the difference between "this worked" and "this did not" and nothing finer is
/// established yet. A code per failure kind is a contract, so it waits until something needs to branch on one.
/// </remarks>
internal static class CliExitCode
{
    /// <summary>The command did what it was asked.</summary>
    internal const int Success = 0;

    /// <summary>The command failed for a reason the operator can act on, already reported to standard error.</summary>
    internal const int Failure = 1;
}
