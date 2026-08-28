// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Security.Transport;
using MailFathom.Infrastructure.Security.Transport;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Net.Http.Headers;

namespace MailFathom.Host.Security.Endpoints;

/// <summary>Composes the credentials the client endpoint accepts, and what it tells a browser it may read.</summary>
/// <remarks>
/// <para>
/// Almost nothing about credentials is here, which is the point. Every rule about which ones are accepted and what makes
/// a caller authorized already exists once, in <see cref="TransportSecurityExtensions" />, and this hands it the client
/// surface and that surface's own settings. Nothing about API-key comparison or token validation is restated, so a
/// change to either reaches every endpoint.
/// </para>
/// <para>
/// What is this surface's own is the CORS policy, and it is the one control the administrative endpoint has no use for:
/// its clients are command-line tools with no origin to be told anything, while this endpoint is called by a page and a
/// preflight it cannot answer is a client that never starts. It is not the origin validation the MCP surface performs
/// beside its own policy — that check belongs to the Streamable HTTP transport's own requirement, and this surface
/// speaks no such protocol.
/// </para>
/// <para>
/// A protected resource metadata document is published as well, by
/// <see cref="Api.ProtectedResourceMetadataEndpoint" /> rather than from here and for the reason the administrative
/// surface's is: it belongs to no authentication scheme, since its whole purpose is to answer a caller that has not
/// authenticated.
/// </para>
/// </remarks>
internal static class ClientTransportSecurityExtensions
{
    /// <summary>The CORS policy the client endpoint requires, named so the endpoint asks for this one rather than a default.</summary>
    /// <remarks>Named separately from the MCP endpoint's, because an endpoint resolves exactly one policy and two surfaces sharing one would let either deployment's origins decide what the other answers.</remarks>
    internal const string CorsPolicyName = "MailFathomClientEndpoint";

    /// <summary>Adds the CORS policy, the authentication schemes, and the authorization requirement the client endpoint runs under.</summary>
    /// <param name="services">The container to add to.</param>
    /// <param name="endpointSettings">The endpoint settings composition read.</param>
    /// <returns>The container, so composition reads as one sequence.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services" /> or <paramref name="endpointSettings" /> is <see langword="null" />.</exception>
    internal static IServiceCollection AddClientTransportSecurity(
        this IServiceCollection services,
        ClientEndpointOptions endpointSettings)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(endpointSettings);

        // The policy is built from the configured origins and registered under this surface's own name. Unlike the MCP
        // surface it is not also registered as a service: the only consumer of that registration is the origin
        // validation middleware, which this surface does not run, and a second instance of one type in the container
        // would leave which surface's origins that middleware read decided by registration order.
        var originPolicy = endpointSettings.Cors.ToOriginPolicy();

        services.AddCors(corsOptions => corsOptions.AddPolicy(
            CorsPolicyName,
            policy => ConfigureCorsPolicy(policy, originPolicy)));

        if (!endpointSettings.RequiresAuthentication)
        {
            return services;
        }

        services.AddOwnerFacingTransportAuthentication(
            TransportSurface.Client,
            [.. endpointSettings.Authentication],
            ChallengeSchemeFor(endpointSettings));

        return services;
    }

    /// <summary>Names the registered scheme that answers a request presenting no credential at all.</summary>
    /// <remarks>
    /// <para>
    /// It has to be a scheme this surface actually registered, or the challenge forwards to nothing. Three of the four
    /// challenge identically — a bare bearer challenge is all a client holding a key, an assertion, or a token needs,
    /// since each of them sets its credential deliberately — so which of those three answers is a matter of which is
    /// certain to exist rather than of what a client is told. RFC 9728 lets a challenge point at the metadata document,
    /// and this one does not need to: a client here reaches the document by appending the route prefix it is already
    /// calling, which is what the resource identifier is validated against at startup, so nothing about authorizing
    /// depends on the wording of a refusal.
    /// </para>
    /// <para>
    /// Basic is the exception and is therefore answered first where it is configured. A password is typed by a person,
    /// and a client only asks for one when a <c>WWW-Authenticate: Basic</c> challenge tells it to, so a surface
    /// accepting passwords whose refusal named no Basic challenge would be a surface nobody could sign in to without
    /// knowing in advance that they could. Its challenge carries the bare bearer one beside it, so the other three
    /// methods are told exactly what they would have been told without it.
    /// </para>
    /// <para>
    /// One scheme always exists to name: an endpoint reaching this point configured at least one method, and
    /// configuration validation refuses OAuth with no authorization server behind it.
    /// </para>
    /// </remarks>
    private static string ChallengeSchemeFor(ClientEndpointOptions endpointSettings)
    {
        if (endpointSettings.AllowsBasic)
        {
            return TransportSurface.Client.BasicSchemeName;
        }

        if (endpointSettings.AllowsApiKey)
        {
            return TransportSurface.Client.ApiKeySchemeName;
        }

        return endpointSettings.AllowsClientAssertion
            ? TransportSurface.Client.ClientAssertionSchemeName
            : TransportSurface.Client.OAuthSchemeNameFor(
                endpointSettings.OAuthMethods()[0].AuthorizationServers[0].Name!);
    }

    /// <summary>Builds the CORS policy from the configured origins.</summary>
    /// <remarks>
    /// <para>
    /// A policy that names no origin at all is the deliberate third posture rather than an oversight: it advertises
    /// nothing to a browser, which is what a deployment whose client is a desktop or mobile head — neither of which is
    /// subject to CORS — wants.
    /// </para>
    /// <para>
    /// Credentials are never allowed, under any policy. A browser that could attach an ambient cookie would let a page
    /// act as whoever is logged in somewhere else, and this surface has no use for one anyway: its credential is a
    /// bearer token the client sets deliberately. Allowing any origin and allowing credentials is also a combination the
    /// CORS specification forbids outright.
    /// </para>
    /// <para>
    /// The methods and headers are what this surface serves rather than what an HTTP API might: one read, one
    /// <c>Authorization</c> header, and JSON. A route added later adds its method here, which is the direction that
    /// fails visibly — a policy written wide in advance is a policy nobody narrows afterwards.
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

        policy
            .WithMethods(HttpMethods.Get)
            .WithHeaders(HeaderNames.Authorization, HeaderNames.ContentType, HeaderNames.Accept)

            // A refusal says where to authorize, and a browser cannot read a response header the policy does not name.
            // Without this the one answer that tells a page how to proceed is the one it cannot see, and a client that
            // could have started discovery only learns that something failed.
            .WithExposedHeaders(HeaderNames.WWWAuthenticate);
    }
}
