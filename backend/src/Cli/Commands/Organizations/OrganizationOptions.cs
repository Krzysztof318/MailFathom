// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;

namespace MailFathom.Cli.Commands.Organizations;

/// <summary>How an organization is named by the commands that act on one.</summary>
/// <remarks>
/// Required rather than settled from the listing the way a user is: a deployment holding one organization is not the
/// ordinary shape the way one holding one person is, and guessing which organization a rename or a removal meant would
/// change how somebody signs in.
/// </remarks>
internal static class OrganizationOptions
{
    /// <summary>Builds the option naming which organization a command acts on.</summary>
    /// <returns>The option.</returns>
    internal static Option<Guid> Organization() => new("--organization")
    {
        Description = "The organization to act on, by the identifier 'organization list' reports.",
        Required = true,
    };

    /// <summary>Builds the option naming the short name an organization's members sign in under.</summary>
    /// <param name="description">What the short name is for in the command declaring it.</param>
    /// <returns>The option.</returns>
    internal static Option<string> ShortName(string description) => new("--short-name")
    {
        Description = description,
        Required = true,
    };
}
