// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration.Policies;

/// <summary>One scope's settings policy as the deployment holds it.</summary>
/// <param name="OrganizationId">The organization the policy belongs to, or <see langword="null" /> where it is the deployment's own.</param>
/// <param name="Version">The version the policy was read at, which the next write is composed over, and zero where the scope stores none.</param>
/// <param name="Document">The policy as a JSON object, which is an empty one where the scope stores none.</param>
/// <remarks>Nothing in the document is redacted, because a policy holds no secret: what an editing session opens is what is stored.</remarks>
internal sealed record SettingsPolicy(
    [property: JsonPropertyName("organizationId")] Guid? OrganizationId,
    [property: JsonPropertyName("version")] long Version,
    [property: JsonPropertyName("document")] string? Document);
