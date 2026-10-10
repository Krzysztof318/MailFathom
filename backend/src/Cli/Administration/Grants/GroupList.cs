// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration.Grants;

/// <summary>The groups a caller's scope covers, as one page of the listing answers with them or as every page read together.</summary>
/// <param name="Groups">The groups, in the deployment's identifier order.</param>
/// <param name="NextCursor">The cursor the following page is asked with, or <see langword="null" /> at the end.</param>
internal sealed record GroupList(
    [property: JsonPropertyName("groups")] IReadOnlyList<GroupEntry>? Groups,
    [property: JsonPropertyName("nextCursor")] string? NextCursor);

/// <summary>One group a deployment holds.</summary>
/// <param name="Id">The identifier every membership and assignment names it by.</param>
/// <param name="Name">The name an operator reads it by.</param>
/// <param name="OrganizationId">The organization it belongs to, or <see langword="null" /> for a group of the deployment's own.</param>
/// <param name="Members">How many users belong to it.</param>
/// <param name="CreatedAt">When it was recorded.</param>
internal sealed record GroupEntry(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("organizationId")] Guid? OrganizationId,
    [property: JsonPropertyName("members")] int Members,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt);

/// <summary>The members of one group, as one page of the listing answers with them or as every page read together.</summary>
/// <param name="Group">The group the listing is about.</param>
/// <param name="Members">The members' user identifiers, in identifier order.</param>
/// <param name="NextCursor">The cursor the following page is asked with, or <see langword="null" /> at the end.</param>
internal sealed record GroupMemberList(
    [property: JsonPropertyName("group")] Guid Group,
    [property: JsonPropertyName("members")] IReadOnlyList<Guid>? Members,
    [property: JsonPropertyName("nextCursor")] string? NextCursor);
