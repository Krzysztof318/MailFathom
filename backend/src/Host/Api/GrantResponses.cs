// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access.Grants;
using MailFathom.Domain.Access;

namespace MailFathom.Host.Api;

/// <summary>A role to record.</summary>
/// <param name="Name">The name an operator reads it by.</param>
/// <param name="Permissions">What it grants: published permission names, and patterns reaching at least one of them.</param>
internal sealed record RoleProvisioningRequest(string? Name, IReadOnlyList<string>? Permissions);

/// <summary>A role's or a group's new name.</summary>
/// <param name="Name">The name an operator reads it by from now on.</param>
internal sealed record GrantRecordNameRequest(string? Name);

/// <summary>The whole list of permissions a role grants from now on.</summary>
/// <param name="Permissions">The published permission names and the patterns, replacing every entry the role listed.</param>
/// <remarks>Nullable so a body naming no list is refused rather than read as an empty one, which would take every name away from everybody the role is assigned to.</remarks>
internal sealed record RolePermissionsRequest(IReadOnlyList<string>? Permissions);

/// <summary>A group to record.</summary>
/// <param name="Name">The name an operator reads it by.</param>
/// <param name="OrganizationId">The organization it belongs to, or <see langword="null" /> beside <paramref name="None" />.</param>
/// <param name="None">Whether the group belongs to no organization, which makes it the deployment's.</param>
/// <remarks>A group in no organization is stated rather than left out, because that is the group only the root may write and an omitted field must not be what chose it.</remarks>
internal sealed record GroupProvisioningRequest(string? Name, Guid? OrganizationId, bool? None);

/// <summary>A role to give to a user or a group at a scope.</summary>
/// <param name="RoleId">The role.</param>
/// <param name="PrincipalKind">Who it is given to: <c>user</c> or <c>group</c>.</param>
/// <param name="PrincipalId">The user's or the group's identifier.</param>
/// <param name="ScopeKind">What it reaches: <c>deployment</c>, <c>organization</c>, or <c>user</c>.</param>
/// <param name="ScopeId">The organization's or the user's identifier, and absent for the deployment.</param>
/// <remarks>The scope is named rather than defaulted: a missing scope must never read as the deployment, which is the widest grant there is.</remarks>
internal sealed record RoleAssignmentRequest(
    Guid? RoleId,
    string? PrincipalKind,
    Guid? PrincipalId,
    string? ScopeKind,
    Guid? ScopeId);

/// <summary>One pattern a role lists, beside what it resolves to in the build answering.</summary>
/// <param name="Pattern">The pattern as it was written.</param>
/// <param name="Reaches">The published permission names it reaches now, which a later release may add to.</param>
internal sealed record RolePatternResponse(string Pattern, IReadOnlyList<string> Reaches);

/// <summary>One role as an administrator reads it.</summary>
/// <param name="Id">The identifier every assignment of it names.</param>
/// <param name="Name">The name an operator reads it by.</param>
/// <param name="Permissions">The list as it was written: each published permission name, then each pattern. Sending it back writes the same role.</param>
/// <param name="Patterns">Each pattern on that list beside the names it reaches now, empty for a role of names alone.</param>
/// <param name="Unpublished">Stored entries that grant nothing in this build — a name it does not publish, a pattern reaching nothing it publishes — reported so somebody can remove them.</param>
/// <param name="CreatedAt">When it was recorded.</param>
internal sealed record RoleResponse(
    Guid Id,
    string Name,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<RolePatternResponse> Patterns,
    IReadOnlyList<string> Unpublished,
    DateTimeOffset CreatedAt)
{
    /// <summary>Describes one role for a caller.</summary>
    /// <param name="role">The role.</param>
    /// <returns>The response body.</returns>
    internal static RoleResponse For(Role role)
    {
        ArgumentNullException.ThrowIfNull(role);

        return new RoleResponse(
            role.Id,
            role.Name,
            role.Permissions.Written,
            [
                .. role.Permissions.Patterns
                    .Select(pattern => new RolePatternResponse(
                        pattern.Written,
                        [.. pattern.CoveredPermissions().Select(permission => permission.Name)]))
                    .Where(pattern => pattern.Reaches.Count > 0),
            ],
            role.Permissions.Unpublished,
            role.CreatedAt);
    }
}

