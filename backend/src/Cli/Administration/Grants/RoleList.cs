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
/// <param name="Permissions">The published permission names it grants.</param>
/// <param name="Unpublished">Stored names this deployment's build does not publish, which grant nothing.</param>
/// <param name="CreatedAt">When it was defined.</param>
internal sealed record RoleEntry(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("permissions")] IReadOnlyList<string>? Permissions,
    [property: JsonPropertyName("unpublished")] IReadOnlyList<string>? Unpublished,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt);
