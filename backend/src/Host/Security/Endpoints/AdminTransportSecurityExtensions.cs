// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Security.Transport;
using MailFathom.Infrastructure.Security.Transport;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Net.Http.Headers;

namespace MailFathom.Host.Security.Endpoints;

/// <summary>Composes the credentials the administrative endpoint accepts, and what it tells a browser it may read.</summary>
/// <remarks>
/// There is almost nothing here about credentials, which is the point. Every rule about which ones are accepted and
/// what makes a caller authorized already exists once, in <see cref="TransportSecurityExtensions" />, and this hands
/// it the administrative surface and that surface's own settings. Nothing about API-key comparison or token
/// validation is restated, so a change to either reaches every endpoint.
/// <para>
/// The CORS policy is this surface's own, named separately from the MCP and client policies, because an endpoint
/// resolves exactly one and two surfaces sharing one would let either deployment's origins decide what the other
/// answers. The default is every origin, which is what a first run and a local orchestration need; an operator who
/// knows the origin they serve names it. Unlike the MCP surface the origin policy is not also registered as a
/// service: the only consumer of that registration is the origin validation middleware, which this surface does not
/// run.
/// </para>
/// <para>
/// A protected resource metadata document *is* published, by
/// <see cref="Api.ProtectedResourceMetadataEndpoint" /> rather than from here, because <c>mfctl login</c> is
/// exactly the client this surface once had none of: one that arrives holding nothing and has to find out where to
/// authorize. It is mapped as a route instead of registered here because it belongs to no authentication scheme —
/// its whole purpose is to answer a caller that has not authenticated.
/// </para>
/// </remarks>
internal static class AdminTransportSecurityExtensions
{
    /// <summary>The CORS policy the administrative endpoint requires, named so the endpoint asks for this one rather than a default.</summary>
    internal const string CorsPolicyName = "MailFathomAdminEndpoint";

    /// <summary>Adds the CORS policy, the authentication schemes, and the authorization requirement the administrative endpoint runs under.</summary>
    /// <param name="services">The container to add to.</param>
    /// <param name="endpointSettings">The endpoint settings composition read.</param>
    /// <returns>The container, so composition reads as one sequence.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services" /> or <paramref name="endpointSettings" /> is <see langword="null" />.</exception>
    internal static IServiceCollection AddAdminTransportSecurity(
        this IServiceCollection services,
        AdminEndpointOptions endpointSettings)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(endpointSettings);

        var originPolicy = endpointSettings.Cors.ToOriginPolicy();

        services.AddCors(corsOptions => corsOptions.AddPolicy(
            CorsPolicyName,
            policy => ConfigureCorsPolicy(policy, originPolicy)));

        if (!endpointSettings.RequiresAuthentication)
        {
            return services;
        }

        services.AddTransportAuthentication(
            TransportSurface.Admin,
            [.. endpointSettings.Authentication],
            ChallengeSchemeFor(endpointSettings));

        return services;
    }

    /// <summary>Names the registered scheme that answers a request presenting no credential at all.</summary>
    /// <remarks>
    /// <para>
    /// It has to be a scheme this surface actually registered, or the challenge forwards to nothing. The API key scheme
    /// is the natural answer and produces the bare bearer challenge, which is all this endpoint has to say. RFC 9728
    /// lets a challenge point at the metadata document, and this one does not need to: a client here reaches the
    /// document by appending the route prefix it is already calling, which is what the resource identifier is validated
    /// against at startup, so nothing about authorizing depends on the wording of a refusal.
    /// </para>
    /// <para>
    /// With API keys turned off the client assertion scheme answers, and with both turned off the first authorization
    /// server's validator does. All three challenge identically, which is what makes the order here a matter of which
    /// scheme is certain to exist rather than of what a client is told. One of them always does: an endpoint reaching
    /// this point configured at least one method, and configuration validation refuses OAuth with no authorization
    /// server behind it.
    /// </para>
    /// </remarks>
    private static string ChallengeSchemeFor(AdminEndpointOptions endpointSettings)
    {
        if (endpointSettings.AllowsApiKey)
        {
            return TransportSurface.Admin.ApiKeySchemeName;
        }

        return endpointSettings.AllowsClientAssertion
            ? TransportSurface.Admin.ClientAssertionSchemeName
            : TransportSurface.Admin.OAuthSchemeNameFor(
                endpointSettings.OAuthMethods()[0].AuthorizationServers[0].Name!);
    }

    /// <summary>Builds the CORS policy from the configured origins.</summary>
    /// <remarks>
    /// Credentials are never allowed, under any policy, for the reason the other two surfaces state: a browser that
    /// could attach an ambient cookie would let a page act as whoever is logged in somewhere else, and this surface's
    /// credential is a bearer token the client sets deliberately.
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
            .WithMethods(HttpMethods.Get, HttpMethods.Post, HttpMethods.Put, HttpMethods.Delete)
            .WithHeaders(HeaderNames.Authorization, HeaderNames.ContentType, HeaderNames.Accept)
            .WithExposedHeaders(HeaderNames.WWWAuthenticate);
    }
}