/// <summary>One page of roles.</summary>
/// <param name="Roles">The page's roles, in identifier order.</param>
/// <param name="NextCursor">The cursor the following page is asked with, or <see langword="null" /> on the last page.</param>
internal sealed record RoleListResponse(IReadOnlyList<RoleResponse> Roles, string? NextCursor);

/// <summary>One group as an administrator reads it.</summary>
/// <param name="Id">The identifier every membership and assignment names it by.</param>
/// <param name="Name">The name an operator reads it by.</param>
/// <param name="OrganizationId">The organization it belongs to, or <see langword="null" /> for the deployment's.</param>
/// <param name="Members">How many users belong to it.</param>
/// <param name="CreatedAt">When it was recorded.</param>
internal sealed record GroupResponse(Guid Id, string Name, Guid? OrganizationId, int Members, DateTimeOffset CreatedAt)
{
    /// <summary>Describes one group for a caller.</summary>
    /// <param name="group">The group.</param>
    /// <returns>The response body.</returns>
    internal static GroupResponse For(UserGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);

        return new GroupResponse(group.Id, group.Name, group.OrganizationId, group.MemberCount, group.CreatedAt);
    }
}

/// <summary>One page of groups.</summary>
/// <param name="Groups">The page's groups, in identifier order.</param>
/// <param name="NextCursor">The cursor the following page is asked with, or <see langword="null" /> on the last page.</param>
internal sealed record GroupListResponse(IReadOnlyList<GroupResponse> Groups, string? NextCursor);

/// <summary>One page of the members of a group.</summary>
/// <param name="Group">The group, echoed so answers about several groups can be told apart.</param>
/// <param name="Members">The members' user identifiers, in identifier order.</param>
/// <param name="NextCursor">The cursor the following page is asked with, or <see langword="null" /> on the last page.</param>
internal sealed record GroupMemberListResponse(Guid Group, IReadOnlyList<Guid> Members, string? NextCursor);

/// <summary>Who an assignment gives a role to, or what it reaches, as a kind and an identifier.</summary>
/// <param name="Kind">The published kind: <c>user</c> or <c>group</c> for a principal, and <c>deployment</c>, <c>organization</c>, or <c>user</c> for a scope.</param>
/// <param name="Id">The identifier it names, absent for the deployment.</param>
internal sealed record GrantReferenceResponse(string Kind, Guid? Id)
{
    /// <summary>Describes a principal.</summary>
    /// <param name="principal">The principal.</param>
    /// <returns>The response body.</returns>
    internal static GrantReferenceResponse For(AssignmentPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        return new GrantReferenceResponse(GrantRequestNames.Of(principal.Kind), principal.Id);
    }

    /// <summary>Describes a scope.</summary>
    /// <param name="scope">The scope.</param>
    /// <returns>The response body.</returns>
    internal static GrantReferenceResponse For(AssignmentScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        return new GrantReferenceResponse(
            GrantRequestNames.Of(scope.Kind),
            scope.Kind == AssignmentScopeKind.Deployment ? null : scope.Target);
    }
}

/// <summary>One role assignment as an administrator reads it.</summary>
/// <param name="Id">The identifier it is revoked by.</param>
/// <param name="RoleId">The role it gives.</param>
/// <param name="Principal">Who it gives it to.</param>
/// <param name="Scope">What it reaches.</param>
/// <param name="AssignedAt">When it was given.</param>
internal sealed record RoleAssignmentResponse(
    Guid Id,
    Guid RoleId,
    GrantReferenceResponse Principal,
    GrantReferenceResponse Scope,
    DateTimeOffset AssignedAt)
{
    /// <summary>Describes one assignment for a caller.</summary>
    /// <param name="assignment">The assignment.</param>
    /// <returns>The response body.</returns>
    internal static RoleAssignmentResponse For(RoleAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);

        return new RoleAssignmentResponse(
            assignment.Id,
            assignment.RoleId,
            GrantReferenceResponse.For(assignment.Principal),
            GrantReferenceResponse.For(assignment.Scope),
            assignment.AssignedAt);
    }
}

/// <summary>One page of role assignments.</summary>
/// <param name="Assignments">The page's assignments, in identifier order.</param>
/// <param name="NextCursor">The cursor the following page is asked with, or <see langword="null" /> on the last page.</param>
internal sealed record RoleAssignmentListResponse(IReadOnlyList<RoleAssignmentResponse> Assignments, string? NextCursor);

