// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration;

/// <summary>What the administrative endpoint reports about the caller of a request.</summary>
/// <param name="Service">The product answering, which is what tells a successful sign-in from having reached something else that returns JSON.</param>
/// <param name="Version">The running version.</param>
/// <param name="Credential">The name the deployment knows the presented credential by.</param>
/// <param name="Permissions">The administrative permissions the credential holds over the whole deployment, which is what decides every command a narrower scope is not admitted on.</param>
/// <param name="Scopes">Each scope the credential holds anything at, with what it holds there.</param>
/// <param name="User">The user the caller administers as.</param>
/// <param name="Roles">The roles that user holds and where, before the credential narrows them.</param>
/// <remarks>
/// Every member is nullable because this describes what came off the wire rather than what a deployment sends: the body
/// is read before anything has established that the address is MailFathom at all. An absent list is therefore "the body
/// stated none" and an empty one is "the credential holds none", which are different answers and are reported as such.
/// </remarks>
internal sealed record AdminSession(
    [property: JsonPropertyName("service")] string? Service,
    [property: JsonPropertyName("version")] string? Version,
    [property: JsonPropertyName("credential")] string? Credential,
    [property: JsonPropertyName("permissions")] IReadOnlyList<string>? Permissions,
    [property: JsonPropertyName("scopes")] IReadOnlyList<AdminSessionScope>? Scopes,
    [property: JsonPropertyName("user")] AdminSessionUser? User = null,
    [property: JsonPropertyName("roles")] IReadOnlyList<AdminSessionRole>? Roles = null);

/// <summary>One scope the caller holds anything at, as the session reports it.</summary>
/// <param name="Scope">The kind of scope: <c>deployment</c>, <c>organization</c>, or <c>user</c>.</param>
/// <param name="Target">The organization or user the scope names, absent for the deployment.</param>
/// <param name="Permissions">The names granted there other than those only the deployment scope grants; at a narrower scope each admits the routes checked at a scope covering what they name, for a target that scope covers, and the listings of users, of organizations, of mail accounts, of their synchronization status, of groups, and of role assignments, which answer with what that scope covers, beside the list of roles; it admits no other route naming no target.</param>
/// <param name="ReachingNothing">The names granted there that only the deployment scope grants, so at a narrower scope they reach nothing.</param>
internal sealed record AdminSessionScope(
    [property: JsonPropertyName("scope")] string? Scope,
    [property: JsonPropertyName("target")] Guid? Target,
    [property: JsonPropertyName("permissions")] IReadOnlyList<string>? Permissions,
    [property: JsonPropertyName("reachingNothing")] IReadOnlyList<string>? ReachingNothing);

/// <summary>The user the administrative endpoint says the caller administers as.</summary>
/// <param name="Id">The user's identifier, which every other command names a user by.</param>
/// <param name="DisplayName">What the user is called, absent where the deployment stated none.</param>
internal sealed record AdminSessionUser(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("displayName")] string? DisplayName)
{
    /// <summary>Names the user the way an operator reads them: by what they are called, with the identifier commands take.</summary>
    /// <returns>The description.</returns>
    /// <remarks>The display name is free text an administrator recorded and arrives from a body read before anything established the address is MailFathom, so it is reduced to printable text before it reaches a terminal.</remarks>
    internal string Describe() => ConsoleSafeText.Sanitize(this.DisplayName) is { } displayName
        ? $"{displayName} ({this.Id:D})"
        : $"{this.Id:D}";
}

/// <summary>One role the caller's user holds, and what the assignment giving it reaches.</summary>
/// <param name="Role">The role's name.</param>
/// <param name="Scope"><c>deployment</c>, <c>organization</c>, or <c>user</c>.</param>
/// <param name="Target">The organization or the user the scope names, absent for the deployment.</param>
internal sealed record AdminSessionRole(
    [property: JsonPropertyName("role")] string? Role,
    [property: JsonPropertyName("scope")] string? Scope,
    [property: JsonPropertyName("target")] Guid? Target)
{
    /// <summary>Names the role and the scope it is held at.</summary>
    /// <returns>The description, such as <c>auditor over organization 0199…</c>.</returns>
    /// <remarks>Both words come off the wire, a role name being free text an administrator wrote, so each is reduced to printable text first.</remarks>
    internal string Describe() => this.Target is { } target
        ? $"{ConsoleSafeText.Sanitize(this.Role)} over {ConsoleSafeText.Sanitize(this.Scope)} {target:D}"
        : $"{ConsoleSafeText.Sanitize(this.Role)} over the {ConsoleSafeText.Sanitize(this.Scope)}";
}
