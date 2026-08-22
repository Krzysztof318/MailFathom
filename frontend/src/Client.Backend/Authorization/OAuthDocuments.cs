// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace MailFathom.Client.Backend.Authorization;

/// <summary>The subset of an RFC 9728 protected resource metadata document this client reads.</summary>
/// <param name="Resource">The identifier a token must be issued for, sent back as RFC 8707's <c>resource</c> parameter.</param>
/// <param name="AuthorizationServers">The issuers whose tokens the deployment accepts.</param>
/// <param name="ScopesSupported">The scopes the deployment tells a client to ask for, which this client asks for verbatim rather than adding to.</param>
/// <remarks>
/// Every member is optional because the document comes from a machine this process does not own — a proxy, a captive
/// portal, and a deployment serving no client surface all answer this address with something. What makes a document
/// usable is checked once, in <see cref="DeploymentAuthorizationDiscovery" />, rather than by each reader.
/// </remarks>
[SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "The values are JSON fields read verbatim from a deployment. Binding them as Uri would move a malformed value's failure inside deserialization, where it arrives as a format exception rather than as the failure a screen can act on.")]
internal sealed record ProtectedResourceMetadata(
    [property: JsonPropertyName("resource")] string? Resource,
    [property: JsonPropertyName("authorization_servers")] IReadOnlyList<string>? AuthorizationServers,
    [property: JsonPropertyName("scopes_supported")] IReadOnlyList<string>? ScopesSupported);

/// <summary>The subset of an authorization server's discovery document this client reads.</summary>
/// <param name="Issuer">The issuer the document reports, which must equal the one that led here.</param>
/// <param name="AuthorizationEndpoint">Where a person is sent to approve the sign-in.</param>
/// <param name="TokenEndpoint">Where the authorization code is exchanged.</param>
/// <remarks>
/// Read from whichever of OAuth 2.0 Authorization Server Metadata and OpenID Connect Discovery the server publishes;
/// the two describe the same fields for everything a browser sign-in needs, so one shape reads either. The device
/// grant the command-line tool also offers has no counterpart here, so no device authorization endpoint is read: a
/// client with a window has a browser by definition.
/// </remarks>
[SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "The values are JSON fields read verbatim from an authorization server this process does not own. Binding them as Uri would move a malformed value's failure inside deserialization, where it arrives as a format exception rather than as the failure a screen can act on.")]
internal sealed record AuthorizationServerMetadata(
    [property: JsonPropertyName("issuer")] string? Issuer,
    [property: JsonPropertyName("authorization_endpoint")] string? AuthorizationEndpoint,
    [property: JsonPropertyName("token_endpoint")] string? TokenEndpoint);

/// <summary>What a token endpoint answers, in either of the two shapes RFC 6749 defines for it.</summary>
/// <param name="AccessToken">The credential every request presents, absent from a refusal.</param>
/// <param name="ExpiresInSeconds">How long the access token remains acceptable, absent from a server that states no lifetime.</param>
/// <param name="Error">The machine-readable refusal code, absent from a grant that was issued.</param>
/// <remarks>
/// One record for both, because RFC 6749 requires a refused grant to arrive as <c>400</c> with a machine-readable
/// <c>error</c> and the status code alone therefore says less than the body does. No refresh token is read: this
/// client keeps nothing that outlives the process, so a credential whose only purpose is to survive one would be
/// something to hold and never spend.
/// </remarks>
internal sealed record OAuthTokenResponse(
    [property: JsonPropertyName("access_token")] string? AccessToken,
    [property: JsonPropertyName("expires_in")] int? ExpiresInSeconds,
    [property: JsonPropertyName("error")] string? Error)
{
    /// <inheritdoc />
    /// <remarks>Redacted by construction, because the access token is a credential.</remarks>
    public override string ToString() => "***";
}
