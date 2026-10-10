// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Access.Grants;

/// <summary>Why a user holds one permission at one scope: the role that lists it, the assignment that gives the role, and the group the assignment reaches them through, if any.</summary>
/// <param name="Permission">The permission held.</param>
/// <param name="RoleId">The role listing it.</param>
/// <param name="RoleName">The name an operator reads the role by.</param>
/// <param name="AssignmentId">The assignment giving the role.</param>
/// <param name="GroupId">The group the assignment names, or <see langword="null" /> where it names the user directly.</param>
/// <param name="GroupName">The name an operator reads that group by, or <see langword="null" /> where it names the user directly.</param>
/// <param name="Scope">The scope the assignment names, which is where the permission is held.</param>
/// <remarks>
/// One row of the answer ADR 0012 gives every grant: <em>this role, directly or through this group, at this scope</em>.
/// Groups are not nested, so the row is the whole explanation rather than the first step of a path.
/// </remarks>
public sealed record GrantSource(
    MailFathomPermission Permission,
    Guid RoleId,
    string RoleName,
    Guid AssignmentId,
    Guid? GroupId,
    string? GroupName,
    AssignmentScope Scope)
{
    /// <summary>Gets whether the permission reaches nothing where it is held, because every operation it covers is the deployment's alone and the assignment names a narrower scope.</summary>
    public bool IsInert => this.Permission.IsDeploymentScopeOnly && this.Scope.Kind != AssignmentScopeKind.Deployment;
}
