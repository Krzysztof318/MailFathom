// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access.Grants;
using MailFathom.Application.Paging;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MailFathom.Infrastructure.Persistence.Grants;

/// <summary>Keeps the roles, groups, and role assignments a user's grant is computed from.</summary>
/// <remarks>
/// <para>
/// Every write is a single statement or one short transaction executed against the database, for the reason
/// <see cref="Users.PersistedOrganizations" /> gives. Each refusal is decided by a constraint at the moment of the
/// write — a name's unique index, the assignment's unique index, a foreign key naming what does not exist — and a
/// read afterwards only says which one it was.
/// </para>
/// <para>
/// A deletion that is refused while something still stands on the row locks the row before it counts, and the count
/// and the delete commit together. An assignment or a membership being written beside it checks its foreign key,
/// which waits on that lock, so the number a refusal names is the number that refused it and a deletion that goes
/// ahead leaves the concurrent write to meet a missing row rather than to dangle.
/// </para>
/// <para>
/// A write that can take the root away — revoking an assignment, ending a membership, replacing a role's list — locks
/// every role listing <see cref="MailFathomPermission.AdminRolesWrite" />, in identifier order, before it reads whether
/// a root is held, and reads it again after its own change and before it commits. Two such writes therefore commit one
/// after the other, and the second reads what the first left, so removing two roots at once leaves the second refused
/// rather than the deployment with none. Only a write that found a root and would leave none is refused, so a
/// deployment that already has no root — its database edited by hand — is not frozen by the rule.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class PersistedGrants(MailFathomDbContext dbContext, IGrantChangeAnnouncer changes) : IGrantStore
{
    /// <inheritdoc />
    public async Task<Role?> ReadRoleAsync(Guid roleId, CancellationToken cancellationToken)
    {
        var role = await dbContext.Roles
            .AsNoTracking()
            .Where(candidate => candidate.Id == roleId)
            .Select(candidate => new { candidate.Id, candidate.Name, candidate.CreatedAt })
            .FirstOrDefaultAsync(cancellationToken);

        if (role is null)
        {
            return null;
        }

        var listed = await dbContext.RolePermissions
            .AsNoTracking()
            .Where(permission => permission.RoleId == roleId)
            .Select(permission => permission.Permission)
            .ToArrayAsync(cancellationToken);

        return new Role(role.Id, role.Name, RolePermissions.Read(listed), role.CreatedAt);
    }

    /// <inheritdoc />
    public async Task<AdministrativeListingPage<Role>> ReadRolesAsync(
        AdministrativeListingQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var roles = dbContext.Roles.AsNoTracking();

        if (query.After is { } after)
        {
            roles = roles.Where(role => role.Id > after);
        }

        var page = query.PageOf(
            await roles
                .OrderBy(role => role.Id)
                .Take(query.PageSize + 1)
                .Select(role => new { role.Id, role.Name, role.CreatedAt })
                .ToArrayAsync(cancellationToken),
            role => role.Id);

        var roleIds = page.Entries.Select(role => role.Id).ToArray();

        var listed = (await dbContext.RolePermissions
                .AsNoTracking()
                .Where(permission => roleIds.Contains(permission.RoleId))
                .Select(permission => new { permission.RoleId, permission.Permission })
                .ToArrayAsync(cancellationToken))
            .ToLookup(permission => permission.RoleId, permission => permission.Permission);

        return new AdministrativeListingPage<Role>(
            [.. page.Entries.Select(role => new Role(role.Id, role.Name, RolePermissions.Read(listed[role.Id]), role.CreatedAt))],
            page.ContinuesAfter);
    }

    /// <inheritdoc />
    public async Task<GrantWriteResult> CreateRoleAsync(
        Guid roleId,
        string name,
        RolePermissions permissions,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(permissions);

        await using var creation = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // The identifier is freshly minted, so the name's index is the one constraint a loser can meet.
        var written = await dbContext.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO roles ("Id", "Name", "CreatedAt")
             VALUES ({roleId}, {name}, {createdAt})
             ON CONFLICT DO NOTHING
             """,
            cancellationToken);

        if (written != 1)
        {
            return GrantWriteResult.Of(GrantWriteOutcome.NameTaken);
        }

        await this.InsertPermissionsAsync(roleId, permissions, cancellationToken);

        await creation.CommitAsync(cancellationToken);

        return GrantWriteResult.Of(GrantWriteOutcome.Written);
    }

    /// <inheritdoc />
    public async Task<GrantWriteResult> RenameRoleAsync(Guid roleId, string name, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        try
        {
            var written = await dbContext.Roles
                .Where(role => role.Id == roleId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(role => role.Name, name), cancellationToken);

            return GrantWriteResult.Of(written == 1 ? GrantWriteOutcome.Written : GrantWriteOutcome.UnknownRole);
        }
        catch (PostgresException violation) when (violation.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return GrantWriteResult.Of(GrantWriteOutcome.NameTaken);
        }
    }

    /// <inheritdoc />
    /// <remarks>The role row is locked first, together with every role giving the root, so two replacements of one list commit one after the other and the list is always exactly one writer's.</remarks>
    public async Task<GrantWriteResult> ReplaceRolePermissionsAsync(
        Guid roleId,
        RolePermissions permissions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        await using var replacement = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        await this.LockRootRolesAsync(roleId, cancellationToken);

        if (!await dbContext.Roles.AnyAsync(role => role.Id == roleId, cancellationToken))
        {
            return GrantWriteResult.Of(GrantWriteOutcome.UnknownRole);
        }

        var rootHeld = await this.IsRootHeldAsync(cancellationToken);

        await dbContext.RolePermissions
            .Where(permission => permission.RoleId == roleId)
            .ExecuteDeleteAsync(cancellationToken);

        await this.InsertPermissionsAsync(roleId, permissions, cancellationToken);

        if (rootHeld && !await this.IsRootHeldAsync(cancellationToken))
        {
            return GrantWriteResult.Of(GrantWriteOutcome.LastRoot);
        }

        await replacement.CommitAsync(cancellationToken);

        return await this.AnnouncedAsync(GrantWriteResult.Of(GrantWriteOutcome.Written));
    }

    /// <inheritdoc />
    public async Task<GrantWriteResult> DeleteRoleAsync(Guid roleId, CancellationToken cancellationToken)
    {
        await using var removal = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        await dbContext.Database.ExecuteSqlAsync(
            $"""SELECT 1 FROM roles WHERE "Id" = {roleId} FOR UPDATE""",
            cancellationToken);

        if (!await dbContext.Roles.AnyAsync(role => role.Id == roleId, cancellationToken))
        {
            return GrantWriteResult.Of(GrantWriteOutcome.UnknownRole);
        }

        var standing = await dbContext.RoleAssignments
            .CountAsync(assignment => assignment.RoleId == roleId, cancellationToken);

        if (standing > 0)
        {
            return new GrantWriteResult(GrantWriteOutcome.StillAssigned, standing);
        }

        await dbContext.Roles.Where(role => role.Id == roleId).ExecuteDeleteAsync(cancellationToken);

        await removal.CommitAsync(cancellationToken);

        return GrantWriteResult.Of(GrantWriteOutcome.Written);
    }

    /// <inheritdoc />
    public Task<UserGroup?> ReadGroupAsync(Guid groupId, CancellationToken cancellationToken) =>
        dbContext.UserGroups
            .AsNoTracking()
            .Where(group => group.Id == groupId)
            .Select(group => new UserGroup(
                group.Id,
                group.Name,
                group.OrganizationId,
                dbContext.UserGroupMembers.Count(member => member.GroupId == group.Id),
                group.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    /// <remarks>A user scope covers no group, so a reach built from organizations and users answers with the organizations' groups alone.</remarks>
    public async Task<AdministrativeListingPage<UserGroup>> ReadGroupsAsync(
        AdministrativeListingQuery query,
        GrantListingReach reach,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(reach);

        var groups = dbContext.UserGroups.AsNoTracking();

        if (!reach.WholeDeployment)
        {
            Guid[] organizations = [.. reach.Organizations];

            groups = groups.Where(group =>
                group.OrganizationId != null && organizations.Contains(group.OrganizationId.Value));
        }

        if (query.After is { } after)
        {
            groups = groups.Where(group => group.Id > after);
        }

        var stored = await groups
            .OrderBy(group => group.Id)
            .Take(query.PageSize + 1)
            .Select(group => new UserGroup(
                group.Id,
                group.Name,
                group.OrganizationId,
                dbContext.UserGroupMembers.Count(member => member.GroupId == group.Id),
                group.CreatedAt))
            .ToArrayAsync(cancellationToken);

        return query.PageOf(stored, group => group.Id);
    }

    /// <inheritdoc />
    public async Task<AdministrativeListingPage<UserId>> ReadGroupMembersAsync(
        Guid groupId,
        AdministrativeListingQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var members = dbContext.UserGroupMembers.AsNoTracking().Where(member => member.GroupId == groupId);

        if (query.After is { } after)
        {
            members = members.Where(member => member.UserId > after);
        }

        var page = query.PageOf(
            await members
                .OrderBy(member => member.UserId)
                .Take(query.PageSize + 1)
                .Select(member => member.UserId)
                .ToArrayAsync(cancellationToken),
            userId => userId);

        return new AdministrativeListingPage<UserId>([.. page.Entries.Select(UserId.Create)], page.ContinuesAfter);
    }

    /// <inheritdoc />
    public async Task<GrantWriteResult> CreateGroupAsync(
        Guid groupId,
        string name,
        Guid? organizationId,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var inOrganization = organizationId is not null;
        var organization = organizationId ?? Guid.Empty;

        try
        {
            // A group in no organization stores a null rather than the empty identifier, which no organization carries
            // and the foreign key would refuse.
            var written = await dbContext.Database.ExecuteSqlAsync(
                $"""
                 INSERT INTO user_groups ("Id", "Name", "OrganizationId", "CreatedAt")
                 VALUES ({groupId}, {name}, CASE WHEN {inOrganization} THEN {organization} END, {createdAt})
                 ON CONFLICT DO NOTHING
                 """,
                cancellationToken);

            return GrantWriteResult.Of(written == 1 ? GrantWriteOutcome.Written : GrantWriteOutcome.NameTaken);
        }
        catch (PostgresException violation) when (violation.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            return GrantWriteResult.Of(GrantWriteOutcome.UnknownOrganization);
        }
    }

    /// <inheritdoc />
    public async Task<GrantWriteResult> RenameGroupAsync(Guid groupId, string name, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        try
        {
            var written = await dbContext.UserGroups
                .Where(group => group.Id == groupId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(group => group.Name, name), cancellationToken);

            return GrantWriteResult.Of(written == 1 ? GrantWriteOutcome.Written : GrantWriteOutcome.UnknownGroup);
        }
        catch (PostgresException violation) when (violation.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return GrantWriteResult.Of(GrantWriteOutcome.NameTaken);
        }
    }

    /// <inheritdoc />
    public async Task<GrantWriteResult> DeleteGroupAsync(Guid groupId, CancellationToken cancellationToken)
    {
        await using var removal = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        await dbContext.Database.ExecuteSqlAsync(
            $"""SELECT 1 FROM user_groups WHERE "Id" = {groupId} FOR UPDATE""",
            cancellationToken);

        if (!await dbContext.UserGroups.AnyAsync(group => group.Id == groupId, cancellationToken))
        {
            return GrantWriteResult.Of(GrantWriteOutcome.UnknownGroup);
        }

        var standing = await dbContext.RoleAssignments
            .CountAsync(assignment => assignment.PrincipalGroupId == groupId, cancellationToken);

        if (standing > 0)
        {
            return new GrantWriteResult(GrantWriteOutcome.StillAssigned, standing);
        }

        await dbContext.UserGroups.Where(group => group.Id == groupId).ExecuteDeleteAsync(cancellationToken);

        await removal.CommitAsync(cancellationToken);

        return GrantWriteResult.Of(GrantWriteOutcome.Written);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The group and the user are both share-locked before their organizations are compared, which is the lock a move
    /// of the user between organizations conflicts with — so the user cannot change organization between the comparison
    /// and the insert, and a membership never lands in a group of an organization its member has just left.
    /// </remarks>
    public async Task<GrantWriteResult> AddGroupMemberAsync(
        Guid groupId,
        UserId user,
        DateTimeOffset addedAt,
        CancellationToken cancellationToken)
    {
        var userId = RequireUser(user);

        await using var joining = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        await dbContext.Database.ExecuteSqlAsync(
            $"""SELECT 1 FROM user_groups WHERE "Id" = {groupId} FOR SHARE""",
            cancellationToken);
        await dbContext.Database.ExecuteSqlAsync(
            $"""SELECT 1 FROM settings_accounts WHERE "Id" = {userId} FOR SHARE""",
            cancellationToken);

        var group = await dbContext.UserGroups
            .Where(candidate => candidate.Id == groupId)
            .Select(candidate => new { candidate.OrganizationId })
            .FirstOrDefaultAsync(cancellationToken);

        if (group is null)
        {
            return GrantWriteResult.Of(GrantWriteOutcome.UnknownGroup);
        }

        var member = await dbContext.UserAccounts
            .Where(candidate => candidate.Id == userId)
            .Select(candidate => new { candidate.OrganizationId })
            .FirstOrDefaultAsync(cancellationToken);

        if (member is null)
        {
            return GrantWriteResult.Of(GrantWriteOutcome.UnknownUser);
        }

        if (!AdmitsMember(group.OrganizationId, member.OrganizationId))
        {
            return GrantWriteResult.Of(GrantWriteOutcome.OutsideGroupOrganization);
        }

        var joined = await dbContext.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO user_group_members ("GroupId", "UserId", "AddedAt")
             VALUES ({groupId}, {userId}, {addedAt})
             ON CONFLICT DO NOTHING
             """,
            cancellationToken);

        await joining.CommitAsync(cancellationToken);

        return joined == 1
            ? await this.AnnouncedAsync(GrantWriteResult.Of(GrantWriteOutcome.Written))
            : GrantWriteResult.Of(GrantWriteOutcome.Unchanged);
    }

    /// <inheritdoc />
    public async Task<GrantWriteResult> RemoveGroupMemberAsync(
        Guid groupId,
        UserId user,
        CancellationToken cancellationToken)
    {
        var userId = RequireUser(user);

        await using var leaving = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        await this.LockRootRolesAsync(alsoRoleId: null, cancellationToken);

        var rootHeld = await this.IsRootHeldAsync(cancellationToken);

        var left = await dbContext.UserGroupMembers
            .Where(member => member.GroupId == groupId && member.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        if (left == 0)
        {
            return GrantWriteResult.Of(GrantWriteOutcome.Unchanged);
        }

        if (rootHeld && !await this.IsRootHeldAsync(cancellationToken))
        {
            return GrantWriteResult.Of(GrantWriteOutcome.LastRoot);
        }

        await leaving.CommitAsync(cancellationToken);

        return await this.AnnouncedAsync(GrantWriteResult.Of(GrantWriteOutcome.Written));
    }

    /// <inheritdoc />
    public async Task<ScopedGrant> ReadGrantOfGroupAsync(Guid groupId, CancellationToken cancellationToken)
    {
        var given = await dbContext.RoleAssignments
            .AsNoTracking()
            .Where(assignment => assignment.PrincipalGroupId == groupId)
            .Join(
                dbContext.RolePermissions,
                assignment => assignment.RoleId,
                listed => listed.RoleId,
                (assignment, listed) => new { listed.Permission, assignment.ScopeOrganizationId, assignment.ScopeUserId })
            .Distinct()
            .ToArrayAsync(cancellationToken);

        return ScopedGrant.Of(given.Select(row => (
            MailFathomPermission.TryParse(row.Permission, out var permission) ? permission : default,
            ScopeOf(row.ScopeOrganizationId, row.ScopeUserId))));
    }

    /// <inheritdoc />
    public async Task<RoleAssignment?> ReadAssignmentAsync(Guid assignmentId, CancellationToken cancellationToken) =>
        await dbContext.RoleAssignments
            .AsNoTracking()
            .FirstOrDefaultAsync(assignment => assignment.Id == assignmentId, cancellationToken) is { } stored
            ? AssignmentOf(stored)
            : null;

    /// <inheritdoc />
    public async Task<AdministrativeListingPage<RoleAssignment>> ReadAssignmentsAsync(
        AdministrativeListingQuery query,
        GrantListingReach reach,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(reach);

        var assignments = this.Within(dbContext.RoleAssignments.AsNoTracking(), reach);

        if (query.After is { } after)
        {
            assignments = assignments.Where(assignment => assignment.Id > after);
        }

        var page = query.PageOf(
            await assignments
                .OrderBy(assignment => assignment.Id)
                .Take(query.PageSize + 1)
                .ToArrayAsync(cancellationToken),
            assignment => assignment.Id);

        return new AdministrativeListingPage<RoleAssignment>(
            [.. page.Entries.Select(AssignmentOf)],
            page.ContinuesAfter);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The principal and the scope are written as a value with flags choosing its column rather than as nullable
    /// parameters, so each unused column is a typed null the statement composes rather than an untyped one a driver
    /// has to guess at.
    /// </remarks>
    public async Task<GrantWriteResult> AssignAsync(
        Guid assignmentId,
        Guid roleId,
        AssignmentPrincipal principal,
        AssignmentScope scope,
        DateTimeOffset assignedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(scope);

        var principalId = principal.Id;
        var toUser = principal.Kind == AssignmentPrincipalKind.User;
        var scopeTarget = scope.Target;
        var atOrganization = scope.Kind == AssignmentScopeKind.Organization;
        var atUser = scope.Kind == AssignmentScopeKind.User;

        try
        {
            // The identifier is freshly minted, so the assignment's own unique index is the one constraint a loser can meet.
            var written = await dbContext.Database.ExecuteSqlAsync(
                $"""
                 INSERT INTO role_assignments
                     ("Id", "RoleId", "PrincipalUserId", "PrincipalGroupId", "ScopeOrganizationId", "ScopeUserId", "AssignedAt")
                 VALUES ({assignmentId}, {roleId},
                         CASE WHEN {toUser} THEN {principalId} END,
                         CASE WHEN NOT {toUser} THEN {principalId} END,
                         CASE WHEN {atOrganization} THEN {scopeTarget} END,
                         CASE WHEN {atUser} THEN {scopeTarget} END,
                         {assignedAt})
                 ON CONFLICT DO NOTHING
                 """,
                cancellationToken);

            return await this.AnnouncedAsync(
                GrantWriteResult.Of(written == 1 ? GrantWriteOutcome.Written : GrantWriteOutcome.AlreadyAssigned));
        }
        catch (PostgresException violation) when (violation.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            return GrantWriteResult.Of(
                MissingReferenceOf(violation.ConstraintName)
                ?? throw new InvalidOperationException(
                    "An assignment was refused by a foreign key it does not declare.",
                    violation));
        }
    }

    /// <inheritdoc />
    public async Task<GrantWriteResult> RevokeAsync(Guid assignmentId, CancellationToken cancellationToken)
    {
        await using var revocation = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        await this.LockRootRolesAsync(alsoRoleId: null, cancellationToken);

        var rootHeld = await this.IsRootHeldAsync(cancellationToken);

        var removed = await dbContext.RoleAssignments
            .Where(assignment => assignment.Id == assignmentId)
            .ExecuteDeleteAsync(cancellationToken);

        if (removed != 1)
        {
            return GrantWriteResult.Of(GrantWriteOutcome.UnknownAssignment);
        }

        if (rootHeld && !await this.IsRootHeldAsync(cancellationToken))
        {
            return GrantWriteResult.Of(GrantWriteOutcome.LastRoot);
        }

        await revocation.CommitAsync(cancellationToken);

        return await this.AnnouncedAsync(GrantWriteResult.Of(GrantWriteOutcome.Written));
    }

    /// <inheritdoc />
    /// <remarks>
    /// One statement over the user's own assignments and their groups' assignments, joined to the names each role
    /// lists. A name listed through several assignments at one scope comes back once, and what makes the result a
    /// union rather than a list is <see cref="ScopedGrant.Of" />, which also drops a stored name this build does not
    /// publish. Nothing pages it, because what it returns is bounded by the published set times the scopes the user
    /// was given rather than by anything a request names.
    /// </remarks>
    public async Task<ScopedGrant> ReadGrantOfAsync(UserId user, CancellationToken cancellationToken)
    {
        var held = await this.AssignmentsHeldBy(RequireUser(user))
            .Join(
                dbContext.RolePermissions,
                assignment => assignment.RoleId,
                listed => listed.RoleId,
                (assignment, listed) => new { listed.Permission, assignment.ScopeOrganizationId, assignment.ScopeUserId })
            .Distinct()
            .ToArrayAsync(cancellationToken);

        return ScopedGrant.Of(held.Select(row => (
            MailFathomPermission.TryParse(row.Permission, out var permission) ? permission : default,
            ScopeOf(row.ScopeOrganizationId, row.ScopeUserId))));
    }

    /// <inheritdoc />
    /// <remarks>Nothing pages it, for the reason nothing pages <see cref="ReadGrantOfAsync" />: what it returns is bounded by the assignments an administrator wrote for this one user and their groups, not by anything a request names.</remarks>
    public async Task<IReadOnlyList<HeldRole>> ReadRolesHeldByAsync(UserId user, CancellationToken cancellationToken)
    {
        var held = await this.AssignmentsHeldBy(RequireUser(user))
            .Join(
                dbContext.Roles,
                assignment => assignment.RoleId,
                role => role.Id,
                (assignment, role) => new { role.Name, assignment.ScopeOrganizationId, assignment.ScopeUserId })
            .Distinct()
            .OrderBy(row => row.Name)
            .ThenBy(row => row.ScopeOrganizationId)
            .ThenBy(row => row.ScopeUserId)
            .ToArrayAsync(cancellationToken);

        return [.. held.Select(row => new HeldRole(row.Name, ScopeOf(row.ScopeOrganizationId, row.ScopeUserId)))];
    }

    /// <inheritdoc />
    /// <remarks>
    /// One statement over the assignments <see cref="ReadGrantOfAsync" /> reads, narrowed by the reach
    /// <see cref="ReadAssignmentsAsync" /> lists within, so the membership rule and the reach are each the one rule
    /// rather than a second copy of it. A stored name this build does not publish is left out by the statement rather
    /// than after it, so the limit counts rows that explain something.
    /// </remarks>
    public async Task<IReadOnlyList<GrantSource>> ReadGrantSourcesOfAsync(
        UserId user,
        GrantListingReach reach,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reach);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        string[] published = [.. MailFathomPermission.All.Select(permission => permission.Name)];

        var rows = await (
                from assignment in this.Within(this.AssignmentsHeldBy(RequireUser(user)), reach)
                join role in dbContext.Roles on assignment.RoleId equals role.Id
                join listed in dbContext.RolePermissions on assignment.RoleId equals listed.RoleId
                where published.Contains(listed.Permission)
                join userGroup in dbContext.UserGroups on assignment.PrincipalGroupId equals (Guid?)userGroup.Id into named
                from userGroup in named.DefaultIfEmpty()
                orderby assignment.Id, listed.Permission
                select new GrantSourceRow(
                    listed.Permission,
                    role.Id,
                    role.Name,
                    assignment.Id,
                    assignment.PrincipalGroupId,
                    userGroup == null ? null : userGroup.Name,
                    assignment.ScopeOrganizationId,
                    assignment.ScopeUserId))
            .Take(limit)
            .ToArrayAsync(cancellationToken);

        return [.. rows.Select(SourceOf).OfType<GrantSource>()];
    }

    /// <inheritdoc />
    public async Task<UserPlacement?> ReadUserPlacementAsync(UserId user, CancellationToken cancellationToken)
    {
        var userId = RequireUser(user);

        var account = await dbContext.UserAccounts
            .AsNoTracking()
            .Where(candidate => candidate.Id == userId)
            .Select(candidate => new { candidate.OrganizationId })
            .FirstOrDefaultAsync(cancellationToken);

        return account is null ? null : new UserPlacement(user, account.OrganizationId);
    }

    /// <summary>Reads one stored row of a grant's explanation as the source it names.</summary>
    /// <param name="row">The stored row.</param>
    /// <returns>The source, or <see langword="null" /> for a stored name this build does not publish, which grants nothing and so explains nothing.</returns>
    internal static GrantSource? SourceOf(GrantSourceRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return MailFathomPermission.TryParse(row.Permission, out var permission)
            ? new GrantSource(
                permission,
                row.RoleId,
                row.RoleName,
                row.AssignmentId,
                row.GroupId,
                row.GroupName,
                ScopeOf(row.ScopeOrganizationId, row.ScopeUserId))
            : null;
    }

    /// <summary>Names what an assignment named that does not exist, from the foreign key that refused it.</summary>
    /// <param name="constraintName">The constraint PostgreSQL reported.</param>
    /// <returns>The outcome naming the missing role, group, user, or organization; <see langword="null" /> for a constraint no assignment declares, which is a defect the caller raises with the violation attached rather than an answer.</returns>
    internal static GrantWriteOutcome? MissingReferenceOf(string? constraintName) => constraintName switch
    {
        PersistenceConstraintNames.RoleAssignmentRoleForeignKeyName => GrantWriteOutcome.UnknownRole,
        PersistenceConstraintNames.RoleAssignmentPrincipalGroupForeignKeyName => GrantWriteOutcome.UnknownGroup,
        PersistenceConstraintNames.RoleAssignmentPrincipalUserForeignKeyName
            or PersistenceConstraintNames.RoleAssignmentScopeUserForeignKeyName => GrantWriteOutcome.UnknownUser,
        PersistenceConstraintNames.RoleAssignmentScopeOrganizationForeignKeyName => GrantWriteOutcome.UnknownOrganization,
        _ => null,
    };

    /// <summary>Decides whether a group may take a user as a member, by the organization each belongs to.</summary>
    /// <param name="groupOrganization">The group's organization, or <see langword="null" /> for a group in none.</param>
    /// <param name="memberOrganization">The user's organization, or <see langword="null" /> for somebody in none.</param>
    /// <returns><see langword="true" /> when the group is in no organization, which is the deployment's and holds anybody, or when both name the same one.</returns>
    internal static bool AdmitsMember(Guid? groupOrganization, Guid? memberOrganization) =>
        groupOrganization is not { } organization || organization == memberOrganization;

    /// <summary>Reads one stored assignment as the principal and the scope its columns name.</summary>
    /// <param name="assignment">The stored row.</param>
    /// <returns>The assignment.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="assignment" /> is <see langword="null" />.</exception>
    /// <remarks>The check constraints guarantee exactly one principal column and at most one scope column, so the columns are read in that order without a second rule beside them.</remarks>
    internal static RoleAssignment AssignmentOf(RoleAssignmentEntity assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);

        var principal = assignment.PrincipalUserId is { } principalUser
            ? AssignmentPrincipal.User(UserId.Create(principalUser))
            : AssignmentPrincipal.Group(assignment.PrincipalGroupId.GetValueOrDefault());

        return new RoleAssignment(
            assignment.Id,
            assignment.RoleId,
            principal,
            ScopeOf(assignment.ScopeOrganizationId, assignment.ScopeUserId),
            assignment.AssignedAt);
    }

    /// <summary>Reads the scope an assignment's two scope columns name.</summary>
    /// <param name="organizationId">The organization column.</param>
    /// <param name="userId">The user column.</param>
    /// <returns>The organization or the user named, or the deployment where neither is.</returns>
    internal static AssignmentScope ScopeOf(Guid? organizationId, Guid? userId) => (organizationId, userId) switch
    {
        ({ } organization, _) => AssignmentScope.Organization(organization),
        (_, { } scopeUser) => AssignmentScope.User(UserId.Create(scopeUser)),
        _ => AssignmentScope.Deployment,
    };

    /// <summary>Composes the assignments naming one user, directly or through a group that still covers them.</summary>
    /// <remarks>
    /// The membership rule <see cref="AdmitsMember" /> applies when a member is added, read again against the user's
    /// organization now: moving a user leaves their memberships of the old organization's groups behind, and those must
    /// give nothing once nobody in that organization covers the user any more.
    /// </remarks>
    private IQueryable<RoleAssignmentEntity> AssignmentsHeldBy(Guid userId)
    {
        var groupsOfUser =
            from member in dbContext.UserGroupMembers
            where member.UserId == userId
            join userGroup in dbContext.UserGroups on member.GroupId equals userGroup.Id
            join account in dbContext.UserAccounts on member.UserId equals account.Id
            where userGroup.OrganizationId == null || userGroup.OrganizationId == account.OrganizationId
            select userGroup.Id;

        return dbContext.RoleAssignments
            .AsNoTracking()
            .Where(assignment => assignment.PrincipalUserId == userId
                || (assignment.PrincipalGroupId != null && groupsOfUser.Contains(assignment.PrincipalGroupId.Value)));
    }

    /// <summary>Narrows assignments to the ones inside a reach.</summary>
    /// <remarks>
    /// An assignment is inside a reach when both its principal and its scope are: a user the reach names or one belonging
    /// to one of its organizations, a group of one of its organizations, and a scope naming one of those organizations or
    /// one of those users. The deployment scope is inside the whole deployment's reach alone.
    /// </remarks>
    private IQueryable<RoleAssignmentEntity> Within(IQueryable<RoleAssignmentEntity> assignments, GrantListingReach reach)
    {
        if (reach.WholeDeployment)
        {
            return assignments;
        }

        Guid[] organizations = [.. reach.Organizations];
        Guid[] users = [.. reach.Users];

        var members = dbContext.UserAccounts
            .Where(account => account.OrganizationId != null && organizations.Contains(account.OrganizationId.Value))
            .Select(account => account.Id);

        var groups = dbContext.UserGroups
            .Where(group => group.OrganizationId != null && organizations.Contains(group.OrganizationId.Value))
            .Select(group => group.Id);

        return assignments.Where(assignment =>
            ((assignment.PrincipalUserId != null
                    && (users.Contains(assignment.PrincipalUserId.Value)
                        || members.Contains(assignment.PrincipalUserId.Value)))
                || (assignment.PrincipalGroupId != null && groups.Contains(assignment.PrincipalGroupId.Value)))
            && ((assignment.ScopeOrganizationId != null
                    && organizations.Contains(assignment.ScopeOrganizationId.Value))
                || (assignment.ScopeUserId != null
                    && (users.Contains(assignment.ScopeUserId.Value)
                        || members.Contains(assignment.ScopeUserId.Value)))));
    }

    private static Guid RequireUser(UserId user) => user.IsSpecified
        ? user.Value
        : throw new ArgumentException("A membership names a user.", nameof(user));

    /// <summary>Locks every role giving the root, and one more role where a write is about to change it, in identifier order.</summary>
    /// <param name="alsoRoleId">A role the write changes whether or not it gives the root, or <see langword="null" /> for none.</param>
    /// <param name="cancellationToken">Cancels the lock.</param>
    /// <remarks>One statement taking every lock in one order, so two writes taking them cannot wait on each other in a cycle.</remarks>
    private async Task LockRootRolesAsync(Guid? alsoRoleId, CancellationToken cancellationToken)
    {
        var root = MailFathomPermission.AdminRolesWrite.Name;
        var also = alsoRoleId ?? Guid.Empty;

        await dbContext.Database.ExecuteSqlAsync(
            $"""
             SELECT 1 FROM roles
             WHERE "Id" = {also} OR "Id" IN (SELECT "RoleId" FROM role_permissions WHERE "Permission" = {root})
             ORDER BY "Id"
             FOR UPDATE
             """,
            cancellationToken);
    }

    /// <summary>Reads whether anybody holds the root: a role listing it assigned at the deployment scope to a user, or to a group with a member it still admits.</summary>
    private Task<bool> IsRootHeldAsync(CancellationToken cancellationToken)
    {
        var root = MailFathomPermission.AdminRolesWrite.Name;

        var rootRoles = dbContext.RolePermissions
            .Where(listed => listed.Permission == root)
            .Select(listed => listed.RoleId);

        var admittingGroups =
            from member in dbContext.UserGroupMembers
            join userGroup in dbContext.UserGroups on member.GroupId equals userGroup.Id
            join account in dbContext.UserAccounts on member.UserId equals account.Id
            where userGroup.OrganizationId == null || userGroup.OrganizationId == account.OrganizationId
            select userGroup.Id;

        return dbContext.RoleAssignments.AnyAsync(
            assignment => assignment.ScopeOrganizationId == null
                && assignment.ScopeUserId == null
                && rootRoles.Contains(assignment.RoleId)
                && (assignment.PrincipalUserId != null
                    || (assignment.PrincipalGroupId != null && admittingGroups.Contains(assignment.PrincipalGroupId.Value))),
            cancellationToken);
    }

    /// <summary>Announces a write that may have changed somebody's grant, once it committed.</summary>
    /// <remarks>Only a write that went through is announced: a refused one changed nothing for anybody to forget.</remarks>
    private async Task<GrantWriteResult> AnnouncedAsync(GrantWriteResult result)
    {
        if (result.Outcome == GrantWriteOutcome.Written)
        {
            await changes.AnnounceAsync();
        }

        return result;
    }

    private async Task InsertPermissionsAsync(
        Guid roleId,
        RolePermissions permissions,
        CancellationToken cancellationToken)
    {
        string[] names = [.. permissions.Granted.Select(permission => permission.Name)];

        await dbContext.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO role_permissions ("RoleId", "Permission")
             SELECT {roleId}, name FROM unnest({names}) AS name
             """,
            cancellationToken);
    }
}
