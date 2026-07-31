// Copyright © 2026 Krzysztof Kasprowicz

using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using MailMcp.Host.Configuration;
using MailMcp.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.Net.Http.Headers;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Authentication;

namespace MailMcp.Host.Security;

/// <summary>Composes the controls that stand in front of the MCP endpoint.</summary>
/// <remarks>
/// <para>
/// The three controls answer three different questions and are wired separately on purpose. Authentication decides
/// whether a caller is one this deployment serves, CORS decides what a browser is allowed to read of the answer, and
/// the origin check decides whether a request a browser was talked into making is served at all. None of them stands in
/// for another, and a deployment can be narrow on one while wide on the next.
/// </para>
/// <para>
/// Authentication itself is composed of as many schemes as the deployment turned on, sitting behind one scheme that
/// routes to them. That shape is what lets an API key and an access token be accepted at once without either check
/// weakening: each credential reaches the handler that understands it, and a handler never sees a credential of the
/// other kind.
/// </para>
/// </remarks>
internal static class McpTransportSecurityExtensions
{
    /// <summary>The CORS policy the MCP endpoint requires, named so the endpoint asks for this one rather than a default.</summary>
    internal const string CorsPolicyName = "MailMcpEndpoint";

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

        AddAuthenticationSchemes(services, endpointSettings);
        AddAuthorizationPolicy(services, endpointSettings);

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

    /// <summary>Registers the routing scheme and one scheme per credential the deployment accepts.</summary>
    /// <remarks>
    /// The routing scheme is the default, so every part of the pipeline that asks for "the" scheme — the authorization
    /// middleware challenging an anonymous request, the endpoint requiring an authenticated user — reaches the one place
    /// that knows how many schemes there are. Nothing downstream names an individual scheme.
    /// </remarks>
    private static void AddAuthenticationSchemes(IServiceCollection services, McpEndpointOptions endpointSettings)
    {
        var authentication = services.AddAuthentication(McpOAuthAuthentication.RoutingSchemeName);

        var apiKeySchemeName = endpointSettings.AllowsApiKey ? McpApiKeyAuthentication.SchemeName : null;
        var unmatchedSchemeName = endpointSettings.AllowsOAuth
            ? McpAuthenticationDefaults.AuthenticationScheme
            : McpApiKeyAuthentication.SchemeName;

        var oauthSchemesByIssuer = endpointSettings.AllowsOAuth
            ? endpointSettings.OAuth.AuthorizationServers.ToDictionary(
                authorizationServer => authorizationServer.ValidatedIssuer(),
                authorizationServer => McpOAuthAuthentication.SchemeNameFor(authorizationServer.Name!),
                StringComparer.Ordinal)
            : [];

        var schemeSelector = new McpCredentialSchemeSelector(
            oauthSchemesByIssuer,
            apiKeySchemeName,
            unmatchedSchemeName);

        authentication.AddPolicyScheme(
            McpOAuthAuthentication.RoutingSchemeName,
            displayName: null,
            policyOptions =>
            {
                policyOptions.ForwardDefaultSelector = context =>
                    schemeSelector.SchemeFor(context.Request.Headers.Authorization.ToString());

                // A challenge is answered by one scheme whichever credential was presented, because a request that has
                // nothing to authenticate with has told us nothing about which kind of credential it was going to use.
                // With OAuth turned on that is the MCP scheme, whose challenge carries the metadata document a client
                // needs to begin authorizing; otherwise it is the API key scheme's bare bearer challenge.
                policyOptions.ForwardChallenge = unmatchedSchemeName;
            });

        if (endpointSettings.AllowsApiKey)
        {
            services.AddSingleton<McpApiKeyAuthenticator>();
            authentication.AddScheme<AuthenticationSchemeOptions, McpApiKeyAuthenticationHandler>(
                McpApiKeyAuthentication.SchemeName,
                configureOptions: null);
        }

        if (endpointSettings.AllowsOAuth)
        {
            AddOAuthSchemes(authentication, endpointSettings.OAuth);
        }
    }

    /// <summary>Registers the metadata scheme and one token validator per configured authorization server.</summary>
    private static void AddOAuthSchemes(AuthenticationBuilder authentication, McpOAuthOptions oauthSettings)
    {
        authentication.AddMcp(
            mcpOptions =>
            {
                // Absolute and configured, never derived from the request. Left unset, the SDK composes both this
                // address and the resource it advertises from the request's scheme and Host header, so a deployment
                // behind a proxy would tell each client to authenticate for whichever name that client arrived under.
                mcpOptions.ResourceMetadataUri = new Uri(oauthSettings.ProtectedResourceMetadataAddress());
                mcpOptions.ResourceMetadata = new ProtectedResourceMetadata
                {
                    Resource = oauthSettings.CanonicalResource(),
                    AuthorizationServers = [.. oauthSettings.AuthorizationServers.Select(server => server.ValidatedIssuer())],
                    ScopesSupported = [.. oauthSettings.RequiredScopes],
                    BearerMethodsSupported = ["header"],
                    ResourceName = "MailMcp",
                };
            });

        foreach (var authorizationServer in oauthSettings.AuthorizationServers)
        {
            authentication.AddJwtBearer(
                McpOAuthAuthentication.SchemeNameFor(authorizationServer.Name!),
                jwtOptions => ConfigureAuthorizationServer(jwtOptions, authorizationServer, oauthSettings));
        }
    }

