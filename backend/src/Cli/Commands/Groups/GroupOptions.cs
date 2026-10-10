// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;

namespace MailFathom.Cli.Commands.Groups;

/// <summary>How a group, and a user joining or leaving one, are named by the commands that act on them.</summary>
/// <remarks>
/// Both are required rather than settled from a listing the way a user is elsewhere: a membership gives somebody every
/// role the group holds, and guessing which user or which group was meant would hand that to the wrong person.
/// </remarks>
internal static class GroupOptions
{
    /// <summary>Builds the option naming which group a command acts on.</summary>
    /// <returns>The option.</returns>
    internal static Option<Guid> Group() => new("--group")
    {
        Description = "The group to act on, by the identifier 'group list' reports.",
        Required = true,
    };

    /// <summary>Builds the option naming the user joining or leaving a group.</summary>
    /// <returns>The option.</returns>
    internal static Option<Guid> Member() => new("--user")
    {
        Description = "The user joining or leaving the group, by the identifier 'user list' reports.",
        Required = true,
    };
}
