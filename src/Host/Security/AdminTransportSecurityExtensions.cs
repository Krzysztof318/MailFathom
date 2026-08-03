// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration;

namespace MailFathom.Host.Security;

/// <summary>Composes the credentials the administrative endpoint accepts.</summary>
/// <remarks>
/// There is almost nothing here, which is the point. Every rule about which credentials are accepted and what makes a
/// caller authorized already exists once, in <see cref="TransportSecurityExtensions" />, and this hands it the
/// administrative surface and that surface's own settings. Nothing about API-key comparison or token validation is
/// restated, so a change to either reaches both endpoints.
/// <para>
/// Unlike the MCP endpoint there is no CORS policy, no origin check, and no protected resource metadata document. The
/// clients are command-line tools configured with an address and a credential, so there is no browser to negotiate with
/// and no client arriving holding nothing that would need to discover where to authorize.
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
            endpointSettings.Authentication,
            [.. endpointSettings.ApiKeys],
            endpointSettings.OAuth,
            ChallengeSchemeFor(endpointSettings));

        return services;
    }

    /// <summary>Names the registered scheme that answers a request presenting no credential at all.</summary>
    /// <remarks>
    /// <para>
    /// It has to be a scheme this surface actually registered, or the challenge forwards to nothing. The API key scheme
    /// is the natural answer and produces the bare bearer challenge, which is all this endpoint has to say: a client
    /// here was configured with a credential rather than sent to discover one, so no metadata document needs carrying.
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
            : TransportSurface.Admin.OAuthSchemeNameFor(endpointSettings.OAuth.AuthorizationServers[0].Name!);
}
