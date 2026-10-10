// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Access.Grants;
using MailFathom.Application.Paging;
using MailFathom.Domain.Access;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Defines roles, keeps groups and their members, gives and revokes roles at a scope, and explains a user's grant.</summary>
/// <remarks>
/// <para>
/// Reading is <see cref="MailFathomPermission.AdminRead" /> and writing <see cref="MailFathomPermission.AdminRolesWrite" />.
/// Defining a role is the deployment's alone, so those routes admit only a caller holding the writing grant over the
/// whole deployment; every other route names a group, a user, or a scope that only <see cref="GrantAdministration" /> can
/// place, so the transport lets through a caller holding the permission anywhere and the use case answers a target
/// outside the caller's scope as one that does not exist.
/// </para>
/// <para>
/// A refusal for want of a name the write would give arrives as a <c>403</c> naming that name in the member the
/// transport's own refusal names it in, because the remedy is the same: the caller needs it, or needs it wider. Its
/// sentence says which of the two — not granted, or held at a scope narrower than the write gives it at. A write giving
/// a role that lists a pattern is refused the same way below the root, naming the root and saying why it is asked.
/// </para>
/// </remarks>
internal static class GrantEndpoints
{
    /// <summary>The route roles are listed and recorded at, relative to the administrative prefix.</summary>
    internal const string RolesRoute = "/roles";

    /// <summary>The route one role is removed at.</summary>
    internal const string RoleRoute = "/roles/{roleId:guid}";

    /// <summary>The route one role's name is replaced at.</summary>
    internal const string RoleNameRoute = $"{RoleRoute}/name";

    /// <summary>The route the whole list of permissions one role grants is replaced at.</summary>
    internal const string RolePermissionsRoute = $"{RoleRoute}/permissions";

    /// <summary>The route groups are listed and recorded at.</summary>
    internal const string GroupsRoute = "/groups";

    /// <summary>The route one group is removed at.</summary>
    internal const string GroupRoute = "/groups/{groupId:guid}";

    /// <summary>The route one group's name is replaced at.</summary>
    internal const string GroupNameRoute = $"{GroupRoute}/name";

    /// <summary>The route one group's members are listed at.</summary>
    internal const string GroupMembersRoute = $"{GroupRoute}/members";

    /// <summary>The route one user joins and leaves one group at.</summary>
    internal const string GroupMemberRoute = $"{GroupMembersRoute}/{{userId:guid}}";

    /// <summary>The route role assignments are listed and made at.</summary>
    /// <remarks>Named apart from a mail account's assignments to users, which are a different record answering a different question.</remarks>
    internal const string RoleAssignmentsRoute = "/role-assignments";

    /// <summary>The route one role assignment is revoked at.</summary>
    internal const string RoleAssignmentRoute = "/role-assignments/{assignmentId:guid}";

    /// <summary>The route one user's grant is explained at.</summary>
    internal const string UserPermissionsRoute = "/users/{userId:guid}/permissions";

    /// <summary>The greatest request body the write routes read before refusing it.</summary>
    /// <remarks>The largest body here is a role's list, which every published name fits inside many times over.</remarks>
    internal const int MaxRequestBytes = 16 * 1024;

    private const string NoSuchGroup = "This deployment holds no such group within your scope.";

    /// <summary>Maps the role, group, and assignment routes into the administrative group, so they inherit its authorization.</summary>
    /// <param name="api">The administrative route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapGrants(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(RolesRoute, ListRolesAsync)
            .RequirePermissionOverTarget(MailFathomPermission.AdminRead);

