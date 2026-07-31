// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Host.Configuration;
using MailMcp.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Cors.Infrastructure;
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

    /// <summary>Builds the CORS policy from the configured origins.</summary>
    /// <remarks>
    /// Credentials are never allowed, under either policy. A browser that could attach an ambient cookie to an MCP
    /// request would let a page act as whoever is logged in somewhere else, and the endpoint has no use for one anyway:
    /// its credential is a bearer token a client sets deliberately. Allowing any origin and allowing credentials is
    /// also a combination the CORS specification forbids outright.
    /// </remarks>
    private static void ConfigureCorsPolicy(CorsPolicyBuilder policy, McpOriginPolicy originPolicy)
    {
        if (originPolicy.AllowsAnyOrigin)
        {
            policy.AllowAnyOrigin();
        }
        else
        {
            // An empty set is the refuse-every-browser-origin posture rather than an oversight, and naming no origin is
            // how it reaches CORS: the middleware then matches nothing and writes no Access-Control-Allow-Origin, which
            // leaves a browser unable to read a response it was never going to be served.
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
