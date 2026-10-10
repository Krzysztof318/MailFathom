// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Access.Grants;

/// <summary>One role: a name unique across the deployment and the list of permissions it grants, written as names and patterns.</summary>
/// <param name="Id">The identifier every assignment of the role names it by.</param>
/// <param name="Name">The name an operator reads the role by.</param>
/// <param name="Permissions">What the role grants wherever it is assigned, and any stored entry that grants nothing in this build.</param>
/// <param name="CreatedAt">When the role was recorded.</param>
/// <remarks>
/// No use case asks whether a caller holds a role: it checks a permission the role grants, so a role is one decision
/// about many people written once. The one thing read off the role itself is whether its list widens on upgrade,
/// which decides who may give it.
/// </remarks>
public sealed record Role(Guid Id, string Name, RolePermissions Permissions, DateTimeOffset CreatedAt)
{
    /// <summary>The longest name a role is recorded under.</summary>
    public const int MaximumNameLength = 128;
}
