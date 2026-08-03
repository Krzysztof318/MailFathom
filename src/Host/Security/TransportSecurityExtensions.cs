// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using MailFathom.Host.Configuration;
using MailFathom.Infrastructure.Secrets;
using MailFathom.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace MailFathom.Host.Security;

/// <summary>Registers the credentials one transport surface accepts, and the requirement its routes carry.</summary>
/// <remarks>
/// <para>
/// Authentication is composed of as many schemes as the surface turned on, sitting behind one scheme that routes to
/// them. That shape is what lets an API key and an access token be accepted at once without either check weakening: each
/// credential reaches the handler that understands it, and a handler never sees a credential of the other kind.
/// </para>
/// <para>
/// Every name the registration uses comes from the <see cref="TransportSurface" /> it is given, so two surfaces
/// registered through this method share the code and share nothing else. A key configured for one authenticates nothing
/// on the other, a token accepted by one is never consulted for the other, and neither surface's policy can be satisfied
/// by the other's credential — because a policy names only its own routing scheme.
/// </para>
/// </remarks>
internal static class TransportSecurityExtensions
{
    /// <summary>Adds one surface's authentication schemes and its authorization requirement.</summary>
    /// <param name="services">The container to add to.</param>
    /// <param name="surface">The surface being protected, which names every scheme and the policy.</param>
    /// <param name="methods">The credentials this surface accepts.</param>
    /// <param name="apiKeys">The API key references a request may present one of, consulted only when <paramref name="methods" /> includes them.</param>
    /// <param name="oauthSettings">The authorization servers and token requirements, consulted only when <paramref name="methods" /> includes OAuth.</param>
    /// <param name="challengeSchemeName">The scheme answering a request that presented no credential at all.</param>
    /// <returns>The authentication builder, so a surface can add schemes only it needs.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any reference argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="surface" /> is the struct default.</exception>
    /// <remarks>
    /// <para>
    /// The routing scheme becomes the application's default, so every part of the pipeline that asks for "the" scheme
    /// reaches the one place that knows how many schemes there are, and nothing downstream names an individual scheme.
    /// </para>
    /// <para>
    /// There is one application-wide default, which is what a second surface has to plan around: calling this twice
    /// leaves the later registration's routing scheme as the default, and with it the scheme
    /// <c>UseAuthentication</c> runs to populate <c>HttpContext.User</c>. Authorization is unaffected, because each
    /// surface's policy names its own routing scheme explicitly rather than relying on the default — but a surface
    /// registered second must scope its authentication middleware to its own routes rather than adding a second global
    /// one, or requests to the first surface would be pre-authenticated under the second's schemes.
    /// </para>
    /// </remarks>
    internal static AuthenticationBuilder AddTransportAuthentication(
        this IServiceCollection services,
        TransportSurface surface,
        TransportAuthenticationMethods methods,
        IReadOnlyList<ConfiguredSecret> apiKeys,
        OAuthValidationOptions oauthSettings,
        string challengeSchemeName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(apiKeys);
        ArgumentNullException.ThrowIfNull(oauthSettings);
        ArgumentNullException.ThrowIfNull(challengeSchemeName);

        if (!surface.IsSpecified)
        {
            throw new ArgumentException("A transport surface is required to name the schemes and the policy.", nameof(surface));
        }

        var allowsApiKey = methods.HasFlag(TransportAuthenticationMethods.ApiKey);
        var allowsOAuth = methods.HasFlag(TransportAuthenticationMethods.OAuth);

        var authentication = services.AddAuthentication(surface.RoutingSchemeName);

        AddRoutingScheme(authentication, surface, allowsApiKey, allowsOAuth, oauthSettings, challengeSchemeName);

        if (allowsApiKey)
        {
            // Added once however many surfaces accept a key, because the authenticator holds no surface state: which
            // keys it compares against arrive as an argument on every call. A second registration would resolve the
            // same thing twice over and leave which instance answers decided by registration order.
            services.TryAddSingleton<ApiKeyAuthenticator>();
            authentication.AddScheme<ApiKeyAuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
                surface.ApiKeySchemeName,
                schemeOptions =>
                {
                    schemeOptions.Surface = surface;
                    schemeOptions.ApiKeys = apiKeys;
                });
        }

        if (allowsOAuth)
        {
            foreach (var authorizationServer in oauthSettings.AuthorizationServers)
            {
                authentication.AddJwtBearer(
                    surface.OAuthSchemeNameFor(authorizationServer.Name!),
                    jwtOptions => ConfigureAuthorizationServer(jwtOptions, authorizationServer, oauthSettings));
            }
        }

        AddAuthorizationPolicy(services, surface, allowsOAuth, oauthSettings);