    /// <summary>Configures one authorization server's token validator, its metadata retrieval, and its key set.</summary>
    /// <remarks>
    /// <para>
    /// Every profile gets its own configuration manager, which is what keeps two authorization servers isolated. A key
    /// set is reachable only from the scheme whose issuer published it, so a signing key that two servers happen to
    /// identify the same way never validates a token claiming the other's issuer.
    /// </para>
    /// <para>
    /// Nothing about the server's own endpoints is assembled here. The key set address comes out of the discovery
    /// document, which is itself found at the addresses the MCP authorization specification names, so a server that
    /// moves an endpoint keeps working and one that publishes no document fails to configure rather than being reached
    /// at a guessed path.
    /// </para>
    /// </remarks>
    private static void ConfigureAuthorizationServer(
        JwtBearerOptions jwtOptions,
        McpAuthorizationServerOptions authorizationServer,
        McpOAuthOptions oauthSettings)
    {
        var issuer = authorizationServer.ValidatedIssuer();
        var metadataAddresses = authorizationServer.MetadataAddresses();

        jwtOptions.MetadataAddress = metadataAddresses[0];
        jwtOptions.RequireHttpsMetadata = true;

        // The claims stay under the names the token used, because the identity mapping below reads 'iss' and 'sub'. The
        // framework's default renames them to long-form SOAP claim types, which would leave that mapping reading claims
        // that no longer exist and quietly producing no identity.
        jwtOptions.MapInboundClaims = false;

        // No error description reaches the client. The framework's default reports why a token was refused, which tells
        // an unauthenticated caller whether an issuer is configured, whether an audience matched, and whether a token
        // merely expired. The server log keeps all of it.
        jwtOptions.IncludeErrorDetails = false;

        jwtOptions.Backchannel = MetadataBackchannel();
        jwtOptions.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            metadataAddresses[0],
            new OAuthAuthorizationServerMetadataRetriever(authorizationServer.Name!, issuer, metadataAddresses),
            new HttpDocumentRetriever(jwtOptions.Backchannel) { RequireHttps = true })
        {
            AutomaticRefreshInterval = McpOAuthAuthentication.MetadataRefreshInterval,
            RefreshInterval = McpOAuthAuthentication.MetadataRefreshThrottle,
            LastKnownGoodLifetime = McpOAuthAuthentication.LastKnownGoodMetadataLifetime,
        };

        jwtOptions.TokenValidationParameters = McpOAuthAuthentication.TokenValidationParametersFor(
            issuer,
            oauthSettings.CanonicalResource());

        jwtOptions.Events = new JwtBearerEvents
        {
            OnTokenValidated = ReplacePrincipalWithMinimalIdentity,
        };
    }

    /// <summary>Reduces a validated token to the identity MailMcp keeps of it.</summary>
    /// <remarks>
    /// The validated principal carries every claim the authorization server chose to include, which routinely means a
    /// name, an address, and a set of groups. Replacing it here means nothing downstream can read one, so a later change
    /// cannot start depending on a claim the operator never mapped.
    /// </remarks>
    private static Task ReplacePrincipalWithMinimalIdentity(TokenValidatedContext context)
    {
        var identity = context.Principal is { } validatedPrincipal
            ? McpOAuthIdentity.FromValidatedToken(validatedPrincipal.Claims, context.Scheme.Name)
            : null;

        if (identity is null)
        {
            context.Fail("The validated token names no subject.");

            return Task.CompletedTask;
        }

        context.Principal = new ClaimsPrincipal(identity);

        return Task.CompletedTask;
    }

    /// <summary>Builds the client the discovery document and key set are retrieved through.</summary>
    /// <remarks>
    /// It follows no redirect and reads no more than the stated limit, so an authorization server cannot send a key
    /// refresh somewhere the configuration never named or answer it with an unbounded body. The timeout bounds how long
    /// a refresh can hold the request that provoked it.
    /// </remarks>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The client and its handlers are owned by the JwtBearerOptions this is assigned to and live for the process lifetime; disposing them here would leave every key refresh without a transport.")]
    private static HttpClient MetadataBackchannel()
    {
        var transport = new HttpClientHandler { AllowAutoRedirect = false };

        return new HttpClient(new BoundedMetadataHttpMessageHandler(transport, McpOAuthOptions.MetadataSizeLimitInBytes))
        {
            Timeout = McpOAuthOptions.MetadataRetrievalTimeout,
        };
    }

    /// <summary>Registers the requirement the MCP endpoint carries, and the refusal an insufficient token receives.</summary>
    private static void AddAuthorizationPolicy(IServiceCollection services, McpEndpointOptions endpointSettings)
    {
        var requiredScopes = endpointSettings.AllowsOAuth
            ? endpointSettings.OAuth.RequiredScopes.ToArray()
            : [];

        services.AddAuthorization(authorizationOptions => authorizationOptions.AddPolicy(
            McpAccessPolicy.PolicyName,
            policy => policy
                .AddAuthenticationSchemes(McpOAuthAuthentication.RoutingSchemeName)
                .RequireAssertion(context => McpAccessPolicy.IsAuthorized(context.User, requiredScopes))));

        // Only a required scope can turn an authenticated caller away, so the refusal that explains which scope is
        // missing is registered only where one exists. Without it the endpoint answers every failure with a challenge,
        // and there is nothing for this to say.
        if (requiredScopes.Length > 0)
        {
            services.AddSingleton<IAuthorizationMiddlewareResultHandler>(
                new McpInsufficientScopeResultHandler(
                    requiredScopes,
                    endpointSettings.OAuth.ProtectedResourceMetadataAddress()));
        }
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
