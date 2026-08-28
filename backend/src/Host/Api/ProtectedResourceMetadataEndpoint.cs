// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Access;
using MailFathom.Host.Security.Transport;

namespace MailFathom.Host.Api;

/// <summary>Publishes the RFC 9728 document a client reads before it holds any credential.</summary>
/// <remarks>
/// <para>
/// The document is what makes OAuth sign-in something an operator can run rather than something they have to prepare
/// for. Without it <c>mfctl login</c> would need the issuer, the resource identifier, and the required scopes written on
/// its own command line — three values the deployment already knows and the operator would be copying by hand, with a
/// mistyped one presenting as a token the endpoint refuses for reasons the client cannot see. The mail client cannot be
/// given them by hand at all: it is a page, and the address it was configured with is the only thing it starts from.
/// </para>
/// <para>
/// Served unauthenticated, which is the only way it can be served: its reader is a client that has nothing to
/// authenticate with yet, and a document naming where to obtain a credential that itself required one would answer
/// nobody. Nothing in it is a secret — every field is a deployment's own public name for something, and an operator's
/// own identity provider is not disclosed by an endpoint they chose to expose.
/// </para>
/// <para>
/// Mapped as a route rather than published from an authentication handler, which is where the MCP surface's equivalent
/// comes from. That difference is the MCP SDK's rather than a decision here; every one of these documents is composed
/// from the same configured resource and answers at the same RFC 9728 location relative to it.
/// </para>
/// </remarks>
internal static class ProtectedResourceMetadataEndpoint
{
    /// <summary>Maps one surface's document at the address its resource identifier places it.</summary>
    /// <param name="endpoints">The route builder.</param>
    /// <param name="methods">The endpoint's configured credential entries, in configuration order.</param>
    /// <param name="grantedSurface">The half of the permission vocabulary the endpoint's grants draw from, which decides what an entry narrowed by token scopes advertises.</param>
    /// <returns>The mapped route, so a surface can attach what only its own document needs.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpoints" /> or <paramref name="methods" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when no entry states OAuth, which is a surface accepting no token at all.</exception>
    /// <remarks>The administrative endpoint maps this one; a mail-serving surface maps <see cref="MapOwnerFacingProtectedResourceMetadata" /> beside it, because a token admitted there resolves an owner's credential rather than a configured entry. The surface stays a parameter rather than a constant so neither can publish the other's advertised scopes.</remarks>
    internal static RouteHandlerBuilder MapProtectedResourceMetadata(
        this IEndpointRouteBuilder endpoints,
        IReadOnlyList<TransportAuthenticationOptions> methods,
        ProtectedSurface grantedSurface)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(methods);

        return endpoints.Map(PublishedOAuthMetadata.For(methods, grantedSurface));
    }

    /// <summary>Maps a mail-serving surface's document at the address its resource identifier places it.</summary>
    /// <param name="endpoints">The route builder.</param>
    /// <param name="methods">The methods the endpoint accepts, in configuration order.</param>
    /// <param name="grantedSurface">The half of the permission vocabulary the endpoint's credentials draw from, which decides what an entry narrowed by token scopes advertises.</param>
    /// <returns>The mapped route, so a surface can attach what only its own document needs.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpoints" /> or <paramref name="methods" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when no entry accepts a token, which is a surface accepting none at all.</exception>
    internal static RouteHandlerBuilder MapOwnerFacingProtectedResourceMetadata(
        this IEndpointRouteBuilder endpoints,
        IReadOnlyList<OwnerFacingAuthenticationOptions> methods,
        ProtectedSurface grantedSurface)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(methods);

        return endpoints.Map(PublishedOAuthMetadata.ForOwnerFacing(methods, grantedSurface));
    }

    /// <summary>Maps one composed document, which is where the two axes meet again.</summary>
    private static RouteHandlerBuilder Map(this IEndpointRouteBuilder endpoints, PublishedOAuthMetadata published)
    {
        var document = ProtectedResourceMetadataDocument.For(published);

        return endpoints.MapGet(
            ProtectedResourceMetadataAddress.PathFor(document.Resource),
            () => Results.Ok(document));
    }
}

/// <summary>What a client learns about this resource before it has a credential for it.</summary>
/// <param name="Resource">The identifier a token must be issued for, which the client sends as RFC 8707's <c>resource</c> parameter.</param>
/// <param name="AuthorizationServers">The issuers whose tokens this endpoint accepts, each of which publishes its own discovery document.</param>
/// <param name="ScopesSupported">The scopes a client should ask for, which is what RFC 9728 defines the field as rather than what a token is checked against — so a scope this endpoint advertises without requiring is in it too.</param>
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
    /// <param name="published">What the endpoint's entries publish between them.</param>
    /// <returns>The document.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="published" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// What the entries publish between them is <see cref="PublishedOAuthMetadata" />'s to decide, because the MCP
    /// endpoint publishes the same document through the protocol SDK's own type and the two must not answer differently
    /// from one configuration. What is this record's own is the wire shape: the JSON names RFC 9728 fixes, and the
    /// bearer method and resource name that are constants rather than settings.
    /// </remarks>
    internal static ProtectedResourceMetadataDocument For(PublishedOAuthMetadata published)
    {
        ArgumentNullException.ThrowIfNull(published);

        return new ProtectedResourceMetadataDocument(
            published.Resource,
            published.AuthorizationServers,
            published.ScopesSupported,
            ["header"],
            "MailFathom");
    }
}
