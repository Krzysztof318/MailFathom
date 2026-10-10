// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Paging;
using MailFathom.Domain.Access;

namespace MailFathom.Application.Access.Grants;

/// <summary>Where the three records a user's grant is computed from are kept: roles, groups, and role assignments.</summary>
/// <remarks>
/// <para>
/// A role is a name and an explicit list of permissions, a group is a set of users, and an assignment gives one role
/// to one user or group at one scope. They are records in PostgreSQL rather than configuration, so a change to any of
/// them is the same on every replica from the moment it commits.
/// </para>
/// <para>
/// Every invariant a write can break is decided by the database at the moment of the write rather than by a read a
/// moment earlier: a role name and a group name are unique across the deployment, an assignment names a role, a
/// principal, and a scope that exist, and the same role is never given to the same principal at the same scope twice.
/// Each refusal is answered as an outcome rather than raised. Removing a user or an organization takes every membership
/// and assignment naming it with it in the statement that removes it, while removing a role or a group that is still
/// assigned is refused, because it would revoke a grant from everybody holding it as a side effect.
/// </para>
/// <para>
/// Nothing here decides who may write what. The escalation rules — nobody grants more than they hold, or wider than
/// they hold it — belong to the use cases that call this store.
/// </para>
/// </remarks>
public interface IGrantStore
{
    /// <summary>Reads one page of the roles this deployment holds, in identifier order.</summary>
    /// <param name="query">The page asked for.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page, each role with the permissions it lists.</returns>
    Task<AdministrativeListingPage<Role>> ReadRolesAsync(AdministrativeListingQuery query, CancellationToken cancellationToken);

    /// <summary>Records a role.</summary>
    /// <param name="roleId">The identifier the role is to carry.</param>
    /// <param name="name">The name an operator reads it by, already judged.</param>
    /// <param name="permissions">What it grants.</param>
    /// <param name="createdAt">When it was recorded.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see cref="GrantWriteOutcome.Written" /> or <see cref="GrantWriteOutcome.NameTaken" />.</returns>
    Task<GrantWriteResult> CreateRoleAsync(
        Guid roleId,
        string name,
        RolePermissions permissions,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken);

    /// <summary>Replaces the name an operator reads a role by.</summary>
    /// <param name="roleId">The role.</param>
    /// <param name="name">The new name, already judged.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see cref="GrantWriteOutcome.Written" />, <see cref="GrantWriteOutcome.UnknownRole" />, or <see cref="GrantWriteOutcome.NameTaken" />.</returns>
    Task<GrantWriteResult> RenameRoleAsync(Guid roleId, string name, CancellationToken cancellationToken);

    /// <summary>Replaces the whole list of permissions a role grants.</summary>
    /// <param name="roleId">The role.</param>
    /// <param name="permissions">What it is to grant from now on, which drops any stored name this build no longer publishes.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see cref="GrantWriteOutcome.Written" /> or <see cref="GrantWriteOutcome.UnknownRole" />.</returns>
    Task<GrantWriteResult> ReplaceRolePermissionsAsync(
        Guid roleId,
        RolePermissions permissions,
        CancellationToken cancellationToken);

    /// <summary>Removes a role nobody is assigned.</summary>
    /// <param name="roleId">The role.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see cref="GrantWriteOutcome.Written" />, <see cref="GrantWriteOutcome.UnknownRole" />, or <see cref="GrantWriteOutcome.StillAssigned" /> carrying how many assignments stand in the way.</returns>
    Task<GrantWriteResult> DeleteRoleAsync(Guid roleId, CancellationToken cancellationToken);

    /// <summary>Reads one page of the groups this deployment holds, in identifier order.</summary>
    /// <param name="query">The page asked for.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page, each group with how many members it has.</returns>
    Task<AdministrativeListingPage<UserGroup>> ReadGroupsAsync(
        AdministrativeListingQuery query,
        CancellationToken cancellationToken);

    /// <summary>Reads one page of the members of one group, in user identifier order.</summary>
    /// <param name="groupId">The group.</param>
    /// <param name="query">The page asked for.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page; empty for a group nobody holds.</returns>
    Task<AdministrativeListingPage<UserId>> ReadGroupMembersAsync(
        Guid groupId,
        AdministrativeListingQuery query,
        CancellationToken cancellationToken);

    /// <summary>Records a group.</summary>
    /// <param name="groupId">The identifier the group is to carry.</param>
    /// <param name="name">The name an operator reads it by, already judged.</param>
    /// <param name="organizationId">The organization it belongs to, or <see langword="null" /> for none.</param>
    /// <param name="createdAt">When it was recorded.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see cref="GrantWriteOutcome.Written" />, <see cref="GrantWriteOutcome.NameTaken" />, or <see cref="GrantWriteOutcome.UnknownOrganization" />.</returns>
    Task<GrantWriteResult> CreateGroupAsync(
        Guid groupId,
        string name,
        Guid? organizationId,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken);

