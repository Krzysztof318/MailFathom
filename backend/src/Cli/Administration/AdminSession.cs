// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration;

/// <summary>What the administrative endpoint reports about the caller of a request.</summary>
/// <param name="Service">The product answering, which is what tells a successful sign-in from having reached something else that returns JSON.</param>
/// <param name="Version">The running version.</param>
/// <param name="Credential">The name the deployment knows the presented credential by.</param>
/// <param name="Permissions">The administrative permissions the credential holds over the whole deployment, which is what decides every command naming no target.</param>
/// <param name="Scopes">Each scope the credential holds anything at, with what it holds there.</param>
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
    [property: JsonPropertyName("scopes")] IReadOnlyList<AdminSessionScope>? Scopes);

/// <summary>One scope the caller holds anything at, as the session reports it.</summary>
/// <param name="Scope">The kind of scope: <c>deployment</c>, <c>organization</c>, or <c>user</c>.</param>
/// <param name="Target">The organization or user the scope names, absent for the deployment.</param>
/// <param name="Permissions">The names held there that reach an operation there.</param>
/// <param name="ReachingNothing">The names held there that no operation below the deployment covers.</param>
internal sealed record AdminSessionScope(
    [property: JsonPropertyName("scope")] string? Scope,
    [property: JsonPropertyName("target")] Guid? Target,
    [property: JsonPropertyName("permissions")] IReadOnlyList<string>? Permissions,
    [property: JsonPropertyName("reachingNothing")] IReadOnlyList<string>? ReachingNothing);