        api.MapPost(RolesRoute, CreateRoleAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            .RequirePermission(MailFathomPermission.AdminRolesWrite);

        api.MapPut(RoleNameRoute, RenameRoleAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            .RequirePermission(MailFathomPermission.AdminRolesWrite);

        api.MapPut(RolePermissionsRoute, ReplaceRolePermissionsAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            .RequirePermission(MailFathomPermission.AdminRolesWrite);

        api.MapDelete(RoleRoute, DeleteRoleAsync)
            .RequirePermission(MailFathomPermission.AdminRolesWrite);

        api.MapGet(GroupsRoute, ListGroupsAsync)
            .RequirePermissionOverTarget(MailFathomPermission.AdminRead);

        api.MapPost(GroupsRoute, CreateGroupAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            .RequirePermissionOverTarget(MailFathomPermission.AdminRolesWrite);

        api.MapPut(GroupNameRoute, RenameGroupAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            .RequirePermissionOverTarget(MailFathomPermission.AdminRolesWrite);

        api.MapDelete(GroupRoute, DeleteGroupAsync)
            .RequirePermissionOverTarget(MailFathomPermission.AdminRolesWrite);

        api.MapGet(GroupMembersRoute, ListGroupMembersAsync)
            .RequirePermissionOverTarget(MailFathomPermission.AdminRead);

        api.MapPut(GroupMemberRoute, AddGroupMemberAsync)
            .RequirePermissionOverTarget(MailFathomPermission.AdminRolesWrite);

        api.MapDelete(GroupMemberRoute, RemoveGroupMemberAsync)
            .RequirePermissionOverTarget(MailFathomPermission.AdminRolesWrite);

        api.MapGet(RoleAssignmentsRoute, ListAssignmentsAsync)
            .RequirePermissionOverTarget(MailFathomPermission.AdminRead);

        api.MapPost(RoleAssignmentsRoute, AssignAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            .RequirePermissionOverTarget(MailFathomPermission.AdminRolesWrite);

        api.MapDelete(RoleAssignmentRoute, RevokeAsync)
            .RequirePermissionOverTarget(MailFathomPermission.AdminRolesWrite);

        api.MapGet(UserPermissionsRoute, ExplainUserPermissionsAsync)
            .RequirePermissionOverTarget(MailFathomPermission.AdminRead);
    }

    /// <summary>Lists one page of the roles this deployment holds.</summary>
    /// <param name="pageSize">How many roles the page may hold, or <see langword="null" /> for the default.</param>
    /// <param name="cursor">The cursor the previous page returned, or <see langword="null" /> for the first page.</param>
    /// <param name="grants">The grant administration.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the page, or <c>400</c> naming what was wrong with the request.</returns>
    internal static async Task<Results<Ok<RoleListResponse>, ProblemHttpResult>> ListRolesAsync(
        [FromQuery] int? pageSize,
        [FromQuery] string? cursor,
        [FromServices] GrantAdministration grants,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grants);

        if (!AdminListingRequest.TryResolve(AdministrativeListing.Roles, pageSize, cursor, out var query, out var refusal))
        {
            return refusal;
        }

        var page = await grants.ReadRolesAsync(query, cancellationToken);

        return TypedResults.Ok(new RoleListResponse(
            [.. page.Entries.Select(RoleResponse.For)],
            AdminListingRequest.NextCursor(AdministrativeListing.Roles, page.ContinuesAfter)));
    }

    /// <summary>Records a role.</summary>
    /// <param name="request">The name and the permissions it grants.</param>
    /// <param name="grants">The grant administration.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>200</c> with the minted identifier, <c>409</c> when the name is taken, or <c>400</c> naming what was wrong with the request.</returns>
    internal static async Task<Results<Ok<GrantRecordedResponse>, ProblemHttpResult>> CreateRoleAsync(
        [FromBody] RoleProvisioningRequest? request,
        [FromServices] GrantAdministration grants,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grants);

        if (GrantAdministration.FindNameRefusal(request?.Name) is { } refusal)
        {
            return Refused(refusal);
        }

        if (!TryReadPermissions(request!.Permissions, out var permissions, out var unreadable))
        {
            return unreadable;
        }

        var result = await grants.CreateRoleAsync(request.Name, permissions, cancellationToken);

        return result.Outcome == GrantWriteOutcome.Written
            ? TypedResults.Ok(new GrantRecordedResponse(result.RecordId))
            : ProblemOf(result);
    }

    /// <summary>Replaces the name an operator reads a role by.</summary>
    /// <param name="roleId">The role.</param>
    /// <param name="request">The new name.</param>
    /// <param name="grants">The grant administration.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>204</c> once it stands, <c>404</c> when no such role exists, <c>409</c> when the name is taken, or <c>400</c> naming what was wrong with the request.</returns>
    internal static async Task<Results<NoContent, ProblemHttpResult>> RenameRoleAsync(
        Guid roleId,
        [FromBody] GrantRecordNameRequest? request,
        [FromServices] GrantAdministration grants,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grants);

        if (GrantAdministration.FindNameRefusal(request?.Name) is { } refusal)
        {
            return Refused(refusal);
        }

        return Answered(await grants.RenameRoleAsync(roleId, request!.Name, cancellationToken));
    }

    /// <summary>Replaces the whole list of permissions a role grants.</summary>
    /// <param name="roleId">The role.</param>
    /// <param name="request">The permissions it grants from now on.</param>
    /// <param name="grants">The grant administration.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>204</c> once it stands, <c>404</c> when no such role exists, <c>409</c> when it would drop the root from the last role giving it, or <c>400</c> naming what was wrong with the request.</returns>
    /// <remarks>Everybody the role is assigned to holds the new list from their next request, on every replica.</remarks>
    internal static async Task<Results<NoContent, ProblemHttpResult>> ReplaceRolePermissionsAsync(
        Guid roleId,
        [FromBody] RolePermissionsRequest? request,
        [FromServices] GrantAdministration grants,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grants);

        if (!TryReadPermissions(request?.Permissions, out var permissions, out var unreadable))
        {
            return unreadable;
        }

        return Answered(await grants.ReplaceRolePermissionsAsync(roleId, permissions, cancellationToken));
    }

    /// <summary>Removes a role nobody is assigned.</summary>
    /// <param name="roleId">The role.</param>
    /// <param name="grants">The grant administration.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>204</c> once it is gone, <c>404</c> when no such role exists, or <c>409</c> naming how many assignments still give it.</returns>
    internal static async Task<Results<NoContent, ProblemHttpResult>> DeleteRoleAsync(
        Guid roleId,
        [FromServices] GrantAdministration grants,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grants);

        return Answered(await grants.DeleteRoleAsync(roleId, cancellationToken));
    }

