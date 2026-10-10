// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration.Grants;

/// <summary>The role assignments a caller's scope covers, as one page of the listing answers with them or as every page read together.</summary>
/// <param name="Assignments">The assignments, in the deployment's identifier order.</param>
/// <param name="NextCursor">The cursor the following page is asked with, or <see langword="null" /> at the end.</param>
internal sealed record RoleAssignmentList(
    [property: JsonPropertyName("assignments")] IReadOnlyList<RoleAssignmentEntry>? Assignments,
    [property: JsonPropertyName("nextCursor")] string? NextCursor);

/// <summary>One role given to a user or a group at a scope.</summary>
/// <param name="Id">The identifier it is revoked by.</param>
/// <param name="RoleId">The role it gives.</param>
/// <param name="Principal">Who it gives the role to.</param>
/// <param name="Scope">What it reaches.</param>
/// <param name="AssignedAt">When it was given.</param>
internal sealed record RoleAssignmentEntry(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("roleId")] Guid RoleId,
    [property: JsonPropertyName("principal")] GrantReference? Principal,
    [property: JsonPropertyName("scope")] GrantReference? Scope,
    [property: JsonPropertyName("assignedAt")] DateTimeOffset AssignedAt);

/// <summary>Who an assignment gives a role to, or what it reaches, as a published kind and an identifier.</summary>
/// <param name="Kind"><c>user</c> or <c>group</c> for a principal, and <c>deployment</c>, <c>organization</c>, or <c>user</c> for a scope.</param>
/// <param name="Id">The identifier it names, absent for the deployment.</param>
internal sealed record GrantReference(
    [property: JsonPropertyName("kind")] string? Kind,
    [property: JsonPropertyName("id")] Guid? Id)
{
    /// <summary>Describes the reference as an operator reads it, such as <c>organization 7777…</c> or <c>deployment</c>.</summary>
    /// <returns>The kind, followed by the identifier where it names one.</returns>
    internal string Describe()
    {
        var kind = ConsoleSafeText.Sanitize(this.Kind) ?? "unreported";

        return this.Id is { } id ? $"{kind} {id:D}" : kind;
    }
}
