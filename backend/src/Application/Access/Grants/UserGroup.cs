// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Access.Grants;

/// <summary>One group of users, which a role can be assigned to so each member holds it.</summary>
/// <param name="Id">The identifier every membership and assignment names the group by.</param>
/// <param name="Name">The name an operator reads the group by, unique across the deployment.</param>
/// <param name="OrganizationId">The organization the group belongs to, or <see langword="null" /> for a group in none.</param>
/// <param name="Members">How many users belong to it.</param>
/// <param name="CreatedAt">When the group was recorded.</param>
/// <remarks>
/// A group holds users and nothing else, so no group is a member of another and why somebody holds a permission is
/// always one row away. A group in an organization holds only that organization's members, which is what lets the
/// organization's administrator manage it.
/// </remarks>
public sealed record UserGroup(Guid Id, string Name, Guid? OrganizationId, int Members, DateTimeOffset CreatedAt)
{
    /// <summary>The longest name a group is recorded under.</summary>
    public const int MaximumNameLength = 128;
}
