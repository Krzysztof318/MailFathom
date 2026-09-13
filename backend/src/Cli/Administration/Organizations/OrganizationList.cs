// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration.Organizations;

/// <summary>The organizations a deployment holds.</summary>
/// <param name="Organizations">The organizations, ordered by short name.</param>
internal sealed record OrganizationList(
    [property: JsonPropertyName("organizations")] IReadOnlyList<OrganizationEntry>? Organizations);

/// <summary>One organization a deployment holds.</summary>
/// <param name="Id">The identifier every act on the organization names it by.</param>
/// <param name="DisplayName">The name an operator reads it by.</param>
/// <param name="ShortName">The short name its members sign in under, as the first half of <c>SHORTNAME/username</c>.</param>
/// <param name="Members">How many users belong to it.</param>
/// <param name="CreatedAt">When it was recorded.</param>
internal sealed record OrganizationEntry(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("displayName")] string? DisplayName,
    [property: JsonPropertyName("shortName")] string? ShortName,
    [property: JsonPropertyName("members")] int Members,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt);