    /// <summary>Lists one page of the groups the caller's scope covers.</summary>
    /// <param name="pageSize">How many groups the page may hold, or <see langword="null" /> for the default.</param>
    /// <param name="cursor">The cursor the previous page returned, or <see langword="null" /> for the first page.</param>
    /// <param name="grants">The grant administration.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the page, or <c>400</c> naming what was wrong with the request.</returns>
    internal static async Task<Results<Ok<GroupListResponse>, ProblemHttpResult>> ListGroupsAsync(
        [FromQuery] int? pageSize,
        [FromQuery] string? cursor,
        [FromServices] GrantAdministration grants,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grants);

        if (!AdminListingRequest.TryResolve(AdministrativeListing.Groups, pageSize, cursor, out var query, out var refusal))
        {
            return refusal;
        }

        var page = await grants.ReadGroupsAsync(query, cancellationToken);

        return TypedResults.Ok(new GroupListResponse(
            [.. page.Entries.Select(GroupResponse.For)],
            AdminListingRequest.NextCursor(AdministrativeListing.Groups, page.ContinuesAfter)));
    }

    /// <summary>Records a group, in an organization or in none.</summary>
    /// <param name="request">The name and the organization, or that it belongs to none.</param>
    /// <param name="grants">The grant administration.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>200</c> with the minted identifier, <c>409</c> when the name is taken, or <c>400</c> naming what was wrong with the request.</returns>
    internal static async Task<Results<Ok<GrantRecordedResponse>, ProblemHttpResult>> CreateGroupAsync(
        [FromBody] GroupProvisioningRequest? request,
        [FromServices] GrantAdministration grants,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grants);

        if (GrantAdministration.FindNameRefusal(request?.Name) is { } refusal)
        {
            return Refused(refusal);
        }

        if (FindOrganizationRefusal(request!.OrganizationId, request.None) is { } organizationRefusal)
        {
            return Refused(organizationRefusal);
        }

        var organization = request.None == true ? null : request.OrganizationId;
        var result = await grants.CreateGroupAsync(request.Name, organization, cancellationToken);

        return result.Outcome switch
        {
            GrantWriteOutcome.Written => TypedResults.Ok(new GrantRecordedResponse(result.RecordId)),
            GrantWriteOutcome.UnknownOrganization => Refused(
                $"This deployment holds no organization '{organization}' you administer. List the organizations to read "
                + "the identifiers it does hold."),
            _ => ProblemOf(result),
        };
    }

    /// <summary>Replaces the name an operator reads a group by.</summary>
    /// <param name="groupId">The group.</param>
    /// <param name="request">The new name.</param>
    /// <param name="grants">The grant administration.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>204</c> once it stands, <c>404</c> when no such group exists within the caller's scope, <c>409</c> when the name is taken, or <c>400</c> naming what was wrong with the request.</returns>
    internal static async Task<Results<NoContent, ProblemHttpResult>> RenameGroupAsync(
        Guid groupId,
        [FromBody] GrantRecordNameRequest? request,
        [FromServices] GrantAdministration grants,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grants);

        if (GrantAdministration.FindNameRefusal(request?.Name) is { } refusal)
        {
            return Refused(refusal);
        }

        return Answered(await grants.RenameGroupAsync(groupId, request!.Name, cancellationToken));
    }

    /// <summary>Removes a group nothing is assigned to, and its memberships with it.</summary>
    /// <param name="groupId">The group.</param>
    /// <param name="grants">The grant administration.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>204</c> once it is gone, <c>404</c> when no such group exists within the caller's scope, or <c>409</c> naming how many assignments still name it.</returns>
    internal static async Task<Results<NoContent, ProblemHttpResult>> DeleteGroupAsync(
        Guid groupId,
        [FromServices] GrantAdministration grants,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grants);

        return Answered(await grants.DeleteGroupAsync(groupId, cancellationToken));
    }

    /// <summary>Lists one page of the members of one group.</summary>
    /// <param name="groupId">The group.</param>
    /// <param name="pageSize">How many members the page may hold, or <see langword="null" /> for the default.</param>
    /// <param name="cursor">The cursor the previous page returned, or <see langword="null" /> for the first page.</param>
    /// <param name="grants">The grant administration.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the page, <c>404</c> when no such group exists within the caller's scope, or <c>400</c> naming what was wrong with the request.</returns>
    internal static async Task<Results<Ok<GroupMemberListResponse>, ProblemHttpResult>> ListGroupMembersAsync(
        Guid groupId,
        [FromQuery] int? pageSize,
        [FromQuery] string? cursor,
        [FromServices] GrantAdministration grants,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grants);

        if (!AdminListingRequest.TryResolve(
                AdministrativeListing.GroupMembers,
                pageSize,
                cursor,
                out var query,
                out var refusal,
                within: groupId))
        {
            return refusal;
        }

        if (await grants.ReadGroupMembersAsync(groupId, query, cancellationToken) is not { } page)
        {
            return NotFound(NoSuchGroup);
        }

        return TypedResults.Ok(new GroupMemberListResponse(
            groupId,
            [.. page.Entries.Select(member => member.Value)],
            AdminListingRequest.NextCursor(AdministrativeListing.GroupMembers, page.ContinuesAfter, within: groupId)));
    }

    /// <summary>Makes a user a member of a group, which gives them every assignment the group holds.</summary>
    /// <param name="groupId">The group.</param>
    /// <param name="userId">The user joining it.</param>
    /// <param name="grants">The grant administration.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>204</c> once they are a member, also when they already were; <c>404</c> when no such group or user exists within the caller's scope; or <c>409</c> when the user belongs to a different organization from the group.</returns>
    internal static async Task<Results<NoContent, ProblemHttpResult>> AddGroupMemberAsync(
        Guid groupId,
        Guid userId,
        [FromServices] GrantAdministration grants,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grants);

        if (userId == Guid.Empty)
        {
            return Refused("The request named no user.");
        }

        return Answered(await grants.AddGroupMemberAsync(groupId, UserId.Create(userId), cancellationToken));
    }

    /// <summary>Ends a user's membership of a group.</summary>
    /// <param name="groupId">The group.</param>
    /// <param name="userId">The user leaving it.</param>
    /// <param name="grants">The grant administration.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>204</c> once they are no member, also when they were not one; <c>404</c> when no such group or user exists within the caller's scope; or <c>409</c> when leaving would take the last root away.</returns>
    internal static async Task<Results<NoContent, ProblemHttpResult>> RemoveGroupMemberAsync(
        Guid groupId,
        Guid userId,
        [FromServices] GrantAdministration grants,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grants);

        if (userId == Guid.Empty)
        {
            return Refused("The request named no user.");
        }

        return Answered(await grants.RemoveGroupMemberAsync(groupId, UserId.Create(userId), cancellationToken));
    }

    /// <summary>Lists one page of the role assignments the caller's scope covers.</summary>
    /// <param name="pageSize">How many assignments the page may hold, or <see langword="null" /> for the default.</param>
    /// <param name="cursor">The cursor the previous page returned, or <see langword="null" /> for the first page.</param>
    /// <param name="grants">The grant administration.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the page, or <c>400</c> naming what was wrong with the request.</returns>
    internal static async Task<Results<Ok<RoleAssignmentListResponse>, ProblemHttpResult>> ListAssignmentsAsync(
        [FromQuery] int? pageSize,
        [FromQuery] string? cursor,
        [FromServices] GrantAdministration grants,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grants);

        if (!AdminListingRequest.TryResolve(
                AdministrativeListing.RoleAssignments,
                pageSize,
                cursor,
                out var query,
                out var refusal))
        {
            return refusal;
        }

        var page = await grants.ReadAssignmentsAsync(query, cancellationToken);

        return TypedResults.Ok(new RoleAssignmentListResponse(
            [.. page.Entries.Select(RoleAssignmentResponse.For)],
            AdminListingRequest.NextCursor(AdministrativeListing.RoleAssignments, page.ContinuesAfter)));
    }

    /// <summary>Gives a role to a user or a group at a scope.</summary>
    /// <param name="request">The role, the principal, and the scope.</param>
    /// <param name="grants">The grant administration.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>200</c> with the minted identifier, <c>409</c> when the same role is already given to the same principal at the same scope, or <c>400</c> naming what was wrong with the request — a role, a group, a user, or an organization that does not exist within the caller's scope among it.</returns>
    internal static async Task<Results<Ok<GrantRecordedResponse>, ProblemHttpResult>> AssignAsync(
        [FromBody] RoleAssignmentRequest? request,
        [FromServices] GrantAdministration grants,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grants);

        if (!TryReadAssignment(request, out var roleId, out var principal, out var scope, out var refusal))
        {
            return Refused(refusal);
        }

        var result = await grants.AssignAsync(roleId, principal, scope, cancellationToken);

        return result.Outcome switch
        {
            GrantWriteOutcome.Written => TypedResults.Ok(new GrantRecordedResponse(result.RecordId)),
            GrantWriteOutcome.AlreadyAssigned => ProblemOf(result),
            GrantWriteOutcome.UnknownRole => Refused("This deployment holds no such role. List the roles to read the identifiers it does hold."),
            GrantWriteOutcome.UnknownGroup => Refused("This deployment holds no such group within your scope."),
            GrantWriteOutcome.UnknownOrganization => Refused("This deployment holds no such organization within your scope."),
            _ => Refused("This deployment holds no such user within your scope."),
        };
    }

    /// <summary>Revokes one role assignment.</summary>
    /// <param name="assignmentId">The assignment.</param>
    /// <param name="grants">The grant administration.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>204</c> once it is gone, <c>404</c> when no such assignment exists within the caller's scope, or <c>409</c> when it is the last one giving the root.</returns>
    internal static async Task<Results<NoContent, ProblemHttpResult>> RevokeAsync(
        Guid assignmentId,
        [FromServices] GrantAdministration grants,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grants);

        return Answered(await grants.RevokeAsync(assignmentId, cancellationToken));
    }

    /// <summary>Explains why one user holds each permission they hold, and what each of their credentials keeps of it.</summary>
    /// <param name="userId">The user.</param>
    /// <param name="grants">The grant administration.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the explanation, or <c>404</c> when no such user exists within the caller's scope.</returns>
    internal static async Task<Results<Ok<UserPermissionsResponse>, ProblemHttpResult>> ExplainUserPermissionsAsync(
        Guid userId,
        [FromServices] GrantAdministration grants,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grants);

        if (userId == Guid.Empty)
        {
            return Refused("The request named no user.");
        }

        return await grants.ExplainUserGrantAsync(UserId.Create(userId), cancellationToken) is { } explanation
            ? TypedResults.Ok(UserPermissionsResponse.For(explanation))
            : NotFound("This deployment holds no such user within your scope.");
    }

    /// <summary>Reads the names and patterns a request listed into the list a role is written with.</summary>
    /// <returns><see langword="true" /> when every entry grants something; otherwise <see langword="false" />, with the refusal naming each name nothing publishes and each pattern reaching nothing.</returns>
    private static bool TryReadPermissions(
        IReadOnlyList<string>? names,
        [NotNullWhen(true)] out RolePermissions? permissions,
        [NotNullWhen(false)] out ProblemHttpResult? refusal)
    {
        refusal = null;

        if (names is null)
        {
            permissions = null;
            refusal = Refused("The request named no list of permissions. Send the whole list the role grants, which may be empty.");

            return false;
        }

        if (RolePermissions.TryCreate(names, out var written, out var unpublished) && written is not null)
        {
            permissions = written;

            return true;
        }

        permissions = null;
        refusal = Refused(DescribeUngranting(unpublished));

        return false;
    }

    /// <summary>Says why written entries grant nothing, telling a name nothing publishes from a pattern reaching nothing.</summary>
    /// <remarks>
    /// The two are answered apart because the remedy differs: a name is misspelled or belongs to another release, while
    /// a pattern is well formed and simply has nothing beneath it, which no later release is promised to change.
    /// </remarks>
    private static string DescribeUngranting(IReadOnlyList<string> entries)
    {
        var patterns = entries.Where(entry => PermissionSubtree.TryParse(entry, out _)).ToArray();
        var names = entries.Except(patterns, StringComparer.Ordinal).ToArray();

        string?[] sentences =
        [
            names.Length > 0 ? $"This build publishes no permission named {Quoted(names)}." : null,
            patterns.Length > 0 ? $"This build publishes nothing in the reach of the pattern {Quoted(patterns)}." : null,
            "A role lists published names, and patterns reaching at least one of them, in which '*' is a whole "
            + "dot-separated segment standing for one or more segments.",
        ];

        return string.Join(' ', sentences.OfType<string>());
    }

    private static string Quoted(IEnumerable<string> entries) => string.Join(", ", entries.Select(entry => $"'{entry}'"));

    /// <summary>Reads what an assignment request names, or why it names nothing an assignment can be made of.</summary>
    private static bool TryReadAssignment(
        RoleAssignmentRequest? request,
        out Guid roleId,
        [NotNullWhen(true)] out AssignmentPrincipal? principal,
        [NotNullWhen(true)] out AssignmentScope? scope,
        [NotNullWhen(false)] out string? refusal)
    {
        roleId = request?.RoleId ?? Guid.Empty;
        principal = null;
        scope = null;

        if (roleId == Guid.Empty)
        {
            refusal = "The request named no role.";

            return false;
        }

        principal = (request!.PrincipalKind, request.PrincipalId) switch
        {
            (_, null) => null,
            (_, { } id) when id == Guid.Empty => null,
            (GrantRequestNames.User, { } id) => AssignmentPrincipal.User(UserId.Create(id)),
            (GrantRequestNames.Group, { } id) => AssignmentPrincipal.Group(id),
            _ => null,
        };

        if (principal is null)
        {
            refusal = $"The request named no principal. Send '{GrantRequestNames.User}' or '{GrantRequestNames.Group}' "
                + "as the principal kind beside the identifier it names.";

            return false;
        }

        scope = (request.ScopeKind, request.ScopeId) switch
        {
            (GrantRequestNames.Deployment, null) => AssignmentScope.Deployment,
            (GrantRequestNames.Organization, { } id) when id != Guid.Empty => AssignmentScope.Organization(id),
            (GrantRequestNames.User, { } id) when id != Guid.Empty => AssignmentScope.User(UserId.Create(id)),
            _ => null,
        };

        if (scope is null)
        {
            refusal = $"The request named no scope. Send '{GrantRequestNames.Deployment}' with no identifier, or "
                + $"'{GrantRequestNames.Organization}' or '{GrantRequestNames.User}' beside the identifier it names.";

            return false;
        }

        refusal = null;

        return true;
    }

    /// <summary>Reports why a group request names no single organization, or that it names one or none deliberately.</summary>
    private static string? FindOrganizationRefusal(Guid? organizationId, bool? none) =>
        (organizationId, none == true) switch
        {
            (null, false) => "The request named neither an organization nor that the group should belong to none.",
            ({ } named, true) => $"The request both named organization '{named}' and said the group should belong to none. "
                + "Send one of the two.",
            ({ } named, false) when named == Guid.Empty =>
                "An organization is named by the identifier this deployment recorded it under.",
            _ => null,
        };

    /// <summary>Answers a write naming its record in the path, which is a <c>204</c> or the refusal its outcome names.</summary>
    /// <remarks>A membership that already stood as asked is a <c>204</c> as well: the caller has the state it asked for, and only the record of a change tells the two apart.</remarks>
    private static Results<NoContent, ProblemHttpResult> Answered(GrantWriteResult result) =>
        result.Outcome is GrantWriteOutcome.Written or GrantWriteOutcome.Unchanged
            ? TypedResults.NoContent()
            : ProblemOf(result);

    /// <summary>Writes the refusal an outcome other than <see cref="GrantWriteOutcome.Written" /> names.</summary>
    private static ProblemHttpResult ProblemOf(GrantWriteResult result) => result.Outcome switch
    {
        GrantWriteOutcome.NameTaken => Conflict(
            "Another role, or another group, already carries that name. Choose another."),
        GrantWriteOutcome.UnknownRole => NotFound("This deployment holds no such role."),
        GrantWriteOutcome.UnknownGroup => NotFound(NoSuchGroup),
        GrantWriteOutcome.UnknownUser => NotFound("This deployment holds no such user within your scope."),
        GrantWriteOutcome.UnknownOrganization => NotFound("This deployment holds no such organization within your scope."),
        GrantWriteOutcome.UnknownAssignment => NotFound("This deployment holds no such role assignment within your scope."),
        GrantWriteOutcome.AlreadyAssigned => Conflict(
            "The same role is already given to the same principal at the same scope."),
        GrantWriteOutcome.StillAssigned => Conflict(
            $"{result.StandingAssignments} role assignment(s) still stand on it. Revoke each of them before removing it."),
        GrantWriteOutcome.OutsideGroupOrganization => Conflict(
            "The user belongs to a different organization from the group, which holds only its own organization's "
            + "members."),
        _ => Conflict(
            $"This would leave nobody holding '{MailFathomPermission.AdminRolesWrite.Name}' over the deployment, and a "
            + "deployment nobody can administer is recovered only through its database. Give it to somebody else first."),
    };

    private static ProblemHttpResult Refused(string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status400BadRequest);

    private static ProblemHttpResult NotFound(string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status404NotFound);

    private static ProblemHttpResult Conflict(string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status409Conflict);
}
