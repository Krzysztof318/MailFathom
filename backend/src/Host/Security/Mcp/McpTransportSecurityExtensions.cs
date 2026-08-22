// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Access;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Security.Transport;
using MailFathom.Infrastructure.Security.ClientCertificates;
using MailFathom.Infrastructure.Security.Transport;
using MailFathom.Mcp.Tools.Categories;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Net.Http.Headers;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Authentication;

namespace MailFathom.Host.Security.Mcp;

/// <summary>Composes the controls that stand in front of the MCP endpoint.</summary>
/// <remarks>
/// <para>
/// The three controls answer three different questions and are wired separately on purpose. Authentication decides
/// whether a caller is one this deployment serves, CORS decides what a browser is allowed to read of the answer, and
/// the origin check decides whether a request a browser was talked into making is served at all. None of them stands in
/// for another, and a deployment can be narrow on one while wide on the next.
/// </para>
/// <para>
/// Only what is MCP's own is here. Which credentials are accepted, and what makes a caller authorized, are questions
/// every protected surface asks, so they are registered through <see cref="TransportSecurityExtensions" /> with the MCP
/// surface handed in. What stays is the part that exists because this endpoint speaks a protocol to browsers and to MCP
/// clients: the CORS policy, the client-certificate handshake, the discovery document the MCP authorization
/// specification defines, and the refusal a client reads when its token lacks a scope.
/// </para>
/// </remarks>
internal static class McpTransportSecurityExtensions
{
    /// <summary>The CORS policy the MCP endpoint requires, named so the endpoint asks for this one rather than a default.</summary>
    internal const string CorsPolicyName = "MailFathomEndpoint";

    private const string McpSessionHeaderName = "Mcp-Session-Id";

    private const string McpProtocolVersionHeaderName = "MCP-Protocol-Version";

    private const string LastEventHeaderName = "Last-Event-ID";

    /// <summary>Adds the authentication schemes, the authorization requirement, and the CORS policy the endpoint runs under.</summary>
    /// <param name="services">The container to add to.</param>
    /// <param name="endpointSettings">The endpoint settings composition read.</param>
    /// <returns>The container, so composition reads as one sequence.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services" /> or <paramref name="endpointSettings" /> is <see langword="null" />.</exception>
    internal static IServiceCollection AddMcpTransportSecurity(
        this IServiceCollection services,
        McpEndpointOptions endpointSettings)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(endpointSettings);

        // Mapped once and shared, so the origins the CORS response advertises and the origins the endpoint actually
        // serves cannot drift apart: they are the same object rather than two readings of the same settings.
        var originPolicy = endpointSettings.Cors.ToOriginPolicy();

        services.AddSingleton(originPolicy);
        services.AddCors(corsOptions => corsOptions.AddPolicy(
            CorsPolicyName,
            policy => ConfigureCorsPolicy(policy, originPolicy)));

        if (endpointSettings.ClientCertificateProfiles.Count > 0)
        {
            services.AddSingleton<McpClientCertificateAuthenticator>();
        }

        if (!endpointSettings.RequiresAuthentication)
        {
            return services;
        }

        // A challenge is answered by one scheme whichever credential was presented, because a request that has nothing
        // to authenticate with has told us nothing about which kind of credential it was going to use. With OAuth turned
        // on that is the MCP scheme, whose challenge carries the metadata document a client needs to begin authorizing;
        // otherwise it is a bare bearer challenge, from whichever of the two remaining schemes this endpoint registered.
        var challengeSchemeName = ChallengeSchemeFor(endpointSettings);

        var oauthMethods = endpointSettings.OAuthMethods();

        var authentication = services.AddTransportAuthentication(
            TransportSurface.Mcp,
            [.. endpointSettings.Authentication],
            challengeSchemeName);

        if (oauthMethods.Count > 0)
        {
            AddProtectedResourceMetadataScheme(authentication, [.. endpointSettings.Authentication]);
            AddInsufficientScopeRefusal(services, oauthMethods);
        }

