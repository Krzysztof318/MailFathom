// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Persistence.Grants;

/// <summary>One row of a grant's explanation as the database answers it, before its name and its scope are read.</summary>
/// <param name="Permission">The stored permission name.</param>
/// <param name="RoleId">The role listing it.</param>
/// <param name="RoleName">The role's name.</param>
/// <param name="AssignmentId">The assignment giving the role.</param>
/// <param name="GroupId">The group the assignment names, or <see langword="null" /> for one naming the user.</param>
/// <param name="GroupName">That group's name, or <see langword="null" /> for one naming the user.</param>
/// <param name="ScopeOrganizationId">The organization the assignment's scope names, if any.</param>
/// <param name="ScopeUserId">The user the assignment's scope names, if any.</param>
internal sealed record GrantSourceRow(
    string Permission,
    Guid RoleId,
    string RoleName,
    Guid AssignmentId,
    Guid? GroupId,
    string? GroupName,
    Guid? ScopeOrganizationId,
    Guid? ScopeUserId);
