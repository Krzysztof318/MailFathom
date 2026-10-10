// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;

namespace MailFathom.Cli.Commands.Policies;

/// <summary>How the policy commands name the scope whose settings policy they address.</summary>
/// <remarks>
/// Optional where the commands acting on an organization require it, because a policy is held at two scopes and the
/// deployment's is the one with no organization to name. The organization is named the way every other command names
/// one, by its identifier, and is never checked against the listing first: the deployment refuses one it does not hold
/// and says so.
/// </remarks>
internal static class PolicyOptions
{
    /// <summary>Builds the option naming the organization whose policy a command addresses.</summary>
    /// <returns>The option.</returns>
    internal static Option<Guid?> Organization() => new("--organization")
    {
        Description =
            "The organization whose policy this is, by the identifier 'organization list' reports. Left out, the "
            + "command addresses the deployment's own policy.",
    };
}