/// <summary>The identifier a role, a group, or an assignment was recorded under.</summary>
/// <param name="Id">The identifier every later act on it names.</param>
internal sealed record GrantRecordedResponse(Guid Id);

/// <summary>Why a user holds one permission at one scope.</summary>
/// <param name="Permission">The published permission name.</param>
/// <param name="Pattern">The pattern on the role's list that reaches it, as written, or <see langword="null" /> where the role lists the permission by name.</param>
/// <param name="RoleId">The role listing it.</param>
/// <param name="Role">The role's name.</param>
/// <param name="AssignmentId">The assignment giving the role.</param>
/// <param name="GroupId">The group the assignment reaches the user through, or <see langword="null" /> where it names them directly.</param>
/// <param name="Group">That group's name, or <see langword="null" /> where the assignment names the user directly.</param>
/// <param name="Scope">Where the permission is held.</param>
/// <param name="Inert">Whether the permission reaches nothing there, every operation it covers being the deployment's alone.</param>
internal sealed record GrantSourceResponse(
    string Permission,
    string? Pattern,
    Guid RoleId,
    string Role,
    Guid AssignmentId,
    Guid? GroupId,
    string? Group,
    GrantReferenceResponse Scope,
    bool Inert)
{
    /// <summary>Describes one source for a caller.</summary>
    /// <param name="source">The source.</param>
    /// <returns>The response body.</returns>
    internal static GrantSourceResponse For(GrantSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new GrantSourceResponse(
            source.Permission.Name,
            source.Pattern,
            source.RoleId,
            source.RoleName,
            source.AssignmentId,
            source.GroupId,
            source.GroupName,
            GrantReferenceResponse.For(source.Scope),
            source.IsInert);
    }
}

/// <summary>Why one user holds what they hold within the caller's own scope, and what each of their credentials keeps of it.</summary>
/// <param name="User">The user the explanation is about.</param>
/// <param name="Sources">One row per permission, entry of a role's list granting it, and assignment the caller's own grant covers, in the order the published set declares the permissions.</param>
/// <param name="SourcesTruncated">Whether the user holds more rows there than one explanation carries, so <paramref name="Sources" /> and what each credential is shown to hold are a part of the answer.</param>
/// <param name="Credentials">The user's credentials, each with its own narrowing and what a request presenting it holds now of what <paramref name="Sources" /> names.</param>
internal sealed record UserPermissionsResponse(
    Guid User,
    IReadOnlyList<GrantSourceResponse> Sources,
    bool SourcesTruncated,
    IReadOnlyList<UserCredentialResponse> Credentials)
{
    /// <summary>Describes one explanation for a caller.</summary>
    /// <param name="explanation">The explanation.</param>
    /// <returns>The response body.</returns>
    internal static UserPermissionsResponse For(UserGrantExplanation explanation)
    {
        ArgumentNullException.ThrowIfNull(explanation);

        var grant = explanation.Grant;

        return new UserPermissionsResponse(
            explanation.User.Value,
            [.. explanation.Sources.Select(GrantSourceResponse.For)],
            explanation.SourcesTruncated,
            [.. explanation.Credentials.Select(credential => UserCredentialResponse.For(credential, grant))]);
    }
}

/// <summary>The published names a request writes a principal's or a scope's kind as.</summary>
internal static class GrantRequestNames
{
    /// <summary>The kind naming one user, as a principal and as a scope alike.</summary>
    internal const string User = "user";

    /// <summary>The principal kind naming one group.</summary>
    internal const string Group = "group";

    /// <summary>The scope kind covering the whole deployment.</summary>
    internal const string Deployment = "deployment";

    /// <summary>The scope kind covering one organization.</summary>
    internal const string Organization = "organization";

    /// <summary>Names a principal's kind.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The published name.</returns>
    internal static string Of(AssignmentPrincipalKind kind) => kind == AssignmentPrincipalKind.Group ? Group : User;

    /// <summary>Names a scope's kind.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The published name.</returns>
    internal static string Of(AssignmentScopeKind kind) => kind switch
    {
        AssignmentScopeKind.Organization => Organization,
        AssignmentScopeKind.User => User,
        _ => Deployment,
    };
}