        return services;
    }

    /// <summary>Names the registered scheme that answers a request presenting no credential at all.</summary>
    /// <remarks>
    /// <para>
    /// It has to be a scheme this endpoint actually registered, or the challenge forwards to nothing. All three
    /// challenge with a bearer scheme, so which one answers decides what a client is told only in the OAuth case, where
    /// the challenge carries the metadata document.
    /// </para>
    /// <para>
    /// It is also what a credential this endpoint cannot place authenticates against, so whichever scheme is named here
    /// must authenticate nobody rather than judge something. All three do: the API key and assertion handlers read no
    /// credential out of such a request, and the MCP scheme performs no authentication at all once
    /// <see cref="AddProtectedResourceMetadataScheme" /> has cleared the forwarding the SDK sets by default.
    /// </para>
    /// </remarks>
    private static string ChallengeSchemeFor(McpEndpointOptions endpointSettings)
    {
        if (endpointSettings.AllowsOAuth)
        {
            return McpAuthenticationDefaults.AuthenticationScheme;
        }

        return endpointSettings.AllowsApiKey
            ? TransportSurface.Mcp.ApiKeySchemeName
            : TransportSurface.Mcp.ClientAssertionSchemeName;
    }

    /// <summary>Registers the scheme publishing the RFC 9728 document an MCP client discovers its authorization server through.</summary>
    /// <remarks>
    /// What the configured entries publish between them is <see cref="PublishedOAuthMetadata" />'s to decide, because
    /// the administrative endpoint publishes the same document through a record of this repository's own and the two
    /// must not answer differently from one configuration. What is the SDK's own is the type this fills in.
    /// </remarks>
    private static void AddProtectedResourceMetadataScheme(
        AuthenticationBuilder authentication,
        IReadOnlyList<TransportAuthenticationOptions> methods)
    {
        var published = PublishedOAuthMetadata.For(methods, McpEndpointOptions.GrantedSurface);

        authentication.AddMcp(mcpOptions =>
        {
            // This scheme answers the challenge and publishes the document; it judges no credential, and its handler
            // authenticates nobody by design. The SDK's options nevertheless forward authentication to JwtBearer's own
            // default scheme name, which this host never registers — its validators are named for the authorization
            // server each one speaks for. Every request that presents nothing this endpoint can place is routed here,
            // starting with the unauthenticated request every MCP client opens with, so leaving the forwarding in place
            // answers discovery with a fault instead of the refusal that carries the pointer below.
            mcpOptions.ForwardAuthenticate = null;

            // Absolute and configured, never derived from the request. Left unset, the SDK composes both this address
            // and the resource it advertises from the request's scheme and Host header, so a deployment behind a proxy
            // would tell each client to authenticate for whichever name that client arrived under.
            mcpOptions.ResourceMetadataUri = new Uri(ProtectedResourceMetadataAddress.AddressFor(published.Resource));
            mcpOptions.ResourceMetadata = new ProtectedResourceMetadata
            {
                Resource = published.Resource,
                AuthorizationServers = [.. published.AuthorizationServers],
                ScopesSupported = [.. published.ScopesSupported],
                BearerMethodsSupported = ["header"],
                ResourceName = "MailFathom",
            };
        });
    }

    /// <summary>Registers the refusal that names the scope an authenticated token was missing.</summary>
    /// <remarks>
    /// Only a required scope can turn an authenticated caller away, so this is registered only where one exists. Without
    /// it the endpoint answers every failure with a challenge, and there would be nothing for this to say.
    /// </remarks>
    private static void AddInsufficientScopeRefusal(
        IServiceCollection services,
        IReadOnlyList<OAuthValidationOptions> oauthMethods)
    {
        if (oauthMethods.All(oauthMethod => oauthMethod.RequiredScopes.Count == 0))
        {
            return;
        }

        services.AddSingleton<IAuthorizationMiddlewareResultHandler>(
            new InsufficientScopeResultHandler(
                TransportAuthenticationConfiguration.RequiredScopesByIssuer(oauthMethods),
                ProtectedResourceMetadataAddress.AddressFor(oauthMethods[0].CanonicalResource())));
    }

    /// <summary>Builds the CORS policy from the configured origins.</summary>
    /// <remarks>
    /// <para>
    /// A policy that names no origin at all is the deliberate third posture rather than an oversight: it advertises
    /// nothing to a browser, which is what a deployment whose only clients are agents and command-line tools wants.
    /// </para>
    /// <para>
    /// Credentials are never allowed, under any policy. A browser that could attach an ambient cookie to an MCP
    /// request would let a page act as whoever is logged in somewhere else, and the endpoint has no use for one anyway:
    /// its credential is a bearer token a client sets deliberately. Allowing any origin and allowing credentials is
    /// also a combination the CORS specification forbids outright.
    /// </para>
    /// </remarks>
    private static void ConfigureCorsPolicy(CorsPolicyBuilder policy, BrowserOriginPolicy originPolicy)
    {
        if (originPolicy.AllowsAnyOrigin)
        {
            policy.AllowAnyOrigin();
        }
        else
        {
            policy.WithOrigins([.. originPolicy.AllowedOrigins]);
        }

        // The Streamable HTTP transport posts JSON-RPC, reads the stream back, and ends a session with a delete; the
        // remaining headers are what an MCP client sets on those requests. Listing them beats AllowAnyHeader, which
        // would also let a browser send whatever a future middleware happens to trust.
        policy
            .WithMethods(HttpMethods.Post, HttpMethods.Get, HttpMethods.Delete)
            .WithHeaders(
                HeaderNames.Authorization,
                HeaderNames.ContentType,
                HeaderNames.Accept,
                HeaderNames.CacheControl,
                LastEventHeaderName,
                McpSessionHeaderName,
                McpProtocolVersionHeaderName,

                // MailFathom's own, and the one header here a client is not obliged to send. Without it a browser
                // client could narrow nothing, because a header the policy does not name is dropped before the endpoint
                // sees it — which would read as the surface ignoring the request rather than as CORS refusing it.
                McpToolCategoryHeader.Name)
            .WithExposedHeaders(
                McpSessionHeaderName,
                McpProtocolVersionHeaderName,

                // A refusal says where to authorize and which scopes are required, and a browser cannot read a response
                // header the policy does not name. Without this the one answer that tells a page how to proceed is the
                // one it cannot see, and a client that could have started discovery only learns that something failed.
                HeaderNames.WWWAuthenticate);
    }
}