        return authentication;
    }

    /// <summary>Refuses a bearer token presented over a connection that was not encrypted.</summary>
    /// <remarks>
    /// <para>
    /// An access token is a reusable credential, so a request carrying one over plain HTTP hands it to anybody watching
    /// the network — and unlike a password nothing about presenting it a second time looks unusual. The resource
    /// identifier being HTTPS and metadata retrieval requiring HTTPS protect what this deployment publishes and what it
    /// fetches; neither says anything about the transport an incoming request arrived on.
    /// </para>
    /// <para>
    /// The refusal is silent — no result rather than a failure — so the caller receives the same challenge an
    /// unauthenticated request receives, and the token is never read, validated, or recorded.
    /// </para>
    /// </remarks>
    internal static Task RefuseATokenThatArrivedWithoutTransportEncryption(MessageReceivedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Request.IsHttps)
        {
            context.NoResult();
        }

        return Task.CompletedTask;
    }

    /// <summary>Registers the scheme that reads the presented credential and forwards it to the handler that judges it.</summary>
    private static void AddRoutingScheme(
        AuthenticationBuilder authentication,
        TransportSurface surface,
        bool allowsApiKey,
        bool allowsOAuth,
        OAuthValidationOptions oauthSettings,
        string challengeSchemeName)
    {
        var oauthSchemesByIssuer = allowsOAuth
            ? oauthSettings.AuthorizationServers.ToDictionary(
                authorizationServer => authorizationServer.ValidatedIssuer(),
                authorizationServer => surface.OAuthSchemeNameFor(authorizationServer.Name!),
                StringComparer.Ordinal)
            : [];

        var schemeSelector = new CredentialSchemeSelector(
            oauthSchemesByIssuer,
            allowsApiKey ? surface.ApiKeySchemeName : null,
            challengeSchemeName);

        authentication.AddPolicyScheme(
            surface.RoutingSchemeName,
            displayName: null,
            policyOptions =>
            {
                policyOptions.ForwardDefaultSelector = context =>
                    schemeSelector.SchemeFor(context.Request.Headers.Authorization.ToString());

                // A challenge is answered by one scheme whichever credential was presented, because a request that has
                // nothing to authenticate with has told us nothing about which kind of credential it was going to use.
                policyOptions.ForwardChallenge = challengeSchemeName;
            });
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
    /// document, so a server that moves an endpoint keeps working and one that publishes no document fails to configure
    /// rather than being reached at a guessed path.
    /// </para>
    /// </remarks>
    private static void ConfigureAuthorizationServer(
        JwtBearerOptions jwtOptions,
        AuthorizationServerOptions authorizationServer,
        OAuthValidationOptions oauthSettings)
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
            AutomaticRefreshInterval = OAuthTokenValidation.MetadataRefreshInterval,
            RefreshInterval = OAuthTokenValidation.MetadataRefreshThrottle,
            LastKnownGoodLifetime = OAuthTokenValidation.LastKnownGoodMetadataLifetime,
        };

        jwtOptions.TokenValidationParameters = OAuthTokenValidation.TokenValidationParametersFor(
            issuer,
            oauthSettings.CanonicalResource());

        jwtOptions.Events = new JwtBearerEvents
        {
            OnMessageReceived = RefuseATokenThatArrivedWithoutTransportEncryption,
            OnTokenValidated = ReplacePrincipalWithMinimalIdentity,
        };
    }

    /// <summary>Reduces a validated token to the identity MailFathom keeps of it.</summary>
    /// <remarks>
    /// The validated principal carries every claim the authorization server chose to include, which routinely means a
    /// name, an address, and a set of groups. Replacing it here means nothing downstream can read one, so a later change
    /// cannot start depending on a claim the operator never mapped.
    /// </remarks>
    private static Task ReplacePrincipalWithMinimalIdentity(TokenValidatedContext context)
    {
        var identity = context.Principal is { } validatedPrincipal
            ? OAuthIdentity.FromValidatedToken(validatedPrincipal.Claims, context.Scheme.Name)
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

        return new HttpClient(new BoundedMetadataHttpMessageHandler(transport, OAuthValidationOptions.MetadataSizeLimitInBytes))
        {
            Timeout = OAuthValidationOptions.MetadataRetrievalTimeout,
        };
    }

    /// <summary>Registers the requirement this surface's routes carry.</summary>
    /// <remarks>
    /// The policy names only this surface's routing scheme, which is what keeps a credential the other surface accepts
    /// from ever being consulted here. How a refusal is *worded* is not registered with it: an
    /// <see cref="IAuthorizationMiddlewareResultHandler" /> is one object for the whole application, so a surface that
    /// shapes its own refusal registers it itself rather than through a method two surfaces call.
    /// </remarks>
    private static void AddAuthorizationPolicy(
        IServiceCollection services,
        TransportSurface surface,
        bool allowsOAuth,
        OAuthValidationOptions oauthSettings)
    {
        var requiredScopes = allowsOAuth ? oauthSettings.RequiredScopes.ToArray() : [];

        var authorizedIdentities = allowsOAuth
            ? oauthSettings.AuthorizedIdentities()
            : new HashSet<string>(StringComparer.Ordinal);

        services.AddAuthorization(authorizationOptions => authorizationOptions.AddPolicy(
            surface.AccessPolicyName,
            policy => policy
                .AddAuthenticationSchemes(surface.RoutingSchemeName)
                .RequireAssertion(context =>
                    TransportAccessPolicy.IsAuthorized(context.User, authorizedIdentities, requiredScopes))));
    }
}
