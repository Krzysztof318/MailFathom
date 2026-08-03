// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Access;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Security.Transport;
using MailFathom.Infrastructure.Security.ClientCertificates;
using MailFathom.Infrastructure.Security.Transport;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Server.Kestrel.Https;
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
        // otherwise it is the API key scheme's bare bearer challenge.
        var challengeSchemeName = endpointSettings.AllowsOAuth
            ? McpAuthenticationDefaults.AuthenticationScheme
            : TransportSurface.Mcp.ApiKeySchemeName;

        var authentication = services.AddTransportAuthentication(
            TransportSurface.Mcp,
            endpointSettings.Authentication,
            [.. endpointSettings.ApiKeys],
            endpointSettings.OAuth,
            challengeSchemeName);

        if (endpointSettings.AllowsOAuth)
        {
            AddProtectedResourceMetadataScheme(authentication, endpointSettings.OAuth);
            AddInsufficientScopeRefusal(services, endpointSettings.OAuth);
        }

        return services;
    }

    /// <summary>Asks every HTTPS connection for a client certificate and leaves the decision to the trust profiles.</summary>
    /// <param name="webHost">The web host being configured.</param>
    /// <returns>The web host, so composition reads as one sequence.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="webHost" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// A certificate has to be asked for during the handshake or it never arrives, and it is asked for rather than
    /// demanded so that a client without one reaches the middleware and is refused there. A handshake failure would say
    /// nothing to the operator reading a log and nothing to a client that could act on it.
    /// </para>
    /// <para>
    /// The connection-level validation accepts every certificate on purpose, and it grants nothing by doing so. Kestrel
    /// would otherwise validate against the machine's own trust store, which is both too narrow and too wide for this
    /// design: it would fail the handshake for the private authority a profile names, and it would accept a certificate
    /// from any public authority the machine happens to trust. Whether a certificate is trusted is
    /// <see cref="McpClientCertificateValidation" />'s decision, made against the profile's anchors, and this line
    /// exists so that decision is reached at all.
    /// </para>
    /// </remarks>
    internal static IWebHostBuilder RequestMcpClientCertificates(this IWebHostBuilder webHost)
    {
        ArgumentNullException.ThrowIfNull(webHost);

        return webHost.ConfigureKestrel(kestrel => kestrel.ConfigureHttpsDefaults(https =>
        {
            https.ClientCertificateMode = ClientCertificateMode.AllowCertificate;
            https.ClientCertificateValidation = static (_, _, _) => true;
        }));
    }

    /// <summary>Registers the scheme publishing the RFC 9728 document an MCP client discovers its authorization server through.</summary>
    private static void AddProtectedResourceMetadataScheme(
        AuthenticationBuilder authentication,
        OAuthValidationOptions oauthSettings) =>
        authentication.AddMcp(mcpOptions =>
        {
            // Absolute and configured, never derived from the request. Left unset, the SDK composes both this address
            // and the resource it advertises from the request's scheme and Host header, so a deployment behind a proxy
            // would tell each client to authenticate for whichever name that client arrived under.
            mcpOptions.ResourceMetadataUri = new Uri(
                McpProtectedResourceMetadata.AddressFor(oauthSettings.CanonicalResource()));
            mcpOptions.ResourceMetadata = new ProtectedResourceMetadata
            {
                Resource = oauthSettings.CanonicalResource(),
                AuthorizationServers = [.. oauthSettings.AuthorizationServers.Select(server => server.ValidatedIssuer())],
                ScopesSupported = [.. oauthSettings.RequiredScopes],
                BearerMethodsSupported = ["header"],
                ResourceName = "MailFathom",
            };
        });

    /// <summary>Registers the refusal that names the scope an authenticated token was missing.</summary>
    /// <remarks>
    /// Only a required scope can turn an authenticated caller away, so this is registered only where one exists. Without
    /// it the endpoint answers every failure with a challenge, and there would be nothing for this to say.
    /// </remarks>
    private static void AddInsufficientScopeRefusal(IServiceCollection services, OAuthValidationOptions oauthSettings)
    {
        if (oauthSettings.RequiredScopes.Count == 0)
        {
            return;
        }

        services.AddSingleton<IAuthorizationMiddlewareResultHandler>(
            new InsufficientScopeResultHandler(
                [.. oauthSettings.RequiredScopes],
                McpProtectedResourceMetadata.AddressFor(oauthSettings.CanonicalResource())));
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
    private static void ConfigureCorsPolicy(CorsPolicyBuilder policy, McpOriginPolicy originPolicy)
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
                McpProtocolVersionHeaderName)
            .WithExposedHeaders(
                McpSessionHeaderName,
                McpProtocolVersionHeaderName,

                // A refusal says where to authorize and which scopes are required, and a browser cannot read a response
                // header the policy does not name. Without this the one answer that tells a page how to proceed is the
                // one it cannot see, and a client that could have started discovery only learns that something failed.
                HeaderNames.WWWAuthenticate);
    }
}
