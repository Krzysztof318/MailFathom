// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace MailFathom.Cli.Authorization;

/// <summary>The subset of an RFC 9728 protected resource metadata document the command reads.</summary>
/// <param name="Resource">The identifier a token must be issued for, sent back as RFC 8707's <c>resource</c> parameter.</param>
/// <param name="AuthorizationServers">The issuers whose tokens the deployment accepts.</param>
/// <param name="ScopesSupported">The scopes the deployment tells a client to ask for, which the command asks for verbatim rather than adding to.</param>
/// <remarks>
/// Every member is optional because the document comes from a machine this process does not own — a proxy, a captive
/// portal, and a deployment that serves no administrative surface all answer this address with something. What makes a
/// document usable is checked once, in <see cref="DeploymentAuthorizationDiscovery" />, rather than by each reader.
/// </remarks>
[SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "The values are JSON fields read verbatim from a deployment. Binding them as Uri would move a malformed value's failure inside deserialization, where it arrives as a format exception rather than as the message an operator can act on.")]
internal sealed record ProtectedResourceMetadata(
    [property: JsonPropertyName("resource")] string? Resource,
    [property: JsonPropertyName("authorization_servers")] IReadOnlyList<string>? AuthorizationServers,
    [property: JsonPropertyName("scopes_supported")] IReadOnlyList<string>? ScopesSupported);

/// <summary>The subset of an authorization server's discovery document the command reads.</summary>
/// <param name="Issuer">The issuer the document reports, which must equal the one that led here.</param>
/// <param name="AuthorizationEndpoint">Where a person is sent to approve the sign-in.</param>
/// <param name="TokenEndpoint">Where an authorization code, a device code, or a refresh token is exchanged.</param>
/// <param name="DeviceAuthorizationEndpoint">Where a device code is requested, absent from a server that issues none.</param>
/// <remarks>
/// Read from whichever of OAuth 2.0 Authorization Server Metadata and OpenID Connect Discovery the server publishes.
/// The two describe the same fields for everything the command needs, so one shape reads either.
/// </remarks>
[SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "The values are JSON fields read verbatim from an authorization server this process does not own. Binding them as Uri would move a malformed value's failure inside deserialization, where it arrives as a format exception rather than as the message an operator can act on.")]
internal sealed record AuthorizationServerMetadata(
    [property: JsonPropertyName("issuer")] string? Issuer,
    [property: JsonPropertyName("authorization_endpoint")] string? AuthorizationEndpoint,
    [property: JsonPropertyName("token_endpoint")] string? TokenEndpoint,
    [property: JsonPropertyName("device_authorization_endpoint")] string? DeviceAuthorizationEndpoint);

/// <summary>Everything a sign-in needs, once both documents have been read and found usable.</summary>
/// <param name="Issuer">The authorization server that will issue the token.</param>
/// <param name="AuthorizationEndpoint">Where a person approves the sign-in, absent from a server offering no such grant.</param>
/// <param name="TokenEndpoint">Where every grant is exchanged, and where a refresh token is later spent.</param>
/// <param name="DeviceAuthorizationEndpoint">Where a device code is requested, absent from a server that issues none.</param>
/// <param name="Resource">The identifier the issued token's audience must name.</param>
/// <param name="Scope">The scopes to ask for, space separated as RFC 6749 requires.</param>
/// <remarks>
/// Nothing here was configured on the command line beyond the client identifier: every value came from the deployment
/// or from the server the deployment named. That is what keeps a sign-in from depending on an operator transcribing
/// four values correctly, and it is why a deployment that moves an endpoint keeps working.
/// </remarks>
internal sealed record DeploymentAuthorization(
    string Issuer,
    Uri? AuthorizationEndpoint,
    Uri TokenEndpoint,
    Uri? DeviceAuthorizationEndpoint,
    string Resource,
    string Scope);
