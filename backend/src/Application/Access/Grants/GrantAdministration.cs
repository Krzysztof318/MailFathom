// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access.Credentials;
using MailFathom.Application.Paging;
using MailFathom.Domain.Access;

namespace MailFathom.Application.Access.Grants;

/// <summary>What an administrator does to roles, groups, and role assignments, and how a user's grant is explained.</summary>
/// <remarks>
/// <para>
/// Reading is <see cref="MailFathomPermission.AdminRead" /> and writing is <see cref="MailFathomPermission.AdminRolesWrite" />,
/// each held at a scope covering what the act names: the group, the user, and the scope an assignment is made at. Roles
/// are the deployment's vocabulary, so any holder of the reading grant reads them and only the root — the writing grant
/// held over the deployment — defines one. A group in no organization and an assignment at the deployment scope are the
/// deployment's as well. A target outside the caller's scope is answered exactly as one that does not exist, so an
/// organization's administrator learns nothing about anybody else's groups, users, or assignments by naming them.
/// </para>
/// <para>
/// Nobody grants more than they hold, or wider than they hold it. Assigning a role needs every name it lists held by the
/// writer at a scope covering the assignment's; joining a group is receiving its assignments, so adding a member needs
/// every name those assignments give held at a scope covering each of them. A mail name's scope is never read, so it
/// need only be held. A name with no operation below the deployment scope grants nothing at a narrower one, so giving it
/// there widens nobody and is not asked about. Revoking an assignment and ending a membership need only the coverage,
/// because taking a grant away widens nobody — and the store refuses the one that would leave the deployment with no
/// root. See
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0012-authorization-model-named-permissions-and-where-they-are-enforced.md">ADR 0012</see>.
/// </para>
/// <para>
/// The escalation check reads the role's list, the group's assignments, and where each user belongs a moment before the
/// write, and a concurrent change to any of them is not re-read inside it. Each of those changes is itself an act this
/// class bounds by its writer's grant — a role's list is the root's alone to write — so the window lets nobody widen past
/// what somebody holding the reach to widen them already decided.
/// </para>
/// <para>
/// Every write that changed something is recorded through <see cref="IGrantAuditor" /> after it committed, naming the
/// administrator it was admitted as.
/// </para>
/// </remarks>
public sealed class GrantAdministration
{
    private readonly AccessAuthorization authorization;
    private readonly IGrantStore grants;
    private readonly IUserCredentialStore credentials;
    private readonly IGrantAuditor auditor;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the administration over one deployment's roles, groups, and assignments.</summary>
    /// <param name="authorization">What each act is admitted by.</param>
    /// <param name="grants">Where roles, groups, and assignments are kept.</param>
    /// <param name="credentials">Where a user's credentials are kept, which an explanation reads the narrowing of.</param>
    /// <param name="auditor">Where each change is recorded.</param>
    /// <param name="timeProvider">The clock a record is stamped with.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public GrantAdministration(
        AccessAuthorization authorization,
        IGrantStore grants,
        IUserCredentialStore credentials,
        IGrantAuditor auditor,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(grants);
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(auditor);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.authorization = authorization;
        this.grants = grants;
        this.credentials = credentials;
        this.auditor = auditor;
        this.timeProvider = timeProvider;
    }

    /// <summary>Reads one page of the roles this deployment holds.</summary>
    /// <param name="query">The page asked for.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page, in identifier order.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="query" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller holds <see cref="MailFathomPermission.AdminRead" /> at no scope.</exception>
    public Task<AdministrativeListingPage<Role>> ReadRolesAsync(
        AdministrativeListingQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        this.authorization.RequirePermissionAtAnyScope(MailFathomPermission.AdminRead);

        return this.grants.ReadRolesAsync(query, cancellationToken);
    }

    /// <summary>Records a role under an identifier this deployment mints.</summary>
    /// <param name="name">The name an operator reads it by.</param>
    /// <param name="permissions">What it grants.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the act did, carrying the minted identifier when it was written.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name" /> breaks <see cref="FindNameRefusal" />.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="permissions" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <see cref="MailFathomPermission.AdminRolesWrite" /> over the deployment.</exception>
    public async Task<GrantWriteResult> CreateRoleAsync(
        string? name,
        RolePermissions permissions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        this.authorization.RequirePermission(MailFathomPermission.AdminRolesWrite);

        var label = NameOrThrow(name, nameof(name));
        var createdAt = this.timeProvider.GetUtcNow();
        var roleId = Guid.CreateVersion7(createdAt);

        var result = await this.grants.CreateRoleAsync(roleId, label, permissions, createdAt, cancellationToken);

        await this.RecordAsync(result, GrantAct.RoleCreated, roleId, cancellationToken, permissions: permissions.Granted);

        return result.Outcome == GrantWriteOutcome.Written ? result with { RecordId = roleId } : result;
    }

    /// <summary>Replaces the name an operator reads a role by.</summary>
    /// <param name="roleId">The role.</param>
    /// <param name="name">The new name.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the act did.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name" /> breaks <see cref="FindNameRefusal" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <see cref="MailFathomPermission.AdminRolesWrite" /> over the deployment.</exception>
    public async Task<GrantWriteResult> RenameRoleAsync(Guid roleId, string? name, CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminRolesWrite);

        var result = await this.grants.RenameRoleAsync(roleId, NameOrThrow(name, nameof(name)), cancellationToken);

        await this.RecordAsync(result, GrantAct.RoleRenamed, roleId, cancellationToken);

        return result;
    }

    /// <summary>Replaces the whole list of permissions a role grants, which reaches everybody it is assigned to at their next request.</summary>
    /// <param name="roleId">The role.</param>
    /// <param name="permissions">What it is to grant from now on.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the act did, refused as <see cref="GrantWriteOutcome.LastRoot" /> when it would drop the root from the last role giving it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="permissions" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <see cref="MailFathomPermission.AdminRolesWrite" /> over the deployment.</exception>
    /// <remarks>The root may write any published name, its own role included: that is the one place a grant reaches past what its writer held, and why the root is the deployment-wide name alone.</remarks>
    public async Task<GrantWriteResult> ReplaceRolePermissionsAsync(
        Guid roleId,
        RolePermissions permissions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        this.authorization.RequirePermission(MailFathomPermission.AdminRolesWrite);

        var result = await this.grants.ReplaceRolePermissionsAsync(roleId, permissions, cancellationToken);

        await this.RecordAsync(
            result,
            GrantAct.RolePermissionsReplaced,
            roleId,
            cancellationToken,
            permissions: permissions.Granted);

        return result;
    }

    /// <summary>Removes a role nobody is assigned.</summary>
    /// <param name="roleId">The role.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the act did, carrying how many assignments refused it.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <see cref="MailFathomPermission.AdminRolesWrite" /> over the deployment.</exception>
    public async Task<GrantWriteResult> DeleteRoleAsync(Guid roleId, CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminRolesWrite);

        var result = await this.grants.DeleteRoleAsync(roleId, cancellationToken);

        await this.RecordAsync(result, GrantAct.RoleDeleted, roleId, cancellationToken);

        return result;
    }

    /// <summary>Reads one page of the groups the caller's scopes cover.</summary>
    /// <param name="query">The page asked for.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page, in identifier order.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="query" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller holds <see cref="MailFathomPermission.AdminRead" /> at no scope.</exception>
    public Task<AdministrativeListingPage<UserGroup>> ReadGroupsAsync(
        AdministrativeListingQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        this.authorization.RequirePermissionAtAnyScope(MailFathomPermission.AdminRead);

        return this.grants.ReadGroupsAsync(query, this.ReachOf(MailFathomPermission.AdminRead), cancellationToken);
    }

    /// <summary>Reads one page of the members of one group the caller's scope covers.</summary>
    /// <param name="groupId">The group.</param>
    /// <param name="query">The page asked for.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page in user identifier order, or <see langword="null" /> where no such group exists within the caller's scope.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="query" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller holds <see cref="MailFathomPermission.AdminRead" /> at no scope.</exception>
    public async Task<AdministrativeListingPage<UserId>?> ReadGroupMembersAsync(
        Guid groupId,
        AdministrativeListingQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        this.authorization.RequirePermissionAtAnyScope(MailFathomPermission.AdminRead);

        return await this.CoveredGroupAsync(MailFathomPermission.AdminRead, groupId, cancellationToken) is null
            ? null
            : await this.grants.ReadGroupMembersAsync(groupId, query, cancellationToken);
    }

    /// <summary>Records a group under an identifier this deployment mints.</summary>
    /// <param name="name">The name an operator reads it by.</param>
    /// <param name="organizationId">The organization it belongs to, or <see langword="null" /> for a group of the deployment's.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the act did, carrying the minted identifier when it was written; <see cref="GrantWriteOutcome.UnknownOrganization" /> also for an organization outside the caller's scope.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name" /> breaks <see cref="FindNameRefusal" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller holds <see cref="MailFathomPermission.AdminRolesWrite" /> at no scope, or — for a group in no organization — not over the deployment.</exception>
    public async Task<GrantWriteResult> CreateGroupAsync(
        string? name,
        Guid? organizationId,
        CancellationToken cancellationToken)
    {
        if (organizationId is not { } organization)
        {
            this.authorization.RequirePermissionOverTheDeployment(MailFathomPermission.AdminRolesWrite);
        }
        else
        {
            this.authorization.RequirePermissionAtAnyScope(MailFathomPermission.AdminRolesWrite);

            if (!this.authorization.Covers(
                    MailFathomPermission.AdminRolesWrite,
                    AdministrativeTarget.OrganizationItself(organization)))
            {
                return GrantWriteResult.Of(GrantWriteOutcome.UnknownOrganization);
            }
        }

        var label = NameOrThrow(name, nameof(name));
        var createdAt = this.timeProvider.GetUtcNow();
        var groupId = Guid.CreateVersion7(createdAt);

        var result = await this.grants.CreateGroupAsync(groupId, label, organizationId, createdAt, cancellationToken);

        await this.RecordAsync(result, GrantAct.GroupCreated, groupId, cancellationToken);

        return result.Outcome == GrantWriteOutcome.Written ? result with { RecordId = groupId } : result;
    }

    /// <summary>Replaces the name an operator reads a group by.</summary>
    /// <param name="groupId">The group.</param>
    /// <param name="name">The new name.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the act did; <see cref="GrantWriteOutcome.UnknownGroup" /> also for a group outside the caller's scope.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name" /> breaks <see cref="FindNameRefusal" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller holds <see cref="MailFathomPermission.AdminRolesWrite" /> at no scope.</exception>
    public async Task<GrantWriteResult> RenameGroupAsync(Guid groupId, string? name, CancellationToken cancellationToken)
    {
        this.authorization.RequirePermissionAtAnyScope(MailFathomPermission.AdminRolesWrite);

        var label = NameOrThrow(name, nameof(name));

        if (await this.CoveredGroupAsync(MailFathomPermission.AdminRolesWrite, groupId, cancellationToken) is null)
        {
            return GrantWriteResult.Of(GrantWriteOutcome.UnknownGroup);
        }

        var result = await this.grants.RenameGroupAsync(groupId, label, cancellationToken);

        await this.RecordAsync(result, GrantAct.GroupRenamed, groupId, cancellationToken);

        return result;
    }

    /// <summary>Removes a group nothing is assigned to, and its memberships with it.</summary>
    /// <param name="groupId">The group.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the act did, carrying how many assignments refused it; <see cref="GrantWriteOutcome.UnknownGroup" /> also for a group outside the caller's scope.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller holds <see cref="MailFathomPermission.AdminRolesWrite" /> at no scope.</exception>
    public async Task<GrantWriteResult> DeleteGroupAsync(Guid groupId, CancellationToken cancellationToken)
    {
        this.authorization.RequirePermissionAtAnyScope(MailFathomPermission.AdminRolesWrite);

        if (await this.CoveredGroupAsync(MailFathomPermission.AdminRolesWrite, groupId, cancellationToken) is null)
        {
            return GrantWriteResult.Of(GrantWriteOutcome.UnknownGroup);
        }

        var result = await this.grants.DeleteGroupAsync(groupId, cancellationToken);

        await this.RecordAsync(result, GrantAct.GroupDeleted, groupId, cancellationToken);

        return result;
    }

    /// <summary>Makes a user a member of a group, which gives them every assignment the group holds.</summary>
    /// <param name="groupId">The group.</param>
    /// <param name="user">The user joining it.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the act did, <see cref="GrantWriteOutcome.Unchanged" /> for somebody who was already a member; <see cref="GrantWriteOutcome.UnknownGroup" /> and <see cref="GrantWriteOutcome.UnknownUser" /> also for a group or a user outside the caller's scope.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller holds <see cref="MailFathomPermission.AdminRolesWrite" /> at no scope, or does not hold a name the group's assignments give at a scope covering where they give it.</exception>
    public async Task<GrantWriteResult> AddGroupMemberAsync(
        Guid groupId,
        UserId user,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermissionAtAnyScope(MailFathomPermission.AdminRolesWrite);

        RequireNamedUser(user);

        if (await this.CoveredGroupAsync(MailFathomPermission.AdminRolesWrite, groupId, cancellationToken) is null)
        {
            return GrantWriteResult.Of(GrantWriteOutcome.UnknownGroup);
        }

        if (await this.CoveredUserAsync(user, cancellationToken) is null)
        {
            return GrantWriteResult.Of(GrantWriteOutcome.UnknownUser);
        }

        var joined = await this.grants.ReadGrantOfGroupAsync(groupId, cancellationToken);

        await this.RequireHeldAsync(
            joined.Permissions.SelectMany(permission => joined.ScopesOf(permission).Select(scope => (permission, scope))),
            cancellationToken);

        var result = await this.grants.AddGroupMemberAsync(groupId, user, this.timeProvider.GetUtcNow(), cancellationToken);

        await this.RecordAsync(result, GrantAct.GroupMemberAdded, groupId, cancellationToken, member: user);

        return result;
    }

    /// <summary>Ends a user's membership of a group, which takes every assignment the group holds away from them.</summary>
    /// <param name="groupId">The group.</param>
    /// <param name="user">The user leaving it.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the act did, <see cref="GrantWriteOutcome.Unchanged" /> for somebody who was not a member, refused as <see cref="GrantWriteOutcome.LastRoot" /> when it would take the last root away; <see cref="GrantWriteOutcome.UnknownGroup" /> and <see cref="GrantWriteOutcome.UnknownUser" /> also for a group or a user outside the caller's scope.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller holds <see cref="MailFathomPermission.AdminRolesWrite" /> at no scope.</exception>
    public async Task<GrantWriteResult> RemoveGroupMemberAsync(
        Guid groupId,
        UserId user,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermissionAtAnyScope(MailFathomPermission.AdminRolesWrite);

        RequireNamedUser(user);

        if (await this.CoveredGroupAsync(MailFathomPermission.AdminRolesWrite, groupId, cancellationToken) is null)
        {
            return GrantWriteResult.Of(GrantWriteOutcome.UnknownGroup);
        }

        if (await this.CoveredUserAsync(user, cancellationToken) is null)
        {
            return GrantWriteResult.Of(GrantWriteOutcome.UnknownUser);
        }

        var result = await this.grants.RemoveGroupMemberAsync(groupId, user, cancellationToken);

        await this.RecordAsync(result, GrantAct.GroupMemberRemoved, groupId, cancellationToken, member: user);

        return result;
    }

    /// <summary>Reads one page of the role assignments the caller's scopes cover.</summary>
    /// <param name="query">The page asked for.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page, in identifier order.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="query" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller holds <see cref="MailFathomPermission.AdminRead" /> at no scope.</exception>
    public Task<AdministrativeListingPage<RoleAssignment>> ReadAssignmentsAsync(
        AdministrativeListingQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        this.authorization.RequirePermissionAtAnyScope(MailFathomPermission.AdminRead);

        return this.grants.ReadAssignmentsAsync(query, this.ReachOf(MailFathomPermission.AdminRead), cancellationToken);
    }

    /// <summary>Gives a role to a user or a group at a scope, under an identifier this deployment mints.</summary>
    /// <param name="roleId">The role.</param>
    /// <param name="principal">Who it is given to.</param>
    /// <param name="scope">What it reaches.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the act did, carrying the minted identifier when it was written; the outcome naming an unknown group, user, or organization also for one outside the caller's scope.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="principal" /> or <paramref name="scope" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller holds <see cref="MailFathomPermission.AdminRolesWrite" /> at no scope, not over the deployment for an assignment at the deployment scope, or does not hold a name the role lists at a scope covering the assignment's.</exception>
    public async Task<GrantWriteResult> AssignAsync(
        Guid roleId,
        AssignmentPrincipal principal,
        AssignmentScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(scope);

        if (scope.Kind == AssignmentScopeKind.Deployment)
        {
            this.authorization.RequirePermissionOverTheDeployment(MailFathomPermission.AdminRolesWrite);
        }
        else
        {
            this.authorization.RequirePermissionAtAnyScope(MailFathomPermission.AdminRolesWrite);
        }

        if (await this.UncoveredPrincipalAsync(principal, cancellationToken) is { } principalRefusal)
        {
            return GrantWriteResult.Of(principalRefusal);
        }

        if (await this.UncoveredScopeAsync(scope, cancellationToken) is { } scopeRefusal)
        {
            return GrantWriteResult.Of(scopeRefusal);
        }

        if (await this.grants.ReadRoleAsync(roleId, cancellationToken) is not { } role)
        {
            return GrantWriteResult.Of(GrantWriteOutcome.UnknownRole);
        }

        await this.RequireHeldAsync(role.Permissions.Granted.Select(permission => (permission, scope)), cancellationToken);

        var assignedAt = this.timeProvider.GetUtcNow();
        var assignmentId = Guid.CreateVersion7(assignedAt);

        var result = await this.grants.AssignAsync(assignmentId, roleId, principal, scope, assignedAt, cancellationToken);

        await this.RecordAsync(
            result,
            GrantAct.RoleAssigned,
            assignmentId,
            cancellationToken,
            assignment: new RoleAssignment(assignmentId, roleId, principal, scope, assignedAt));

        return result.Outcome == GrantWriteOutcome.Written ? result with { RecordId = assignmentId } : result;
    }

    /// <summary>Revokes one assignment.</summary>
    /// <param name="assignmentId">The assignment.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the act did, refused as <see cref="GrantWriteOutcome.LastRoot" /> when it is the last assignment giving the root; <see cref="GrantWriteOutcome.UnknownAssignment" /> also for one whose principal or scope lies outside the caller's.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller holds <see cref="MailFathomPermission.AdminRolesWrite" /> at no scope.</exception>
    public async Task<GrantWriteResult> RevokeAsync(Guid assignmentId, CancellationToken cancellationToken)
    {
        this.authorization.RequirePermissionAtAnyScope(MailFathomPermission.AdminRolesWrite);

        if (await this.grants.ReadAssignmentAsync(assignmentId, cancellationToken) is not { } assignment
            || await this.UncoveredPrincipalAsync(assignment.Principal, cancellationToken) is not null
            || await this.UncoveredScopeAsync(assignment.Scope, cancellationToken) is not null)
        {
            return GrantWriteResult.Of(GrantWriteOutcome.UnknownAssignment);
        }

        var result = await this.grants.RevokeAsync(assignmentId, cancellationToken);

        await this.RecordAsync(result, GrantAct.AssignmentRevoked, assignmentId, cancellationToken, assignment: assignment);

        return result;
    }

    /// <summary>Explains why one user holds each permission they hold within the caller's scope, and what each of their credentials keeps of it.</summary>
    /// <param name="user">The user.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The explanation, or <see langword="null" /> where no such user exists within the caller's scope.</returns>
    /// <remarks>
    /// The rows are read within the reach <see cref="ReadAssignmentsAsync" /> lists within, so covering the user is not
    /// enough to read what they hold through a group or at a scope the caller does not cover, and at most
    /// <see cref="UserGrantExplanation.MaximumSources" /> of them are answered, with the rest reported as left out.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller holds <see cref="MailFathomPermission.AdminRead" /> at no scope.</exception>
    public async Task<UserGrantExplanation?> ExplainUserGrantAsync(UserId user, CancellationToken cancellationToken)
    {
        this.authorization.RequirePermissionAtAnyScope(MailFathomPermission.AdminRead);

        RequireNamedUser(user);

        if (await this.grants.ReadUserPlacementAsync(user, cancellationToken) is not { } placement
            || !this.authorization.Covers(MailFathomPermission.AdminRead, placement.Target))
        {
            return null;
        }

        var sources = await this.grants.ReadGrantSourcesOfAsync(
            user,
            this.ReachOf(MailFathomPermission.AdminRead),
            UserGrantExplanation.MaximumSources + 1,
            cancellationToken);
        var held = await this.credentials.ReadForUserAsync(user, cancellationToken);

        return new UserGrantExplanation(
            user,
            [
                .. sources
                    .Take(UserGrantExplanation.MaximumSources)
                    .OrderBy(source => IndexOf(source.Permission))
                    .ThenBy(source => source.RoleName, StringComparer.Ordinal),
            ],
            sources.Count > UserGrantExplanation.MaximumSources,
            held);
    }

    /// <summary>Reports why a written role or group name cannot be recorded, or that it can.</summary>
    /// <param name="name">The name as it was written.</param>
    /// <returns>The sentence naming what to write instead, or <see langword="null" /> when it is accepted.</returns>
    /// <remarks>Roles and groups are bounded alike, so one rule answers for both.</remarks>
    public static string? FindNameRefusal(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "A role or a group is recorded with the name an operator reads it by. Write one.";
        }

        var trimmed = name.Trim();

        return trimmed.Length > Role.MaximumNameLength
            ? $"The name is {trimmed.Length} characters, past the {Role.MaximumNameLength} a role's or a group's name is stored as. Shorten it."
            : null;
    }

    private static string NameOrThrow(string? name, string parameterName) =>
        FindNameRefusal(name) is { } refusal
            ? throw new ArgumentException($"The name was not checked before it reached the use case. {refusal}", parameterName)
            : name!.Trim();

    private static void RequireNamedUser(UserId user)
    {
        if (!user.IsSpecified)
        {
            throw new ArgumentException("A membership and an explanation name a user.", nameof(user));
        }
    }

    private static int IndexOf(MailFathomPermission permission) =>
        MailFathomPermission.All.TakeWhile(published => published != permission).Count();

    private static AdministrativeTarget TargetOf(UserGroup group) => group.OrganizationId is { } organization
        ? AdministrativeTarget.OrganizationItself(organization)
        : AdministrativeTarget.Unplaced;

    private GrantListingReach ReachOf(MailFathomPermission permission) =>
        GrantListingReach.Within(this.authorization.ScopesOf(permission));

    private async Task<UserGroup?> CoveredGroupAsync(
        MailFathomPermission permission,
        Guid groupId,
        CancellationToken cancellationToken) =>
        await this.grants.ReadGroupAsync(groupId, cancellationToken) is { } group
        && this.authorization.Covers(permission, TargetOf(group))
            ? group
            : null;

    private async Task<UserPlacement?> CoveredUserAsync(UserId user, CancellationToken cancellationToken) =>
        await this.grants.ReadUserPlacementAsync(user, cancellationToken) is { } placement
        && this.authorization.Covers(MailFathomPermission.AdminRolesWrite, placement.Target)
            ? placement
            : null;

    /// <summary>Answers why an assignment may not name a principal, or that it may.</summary>
    /// <returns>The outcome naming the group or the user as unknown, or <see langword="null" /> when the caller's scope covers it.</returns>
    private async Task<GrantWriteOutcome?> UncoveredPrincipalAsync(
        AssignmentPrincipal principal,
        CancellationToken cancellationToken) => principal.Kind switch
        {
            AssignmentPrincipalKind.Group =>
                await this.CoveredGroupAsync(MailFathomPermission.AdminRolesWrite, principal.Id, cancellationToken) is null
                    ? GrantWriteOutcome.UnknownGroup
                    : null,
            _ => await this.CoveredUserAsync(UserId.Create(principal.Id), cancellationToken) is null
                ? GrantWriteOutcome.UnknownUser
                : null,
        };

    /// <summary>Answers why an assignment may not be made at a scope, or that it may.</summary>
    /// <returns>The outcome naming the organization or the user as unknown, or <see langword="null" /> when the caller's scope covers it.</returns>
    /// <remarks>The deployment scope is covered by the root alone, which the caller was already required to hold before this is asked of one.</remarks>
    private async Task<GrantWriteOutcome?> UncoveredScopeAsync(AssignmentScope scope, CancellationToken cancellationToken) =>
        scope.Kind switch
        {
            AssignmentScopeKind.Deployment =>
                this.authorization.Covers(MailFathomPermission.AdminRolesWrite, AdministrativeTarget.Unplaced)
                    ? null
                    : GrantWriteOutcome.UnknownAssignment,
            AssignmentScopeKind.Organization =>
                this.authorization.Covers(MailFathomPermission.AdminRolesWrite, AdministrativeTarget.OrganizationItself(scope.Target))
                    ? null
                    : GrantWriteOutcome.UnknownOrganization,
            _ => await this.CoveredUserAsync(UserId.Create(scope.Target), cancellationToken) is null
                ? GrantWriteOutcome.UnknownUser
                : null,
        };

    /// <summary>Requires that the caller holds every name a write would give, at a scope covering where it would give it.</summary>
    /// <param name="given">Each name the write gives, paired with the scope it gives it at.</param>
    /// <param name="cancellationToken">Cancels the reads placing a user scope.</param>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown naming the first name the caller does not hold, or holds only narrower than where it would be given.</exception>
    private async Task RequireHeldAsync(
        IEnumerable<(MailFathomPermission Permission, AssignmentScope Scope)> given,
        CancellationToken cancellationToken)
    {
        foreach (var (permission, scope) in given)
        {
            if (permission.IsDeploymentScopeOnly && scope.Kind != AssignmentScopeKind.Deployment)
            {
                continue;
            }

            var heldAt = this.authorization.ScopesOf(permission);

            if (heldAt.Count == 0)
            {
                throw PrincipalNotAuthorizedException.MissingPermission(permission);
            }

            if (permission.Surface != ProtectedSurface.Mail
                && !this.authorization.Covers(permission, await this.TargetOfAsync(scope, cancellationToken)))
            {
                throw PrincipalNotAuthorizedException.HeldTooNarrowly(permission);
            }
        }
    }

    /// <summary>Places a scope as a target, which for a user scope needs where the user belongs.</summary>
    /// <remarks>A user scope naming somebody since removed is placed as the deployment, the target only the widest grant covers, so a race with an erasure never relaxes the check.</remarks>
    private async Task<AdministrativeTarget> TargetOfAsync(AssignmentScope scope, CancellationToken cancellationToken) =>
        scope.Kind switch
        {
            AssignmentScopeKind.Deployment => AdministrativeTarget.Unplaced,
            AssignmentScopeKind.Organization => AdministrativeTarget.OrganizationItself(scope.Target),
            _ => (await this.grants.ReadUserPlacementAsync(UserId.Create(scope.Target), cancellationToken))?.Target
                ?? AdministrativeTarget.Unplaced,
        };

    /// <summary>Writes down an act that actually changed something, after it committed.</summary>
    private Task RecordAsync(
        GrantWriteResult result,
        GrantAct act,
        Guid recordId,
        CancellationToken cancellationToken,
        UserId? member = null,
        RoleAssignment? assignment = null,
        IReadOnlyList<MailFathomPermission>? permissions = null) =>
        result.Outcome == GrantWriteOutcome.Written
            ? this.auditor.RecordGrantChangeAsync(
                new GrantChange(
                    act,
                    recordId,

                    // Every act above required a permission, which only an admitted caller holds, so the identity is
                    // there; the fallback names the one principal that could reach a use case without one.
                    this.authorization.PrincipalIdentity ?? AuthorizedPrincipal.ProcessIdentityName,
                    this.timeProvider.GetUtcNow())
                {
                    Member = member,
                    Assignment = assignment,
                    Permissions = permissions,
                },
                cancellationToken)
            : Task.CompletedTask;
}