    /// <summary>Replaces the name an operator reads a group by.</summary>
    /// <param name="groupId">The group.</param>
    /// <param name="name">The new name, already judged.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see cref="GrantWriteOutcome.Written" />, <see cref="GrantWriteOutcome.UnknownGroup" />, or <see cref="GrantWriteOutcome.NameTaken" />.</returns>
    Task<GrantWriteResult> RenameGroupAsync(Guid groupId, string name, CancellationToken cancellationToken);

    /// <summary>Removes a group nothing is assigned to, and its memberships with it.</summary>
    /// <param name="groupId">The group.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see cref="GrantWriteOutcome.Written" />, <see cref="GrantWriteOutcome.UnknownGroup" />, or <see cref="GrantWriteOutcome.StillAssigned" /> carrying how many assignments stand in the way.</returns>
    Task<GrantWriteResult> DeleteGroupAsync(Guid groupId, CancellationToken cancellationToken);

    /// <summary>Makes a user a member of a group.</summary>
    /// <param name="groupId">The group.</param>
    /// <param name="user">The user joining it.</param>
    /// <param name="addedAt">When they joined.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see cref="GrantWriteOutcome.Written" />, also when they were already a member; <see cref="GrantWriteOutcome.UnknownGroup" />, <see cref="GrantWriteOutcome.UnknownUser" />, or <see cref="GrantWriteOutcome.OutsideGroupOrganization" />.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody.</exception>
    Task<GrantWriteResult> AddGroupMemberAsync(
        Guid groupId,
        UserId user,
        DateTimeOffset addedAt,
        CancellationToken cancellationToken);

    /// <summary>Ends a user's membership of a group.</summary>
    /// <param name="groupId">The group.</param>
    /// <param name="user">The user leaving it.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see cref="GrantWriteOutcome.Written" />, also when they were not a member.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody.</exception>
    Task<GrantWriteResult> RemoveGroupMemberAsync(Guid groupId, UserId user, CancellationToken cancellationToken);

    /// <summary>Reads one page of every role assignment this deployment holds, in identifier order.</summary>
    /// <param name="query">The page asked for.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page.</returns>
    Task<AdministrativeListingPage<RoleAssignment>> ReadAssignmentsAsync(
        AdministrativeListingQuery query,
        CancellationToken cancellationToken);

    /// <summary>Gives a role to a user or a group at a scope.</summary>
    /// <param name="assignmentId">The identifier the assignment is to carry.</param>
    /// <param name="roleId">The role.</param>
    /// <param name="principal">Who it is given to.</param>
    /// <param name="scope">What it reaches.</param>
    /// <param name="assignedAt">When it was given.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see cref="GrantWriteOutcome.Written" />, <see cref="GrantWriteOutcome.AlreadyAssigned" />, or the outcome naming which of the role, the group, the user, or the organization does not exist.</returns>
    Task<GrantWriteResult> AssignAsync(
        Guid assignmentId,
        Guid roleId,
        AssignmentPrincipal principal,
        AssignmentScope scope,
        DateTimeOffset assignedAt,
        CancellationToken cancellationToken);

    /// <summary>Reads what one user holds through every assignment naming them and every assignment naming a group they are a member of.</summary>
    /// <param name="user">The user.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Each permission those assignments' roles list paired with the scope of the assignment that gave it; empty for a user assigned nothing, and for nobody at all.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody.</exception>
    /// <remarks>A stored name this build does not publish is granted to nobody, as <see cref="RolePermissions.Read" /> decides for every role.</remarks>
    Task<ScopedGrant> ReadGrantOfAsync(UserId user, CancellationToken cancellationToken);

    /// <summary>Reads which roles one user holds, at which scopes, through the same assignments <see cref="ReadGrantOfAsync" /> reads.</summary>
    /// <param name="user">The user.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Each role at each scope once, ordered by role name and then by scope; empty for a user assigned nothing, and for nobody at all.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody.</exception>
    /// <remarks>It answers who a user is to the deployment, which is what a person reads; what they may do is the grant, and the two are never derived from each other.</remarks>
    Task<IReadOnlyList<HeldRole>> ReadRolesHeldByAsync(UserId user, CancellationToken cancellationToken);

    /// <summary>Revokes one assignment.</summary>
    /// <param name="assignmentId">The assignment.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see cref="GrantWriteOutcome.Written" /> or <see cref="GrantWriteOutcome.UnknownAssignment" />.</returns>
    Task<GrantWriteResult> RevokeAsync(Guid assignmentId, CancellationToken cancellationToken);
}
