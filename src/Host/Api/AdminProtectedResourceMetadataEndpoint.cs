// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;
using MailFathom.Host.Configuration.Access;
using MailFathom.Host.Security.Transport;

namespace MailFathom.Host.Api;

/// <summary>Publishes the RFC 9728 document <c>mfctl login</c> reads before it holds any credential.</summary>
/// <remarks>
/// <para>
/// The document is what makes OAuth sign-in something an operator can run rather than something they have to prepare
/// for. Without it the command would need the issuer, the resource identifier, and the required scopes written on its
/// own command line — three values the deployment already knows and the operator would be copying by hand, with a
/// mistyped one presenting as a token the endpoint refuses for reasons the client cannot see.
/// </para>
/// <para>
/// Served unauthenticated, which is the only way it can be served: its reader is a client that has nothing to
/// authenticate with yet, and a document naming where to obtain a credential that itself required one would answer
/// nobody. Nothing in it is a secret — every field is a deployment's own public name for something, and an operator's
/// own identity provider is not disclosed by an endpoint they chose to expose.
/// </para>
/// <para>
/// Mapped as a route rather than published from an authentication handler, which is where the MCP surface's equivalent
/// comes from. That difference is the MCP SDK's rather than a decision here; both documents are composed from the same
/// configured resource and answer at the same RFC 9728 location relative to it.
/// </para>
/// </remarks>
internal static class AdminProtectedResourceMetadataEndpoint
{
    /// <summary>Maps the document at the address its resource identifier places it.</summary>
    /// <param name="endpoints">The route builder.</param>
    /// <param name="oauthSettings">The endpoint's authorization servers and token requirements.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpoints" /> or <paramref name="oauthSettings" /> is <see langword="null" />.</exception>
    internal static void MapAdminProtectedResourceMetadata(
        this IEndpointRouteBuilder endpoints,
        OAuthValidationOptions oauthSettings)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(oauthSettings);

        var document = ProtectedResourceMetadataDocument.For(oauthSettings);

        endpoints.MapGet(
            ProtectedResourceMetadataAddress.PathFor(oauthSettings.CanonicalResource()),
            () => Results.Ok(document));
    }
}

/// <summary>What a client learns about this resource before it has a credential for it.</summary>
/// <param name="Resource">The identifier a token must be issued for, which the client sends as RFC 8707's <c>resource</c> parameter.</param>
/// <param name="AuthorizationServers">The issuers whose tokens this endpoint accepts, each of which publishes its own discovery document.</param>
/// <param name="ScopesSupported">The scopes a token must carry, so a client asks for what it will need rather than for everything.</param>
/// <param name="BearerMethodsSupported">How a token may be presented; the header alone, because a credential in a query reaches every access log on the path.</param>
/// <param name="ResourceName">The product's own name, which is what a consent screen shows the person approving.</param>
/// <remarks>
/// A record of this repository's own rather than the MCP SDK's <c>ProtectedResourceMetadata</c>. The two describe the
/// same specification, and taking the SDK's type would make the administrative surface depend on the MCP protocol
/// library for a document that has nothing to do with the protocol — a dependency that would then be load-bearing for
/// signing in to a deployment whose MCP endpoint may be turned off entirely.
/// </remarks>
internal sealed record ProtectedResourceMetadataDocument(
    [property: JsonPropertyName("resource")] string Resource,
    [property: JsonPropertyName("authorization_servers")] IReadOnlyList<string> AuthorizationServers,
    [property: JsonPropertyName("scopes_supported")] IReadOnlyList<string> ScopesSupported,
    [property: JsonPropertyName("bearer_methods_supported")] IReadOnlyList<string> BearerMethodsSupported,
    [property: JsonPropertyName("resource_name")] string ResourceName)
{
    /// <summary>Describes what one endpoint's OAuth settings publish.</summary>
    /// <param name="oauthSettings">The endpoint's authorization servers and token requirements.</param>
    /// <returns>The document.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="oauthSettings" /> is <see langword="null" />.</exception>
    internal static ProtectedResourceMetadataDocument For(OAuthValidationOptions oauthSettings)
    {
        ArgumentNullException.ThrowIfNull(oauthSettings);

        return new ProtectedResourceMetadataDocument(
            oauthSettings.CanonicalResource(),
            [.. oauthSettings.AuthorizationServers.Select(authorizationServer => authorizationServer.ValidatedIssuer())],
            [.. oauthSettings.RequiredScopes],
            ["header"],
            "MailFathom");
    }
}
