// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;
using MailFathom.Cli.Administration.Users;

namespace MailFathom.Cli.Administration.Grants;

/// <summary>Why one user holds what they hold within the caller's own scope, and what each of their credentials keeps of it.</summary>
/// <param name="User">The user the explanation is about.</param>
/// <param name="Sources">One row per permission, assignment, and scope the caller's own grant covers, in the order the published set declares the permissions.</param>
/// <param name="SourcesTruncated">Whether the user holds more rows there than one explanation carries.</param>
/// <param name="Credentials">The user's credentials, each with its own narrowing and what a request presenting it holds now of what <paramref name="Sources" /> names.</param>
internal sealed record UserPermissions(
    [property: JsonPropertyName("user")] Guid User,
    [property: JsonPropertyName("sources")] IReadOnlyList<GrantSource>? Sources,
    [property: JsonPropertyName("sourcesTruncated")] bool SourcesTruncated,
    [property: JsonPropertyName("credentials")] IReadOnlyList<UserCredential>? Credentials);

/// <summary>Why a user holds one permission at one scope.</summary>
/// <param name="Permission">The published permission name.</param>
/// <param name="RoleId">The role listing it.</param>
/// <param name="Role">The role's name.</param>
/// <param name="AssignmentId">The assignment giving the role, which is what revokes it.</param>
/// <param name="GroupId">The group the assignment reaches the user through, or <see langword="null" /> where it names them directly.</param>
/// <param name="Group">That group's name, or <see langword="null" /> where the assignment names the user directly.</param>
/// <param name="Scope">Where the permission is held.</param>
/// <param name="Inert">Whether the permission reaches nothing there, every operation it covers being the deployment's alone.</param>
internal sealed record GrantSource(
    [property: JsonPropertyName("permission")] string? Permission,
    [property: JsonPropertyName("roleId")] Guid RoleId,
    [property: JsonPropertyName("role")] string? Role,
    [property: JsonPropertyName("assignmentId")] Guid AssignmentId,
    [property: JsonPropertyName("groupId")] Guid? GroupId,
    [property: JsonPropertyName("group")] string? Group,
    [property: JsonPropertyName("scope")] GrantReference? Scope,
    [property: JsonPropertyName("inert")] bool Inert);
