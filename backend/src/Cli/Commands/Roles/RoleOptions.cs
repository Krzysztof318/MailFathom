// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;

namespace MailFathom.Cli.Commands.Roles;

/// <summary>How a role, and the permissions it grants, are named by the commands that act on one.</summary>
/// <remarks>
/// Which names a role may list is the deployment's rule, which it states in its own refusal naming each unpublished
/// one; restating it here would leave two lists to keep in agreement with the build that serves them.
/// </remarks>
internal static class RoleOptions
{
    /// <summary>Builds the option naming which role a command acts on.</summary>
    /// <returns>The option.</returns>
    internal static Option<Guid> Role() => new("--role")
    {
        Description = "The role to act on, by the identifier 'role list' reports.",
        Required = true,
    };

    /// <summary>Builds the option naming the name a role is read by.</summary>
    /// <param name="description">What the name is for in the command declaring it.</param>
    /// <returns>The option.</returns>
    internal static Option<string> Name(string description) => new("--name")
    {
        Description = description,
        Required = true,
    };

    /// <summary>Builds the repeatable option naming a permission the role grants.</summary>
    /// <returns>The option.</returns>
    /// <remarks>Written once per permission rather than as one delimited value, so a name is never split by whichever separator a shell decided to expand.</remarks>
    internal static Option<string[]> Permission() => new("--permission")
    {
        Description = "A published permission the role grants, repeatable. Refused beside '--no-permissions'.",
    };

    /// <summary>Builds the flag stating that the role grants nothing.</summary>
    /// <returns>The option.</returns>
    /// <remarks>
    /// Separate from an empty <c>--permission</c>, because a repeatable option written zero times is indistinguishable
    /// from one nobody wrote, and replacing a role's list with nothing takes every name away from everybody it is
    /// assigned to. Stating it takes a word of its own so it cannot be reached by forgetting the others.
    /// </remarks>
    internal static Option<bool> NoPermissions() => new("--no-permissions")
    {
        Description = "The role grants nothing. Refused beside '--permission'.",
    };

    /// <summary>Settles on the list a role grants, refusing an invocation that named both or neither.</summary>
    /// <param name="permissions">The permissions the invocation named, which may be none.</param>
    /// <param name="noPermissions">Whether the invocation stated that the role grants nothing.</param>
    /// <returns>The permission names to send, which is empty only when stated.</returns>
    /// <exception cref="CliFailure">Thrown when the invocation named permissions and none at once, or named neither.</exception>
    internal static IReadOnlyList<string> ResolvePermissions(string[]? permissions, bool noPermissions) =>
        (permissions ?? [], noPermissions) switch
        {
            ({ Length: > 0 }, true) => throw new CliFailure(
                "The invocation both names permissions and says the role grants none. Drop '--no-permissions' to grant "
                + "what was named, or drop the '--permission' arguments to grant nothing."),
            ({ Length: 0 }, false) => throw new CliFailure(
                "The invocation names no permission. Pass '--permission' once per name the role grants, or "
                + "'--no-permissions' to state that it grants nothing."),
            (var named, _) => named,
        };
}
