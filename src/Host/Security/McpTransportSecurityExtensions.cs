// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Host.Configuration;
using MailMcp.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Net.Http.Headers;

namespace MailMcp.Host.Security;

/// <summary>Composes the controls that stand in front of the MCP endpoint.</summary>
/// <remarks>
/// The three controls answer three different questions and are wired separately on purpose. Authentication decides
/// whether a caller is one this deployment serves, CORS decides what a browser is allowed to read of the answer, and
/// the origin check decides whether a request a browser was talked into making is served at all. None of them stands in
/// for another, and a deployment can be narrow on one while wide on the next.
/// </remarks>
internal static class McpTransportSecurityExtensions
{
    /// <summary>The CORS policy the MCP endpoint requires, named so the endpoint asks for this one rather than a default.</summary>
    internal const string CorsPolicyName = "MailMcpEndpoint";

    private const string McpSessionHeaderName = "Mcp-Session-Id";

    private const string McpProtocolVersionHeaderName = "MCP-Protocol-Version";

    private const string LastEventHeaderName = "Last-Event-ID";

    /// <summary>Adds the authentication scheme, the authorization requirement, and the CORS policy the endpoint runs under.</summary>
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

        if (endpointSettings.Authentication != McpTransportAuthenticationMode.ApiKey)
        {
            return services;
        }

        services.AddSingleton<McpApiKeyAuthenticator>();
        services
            .AddAuthentication(McpApiKeyAuthentication.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, McpApiKeyAuthenticationHandler>(
                McpApiKeyAuthentication.SchemeName,
                configureOptions: null);
        services.AddAuthorization();

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
            .WithExposedHeaders(McpSessionHeaderName, McpProtocolVersionHeaderName);
    }
}
