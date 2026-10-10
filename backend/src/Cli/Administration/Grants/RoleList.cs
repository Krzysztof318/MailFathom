// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration.Grants;

/// <summary>The roles a deployment defines, as one page of the listing answers with them or as every page read together.</summary>
/// <param name="Roles">The roles, in the deployment's identifier order.</param>
/// <param name="NextCursor">The cursor the following page is asked with, or <see langword="null" /> at the end.</param>
internal sealed record RoleList(
    [property: JsonPropertyName("roles")] IReadOnlyList<RoleEntry>? Roles,
    [property: JsonPropertyName("nextCursor")] string? NextCursor);

/// <summary>One role a deployment defines.</summary>
/// <param name="Id">The identifier every assignment of it names.</param>
/// <param name="Name">The name an operator reads it by.</param>
/// <param name="Permissions">The list as it was written: each published permission name, then each pattern.</param>
/// <param name="Patterns">Each pattern on that list beside the names it reaches in the deployment's build.</param>
/// <param name="Unpublished">Stored entries that grant nothing in the deployment's build: a name it does not publish, a pattern reaching nothing it publishes.</param>
/// <param name="CreatedAt">When it was defined.</param>
internal sealed record RoleEntry(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("permissions")] IReadOnlyList<string>? Permissions,
    [property: JsonPropertyName("patterns")] IReadOnlyList<RolePattern>? Patterns,
    [property: JsonPropertyName("unpublished")] IReadOnlyList<string>? Unpublished,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt);

/// <summary>One pattern a role lists, beside what it resolves to.</summary>
/// <param name="Pattern">The pattern as it was written.</param>
/// <param name="Reaches">The published permission names it reaches now, which a later release of the deployment may add to.</param>
internal sealed record RolePattern(
    [property: JsonPropertyName("pattern")] string? Pattern,
    [property: JsonPropertyName("reaches")] IReadOnlyList<string>? Reaches);
