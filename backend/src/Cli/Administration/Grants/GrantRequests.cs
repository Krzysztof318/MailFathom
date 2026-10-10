// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration.Grants;

/// <summary>The role an administrator asks a deployment to define.</summary>
/// <param name="Name">The name an operator reads it by, unique across every role and group.</param>
/// <param name="Permissions">The published permission names it grants, which may be none.</param>
/// <remarks>The identifier is not here: the deployment mints one, so a command supplying one would decide an identity it does not own.</remarks>
internal sealed record RoleProvisioningRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("permissions")] IReadOnlyList<string> Permissions);

/// <summary>The name a role or a group is read by from now on.</summary>
/// <param name="Name">The new name.</param>
internal sealed record GrantRecordNameRequest(
    [property: JsonPropertyName("name")] string Name);

/// <summary>The whole list of permissions a role grants from now on.</summary>
/// <param name="Permissions">The published permission names, replacing every name the role listed.</param>
internal sealed record RolePermissionsRequest(
    [property: JsonPropertyName("permissions")] IReadOnlyList<string> Permissions);

/// <summary>The group an administrator asks a deployment to record.</summary>
/// <param name="Name">The name an operator reads it by, unique across every role and group.</param>
/// <param name="OrganizationId">The organization it belongs to, or <see langword="null" /> when <paramref name="None" /> says it belongs to none.</param>
/// <param name="None">Whether the group belongs to no organization, which makes it the deployment's.</param>
/// <remarks>Belonging to none is sent as a stated decision rather than as a missing identifier, because the deployment refuses a body that states neither.</remarks>
internal sealed record GroupProvisioningRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("organizationId")] Guid? OrganizationId,
    [property: JsonPropertyName("none")] bool None);

/// <summary>A role to give to a user or a group at a scope.</summary>
/// <param name="RoleId">The role.</param>
/// <param name="PrincipalKind">Who it is given to: <c>user</c> or <c>group</c>.</param>
/// <param name="PrincipalId">The user's or the group's identifier.</param>
/// <param name="ScopeKind">What it reaches: <c>deployment</c>, <c>organization</c>, or <c>user</c>.</param>
/// <param name="ScopeId">The organization's or the user's identifier, and <see langword="null" /> for the deployment.</param>
internal sealed record RoleAssignmentRequest(
    [property: JsonPropertyName("roleId")] Guid RoleId,
    [property: JsonPropertyName("principalKind")] string PrincipalKind,
    [property: JsonPropertyName("principalId")] Guid PrincipalId,
    [property: JsonPropertyName("scopeKind")] string ScopeKind,
    [property: JsonPropertyName("scopeId")] Guid? ScopeId);

/// <summary>The identifier a role, a group, or an assignment was recorded under.</summary>
/// <param name="Id">The identifier the deployment minted, which every later act on the record names.</param>
internal sealed record GrantRecorded(
    [property: JsonPropertyName("id")] Guid Id);
