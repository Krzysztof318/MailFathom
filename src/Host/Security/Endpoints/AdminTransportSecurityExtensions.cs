// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Security.Transport;

namespace MailFathom.Host.Security.Endpoints;

/// <summary>Composes the credentials the administrative endpoint accepts.</summary>
/// <remarks>
/// There is almost nothing here, which is the point. Every rule about which credentials are accepted and what makes a
/// caller authorized already exists once, in <see cref="TransportSecurityExtensions" />, and this hands it the
/// administrative surface and that surface's own settings. Nothing about API-key comparison or token validation is
/// restated, so a change to either reaches both endpoints.
/// <para>
/// Unlike the MCP endpoint there is no CORS policy and no origin check. The clients are command-line tools rather than
/// pages, so there is no browser to negotiate with and no ambient credential a page could be talked into attaching.
/// </para>
/// <para>
/// A protected resource metadata document *is* published, by
/// <see cref="Api.AdminProtectedResourceMetadataEndpoint" /> rather than from here, because <c>mfctl login</c> is
/// exactly the client this surface once had none of: one that arrives holding nothing and has to find out where to
/// authorize. It is mapped as a route instead of registered here because it belongs to no authentication scheme —
/// its whole purpose is to answer a caller that has not authenticated.
/// </para>
/// </remarks>
internal static class AdminTransportSecurityExtensions
{
    /// <summary>Adds the authentication schemes and the authorization requirement the administrative endpoint runs under.</summary>
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

        if (!endpointSettings.RequiresAuthentication)
        {
            return services;
        }

        services.AddTransportAuthentication(
            TransportSurface.Admin,
            endpointSettings.ApiKeys(),
            endpointSettings.OAuthMethods(),
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
    /// With API keys turned off that scheme does not exist, so the first authorization server's validator answers
    /// instead — it challenges identically. There is always one, because configuration validation refuses OAuth
    /// authentication with no authorization server configured.
    /// </para>
    /// </remarks>
    private static string ChallengeSchemeFor(AdminEndpointOptions endpointSettings) =>
        endpointSettings.AllowsApiKey
            ? TransportSurface.Admin.ApiKeySchemeName
            : TransportSurface.Admin.OAuthSchemeNameFor(
                endpointSettings.OAuthMethods()[0].AuthorizationServers[0].Name!);
}
