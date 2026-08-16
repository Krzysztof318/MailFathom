// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Claims;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Access;
using MailFathom.Host.Security.ApiKeys;
using MailFathom.Host.Security.ClientAssertions;
using MailFathom.Host.Security.Mcp;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.Infrastructure.Security.ApiKeys;
using MailFathom.Infrastructure.Security.OAuth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace MailFathom.Host.Security.Transport;

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
    /// <summary>Names the registered transport an authorization server's metadata is retrieved over.</summary>
    /// <remarks>One transport for every scheme on every surface, because what it carries is the same fetch of the same kind of document under the same bounds. Each scheme still holds a client of its own over it, so no key refresh can observe another scheme's.</remarks>
    internal const string MetadataBackchannelTransportName = "mailfathom.oauth-metadata";

    /// <summary>Adds one surface's authentication schemes and its authorization requirement.</summary>
    /// <param name="services">The container to add to.</param>
    /// <param name="surface">The surface being protected, which names every scheme and the policy.</param>
    /// <param name="methods">The surface's configured credential entries, in configuration order.</param>
    /// <param name="challengeSchemeName">The scheme answering a request that presented no credential this surface can place, which is both what authenticates it and what challenges it.</param>
    /// <returns>The authentication builder, so a surface can add schemes only it needs.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any reference argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="surface" /> is the struct default.</exception>
    /// <remarks>
    /// <para>
    /// The whole entries rather than the credentials pulled out of them, because an entry is what carries a grant as
    /// well as a credential, and the two have to be registered together: a key is compared by the scheme its entry
    /// selected, and what the caller may do afterwards is what that same entry wrote down.
    /// </para>
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
        IReadOnlyList<TransportAuthenticationOptions> methods,
        string challengeSchemeName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(methods);
        ArgumentNullException.ThrowIfNull(challengeSchemeName);

        if (!surface.IsSpecified)
        {
            throw new ArgumentException("A transport surface is required to name the schemes and the policy.", nameof(surface));
        }

        var apiKeys = TransportAuthenticationConfiguration.ApiKeysIn(methods);
        var publicKeys = TransportAuthenticationConfiguration.PublicKeysIn(methods);
        var oauthMethods = TransportAuthenticationConfiguration.OAuthMethodsIn(methods);

        var authentication = services.AddAuthentication(surface.RoutingSchemeName);

        AddRoutingScheme(authentication, surface, apiKeys, publicKeys, oauthMethods, challengeSchemeName);

        if (publicKeys.Count > 0)
        {
            // Added once however many surfaces accept an assertion, for the reason the API key authenticator is: the
            // verifier holds no surface state, and the replay store is deliberately one for the process — an identifier
            // spent on either surface is spent, which is the safe direction and costs a client nothing, since an
            // identifier is minted fresh per request.
            services.TryAddSingleton<ClientAssertionReplayStore>();
            services.TryAddSingleton<ClientAssertionAuthenticator>();
            authentication.AddScheme<ClientAssertionAuthenticationSchemeOptions, ClientAssertionAuthenticationHandler>(
                surface.ClientAssertionSchemeName,
                schemeOptions =>
                {
                    schemeOptions.Surface = surface;
                    schemeOptions.PublicKeys = publicKeys;
                    schemeOptions.GrantsByKeyName = TransportAuthenticationConfiguration.GrantsByPublicKeyName(
                        methods,
                        surface.GrantedSurface);
                });
        }

        if (apiKeys.Count > 0)
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
                    schemeOptions.GrantsByKeyName = TransportAuthenticationConfiguration.GrantsByApiKeyName(
                        methods,
                        surface.GrantedSurface);
                });
        }

        if (oauthMethods.Count > 0)
        {
            AddMetadataBackchannel(services);
        }

        // Each entry's own servers are registered against that entry's resource and that entry's grant, which is what
        // makes an entry the unit a token is judged by rather than one merged set the whole endpoint shares.
        foreach (var method in methods.Where(method => method.OAuth is not null))
        {
            var oauthMethod = method.OAuth!;
            var grant = method.GrantedPermissions(surface.GrantedSurface);
            var narrowedByTokenScopes = method.PermissionsFromTokenScopes;

            foreach (var authorizationServer in oauthMethod.AuthorizationServers)
            {
                var schemeName = surface.OAuthSchemeNameFor(authorizationServer.Name!);

                authentication.AddJwtBearer(schemeName);
                services.AddOptions<JwtBearerOptions>(schemeName)
                    .Configure<IHttpClientFactory>((jwtOptions, transportFactory) =>
                        ConfigureAuthorizationServer(
                            jwtOptions,
                            authorizationServer,
                            oauthMethod,
                            grant,
                            narrowedByTokenScopes,
                            transportFactory));
            }
        }

        AddAuthorizationPolicy(services, surface, oauthMethods);

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
        IReadOnlyList<ConfiguredSecret> apiKeys,
        IReadOnlyList<ConfiguredSecret> publicKeys,
        IReadOnlyList<OAuthValidationOptions> oauthMethods,
        string challengeSchemeName)
    {
        var oauthSchemesByIssuer = oauthMethods
            .SelectMany(oauthMethod => oauthMethod.AuthorizationServers)
            .ToDictionary(
                authorizationServer => authorizationServer.ValidatedIssuer(),
                authorizationServer => surface.OAuthSchemeNameFor(authorizationServer.Name!),
                StringComparer.Ordinal);

        var schemeSelector = new CredentialSchemeSelector(
            oauthSchemesByIssuer,
            apiKeys.Count > 0 ? surface.ApiKeySchemeName : null,
            publicKeys.Count > 0 ? surface.ClientAssertionSchemeName : null,
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
                // That is the same scheme the selector above routes such a request to, which is what obliges it to
                // authenticate nobody: authenticating and challenging are different jobs, and a scheme that forwarded
                // the first somewhere would answer a fault where the pipeline expects a refusal it can challenge.
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
        OAuthValidationOptions oauthSettings,
        IReadOnlyList<MailFathomPermission> grant,
        bool narrowedByTokenScopes,
        IHttpClientFactory transportFactory)
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

        jwtOptions.Backchannel = transportFactory.CreateClient(MetadataBackchannelTransportName);
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
            OnTokenValidated = context =>
                ReplacePrincipalWithMinimalIdentity(context, grant, narrowedByTokenScopes),
        };
    }

    /// <summary>Reduces a validated token to the identity MailFathom keeps of it, and writes the grant it holds.</summary>
    /// <remarks>
    /// <para>
    /// The validated principal carries every claim the authorization server chose to include, which routinely means a
    /// name, an address, and a set of groups. Replacing it here means nothing downstream can read one, so a later change
    /// cannot start depending on a claim the operator never mapped.
    /// </para>
    /// <para>
    /// The grant is written onto the identity that survives rather than left to be recomposed later, which is what
    /// keeps a permission the entry never granted unreachable however a token is read afterwards.
    /// </para>
    /// </remarks>
    private static Task ReplacePrincipalWithMinimalIdentity(
        TokenValidatedContext context,
        IReadOnlyList<MailFathomPermission> grant,
        bool narrowedByTokenScopes)
    {
        var identity = context.Principal is { } validatedPrincipal
            ? OAuthIdentity.FromValidatedToken(validatedPrincipal.Claims, context.Scheme.Name)
            : null;

        if (identity is null)
        {
            context.Fail("The validated token names no subject.");

            return Task.CompletedTask;
        }

        identity.AddClaims(TransportGrant.ClaimsFor(GrantHeldByToken(identity, grant, narrowedByTokenScopes)));

        context.Principal = new ClaimsPrincipal(identity);

        return Task.CompletedTask;
    }

    /// <summary>Reports which of the entry's permissions this particular token holds.</summary>
    /// <remarks>
    /// Without the narrowing setting every token the entry admits holds the whole ceiling, because the deployment wrote
    /// the grant and the authorization server was never asked. With it, a scope bearing a published permission name
    /// grants that permission and nothing else does — so the intersection is the answer, and a scope naming anything
    /// else is ignored rather than refused, since a token legitimately carries scopes about its client's own session
    /// and about resources that are not this one.
    /// </remarks>
    private static IEnumerable<MailFathomPermission> GrantHeldByToken(
        ClaimsIdentity identity,
        IReadOnlyList<MailFathomPermission> grant,
        bool narrowedByTokenScopes)
    {
        if (!narrowedByTokenScopes)
        {
            return grant;
        }

        var tokenScopes = identity
            .FindAll(OAuthIdentity.ScopeClaimType)
            .Select(scope => scope.Value)
            .ToHashSet(StringComparer.Ordinal);

        return grant.Where(permission => tokenScopes.Contains(permission.Name));
    }

    /// <summary>Registers the transport the discovery document and key set are retrieved through.</summary>
    /// <remarks>
    /// <para>
    /// It follows no redirect and reads no more than the stated limit, so an authorization server cannot send a key
    /// refresh somewhere the configuration never named or answer it with an unbounded body. The timeout bounds how long
    /// a refresh can hold the request that provoked it.
    /// </para>
    /// <para>
    /// This is the one client here that cannot be opened per operation, and the connection lifetime is the consequence
    /// rather than a preference. <see cref="JwtBearerOptions.Backchannel" /> and <see cref="HttpDocumentRetriever" />
    /// each take one client and keep it, so the instance a scheme is configured with performs every key refresh that
    /// scheme ever makes — and the factory's handler rotation, which replaces the chain only for a client asked for
    /// after it, would never reach it. Bounding the pooled connection is what makes an authorization server that moves
    /// its address reachable without restarting the process.
    /// </para>
    /// <para>
    /// A surface registers this before its schemes and both surfaces may register it, so every call here assigns rather
    /// than appends: a second surface must not leave the chain carrying two bounded handlers, and
    /// <c>ConfigurePrimaryHttpMessageHandler</c> replacing the whole chain is what keeps that true without a guard.
    /// </para>
    /// </remarks>
    private static void AddMetadataBackchannel(IServiceCollection services) =>
        services.AddHttpClient(MetadataBackchannelTransportName)
            .ConfigurePrimaryHttpMessageHandler(static () =>
                new BoundedMetadataHttpMessageHandler(OAuthValidationOptions.MetadataSizeLimitInBytes)
                {
                    InnerHandler = new SocketsHttpHandler
                    {
                        AllowAutoRedirect = false,
                        PooledConnectionLifetime = OAuthValidationOptions.MetadataConnectionLifetime,
                    },
                })
            .ConfigureHttpClient(static backchannel =>
                backchannel.Timeout = OAuthValidationOptions.MetadataRetrievalTimeout);

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
        IReadOnlyList<OAuthValidationOptions> oauthMethods)
    {
        var requiredScopesByIssuer = TransportAuthenticationConfiguration.RequiredScopesByIssuer(oauthMethods);

        var authorizedIdentities = oauthMethods
            .SelectMany(oauthMethod => oauthMethod.AuthorizedIdentities())
            .ToHashSet(StringComparer.Ordinal);

        services.AddAuthorization(authorizationOptions => authorizationOptions.AddPolicy(
            surface.AccessPolicyName,
            policy => policy
                .AddAuthenticationSchemes(surface.RoutingSchemeName)
                .RequireAssertion(context =>
                    TransportAccessPolicy.IsAuthorized(context.User, authorizedIdentities, requiredScopesByIssuer))));
    }
}
