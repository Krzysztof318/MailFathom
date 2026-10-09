// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Security.Basic;
using MailFathom.Host.Security.Transport;
using MailFathom.Infrastructure.Security.Transport;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Net.Http.Headers;

namespace MailFathom.Host.Security.Endpoints;

/// <summary>Composes the credentials the administrative endpoint accepts, and what it tells a browser it may read.</summary>
/// <remarks>
/// There is almost nothing here about credentials, which is the point. Every rule about which ones are accepted and
/// what makes a caller authorized already exists once, in <see cref="TransportSecurityExtensions" />, and this hands
/// it the administrative surface and that surface's own methods — the same registration the two mail-serving surfaces
/// use, because every caller on every surface is a user resolved from a credential record.
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

        // No exchange: a session is what the client surface mints for a page that must not keep a credential, and
        // mfctl keeps its own credential in the operator's profile instead.
        services.AddUserFacingTransportAuthentication(
            TransportSurface.Admin,
            [.. endpointSettings.Authentication],
            ChallengeSchemeFor(endpointSettings),
            exchangesCredentialsForSessions: false);

        return services;
    }

    /// <summary>Names the registered scheme that answers a request presenting no credential at all.</summary>
    /// <remarks>
    /// Chosen as the client surface chooses it: Basic first where a password is accepted, and otherwise whichever of the
    /// three bearer schemes is certain to exist. One always does: an endpoint reaching this point configured at least one
    /// method. Unlike the client surface, Basic answers here with the bearer challenge alone, because every
    /// administrative route carries <see cref="NoPasswordChallenge" />.
    /// </remarks>
    private static string ChallengeSchemeFor(AdminEndpointOptions endpointSettings)
    {
        if (endpointSettings.AllowsBasic)
        {
            return TransportSurface.Admin.BasicSchemeName;
        }

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
    /// could attach an ambient cookie would let a page act as whoever is logged in somewhere else. This surface's
    /// credential is one the client sets deliberately — a bearer token, or a password <c>mfctl</c> sends in a Basic
    /// header of its own composing. No administrative route asks a browser for a password, which
    /// <see cref="NoPasswordChallenge" /> holds why, and one a browser holds from another surface is refused here when
    /// a browser page sends it.
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
