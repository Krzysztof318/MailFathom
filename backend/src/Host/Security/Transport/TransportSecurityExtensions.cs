// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Claims;
using MailFathom.Host.Configuration.Access;
using MailFathom.Host.Security.ApiKeys;
using MailFathom.Host.Security.ClientAssertions;
using MailFathom.Host.Security.Mcp;
using MailFathom.Infrastructure.Security.ApiKeys;
using MailFathom.Infrastructure.Security.OAuth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
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
internal static partial class TransportSecurityExtensions
{
    /// <summary>Names the registered transport an authorization server's metadata is retrieved over.</summary>
    /// <remarks>One transport for every scheme on every surface, because what it carries is the same fetch of the same kind of document under the same bounds. Each scheme still holds a client of its own over it, so no key refresh can observe another scheme's.</remarks>
    internal const string MetadataBackchannelTransportName = "mailfathom.oauth-metadata";

    /// <summary>Adds one surface's authentication schemes and its authorization requirement.</summary>
    /// <param name="services">The container to add to.</param>
    /// <param name="surface">The surface being protected, which names every scheme and the policy.</param>
    /// <param name="administrators">The surface's configured administrators, in configuration order.</param>
    /// <param name="challengeSchemeName">The scheme answering a request that presented no credential this surface can place, which is both what authenticates it and what challenges it.</param>
    /// <returns>The authentication builder, so a surface can add schemes only it needs.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any reference argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="surface" /> is the struct default.</exception>
    /// <remarks>
    /// <para>
    /// The whole administrators rather than the credentials pulled out of them, because an administrator is what carries
    /// a name, a grant, and its networks as well as its credentials, and they have to be registered together: a key is
    /// compared by the scheme its credential selected, and who the caller is afterwards is the administrator it sits
    /// under.
    /// </para>
    /// <para>
    /// Nothing here decides what the application authenticates with by default. There is one such default and one
    /// authentication middleware running it over every request, so a surface claiming it would be claiming the other
    /// surface's requests as well, and which surface held it would come down to registration order.
    /// <see cref="DefaultTransportAuthentication" /> is that decision, taken once by the composition root; what a
    /// surface registers is the schemes its own routes are judged by and the policy that names them.
    /// </para>
    /// </remarks>
    internal static AuthenticationBuilder AddTransportAuthentication(
        this IServiceCollection services,
        TransportSurface surface,
        IReadOnlyList<AdministratorOptions> administrators,
        string challengeSchemeName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(administrators);
        ArgumentNullException.ThrowIfNull(challengeSchemeName);

        if (!surface.IsSpecified)
        {
            throw new ArgumentException("A transport surface is required to name the schemes and the policy.", nameof(surface));
        }

        var apiKeys = AdministratorConfiguration.ApiKeysIn(administrators);
        var publicKeys = AdministratorConfiguration.PublicKeysIn(administrators);
        var oauthMethods = AdministratorConfiguration.OAuthMethodsIn(administrators);
        var authorizationServers = AdministratorConfiguration.DistinctAuthorizationServersIn(administrators);
        var admissions = AdministratorAdmission.ForEach(administrators);

        var authentication = services.AddAuthentication();

        AddRoutingScheme(
            authentication,
            surface,
            OAuthSchemesByIssuer(surface, authorizationServers),
            apiKeys.Count > 0 ? surface.ApiKeySchemeName : null,
            publicKeys.Count > 0 ? surface.ClientAssertionSchemeName : null,

            // No Basic scheme, whatever an entry states. A password names a user, and the administrative endpoint —
            // the one surface still registered this way — answers for the deployment rather than for a person, so the
            // entry that would have selected the method is refused by that section's own validation instead of
            // reaching a registration here.
            basicSchemeName: null,

            // And no session token either, which follows: a session is what a password is exchanged for, so a surface
            // that reads no password has nothing to exchange and mints nothing to judge.
            sessionTokenSchemeName: null,
            challengeSchemeName);

        if (publicKeys.Count > 0)
        {
            // Added once however many surfaces accept an assertion, for the reason the API key authenticator is: the
            // verifier holds no surface state, and the replay store is one for the deployment — an identifier spent on
            // either surface, and on any replica, is spent, which is the safe direction and costs a client nothing,
            // since an identifier is minted fresh per request. The store is a singleton because what it holds of its
            // own is the sweep interval; what it records lives in the database the port beneath it writes.
            services.TryAddSingleton<ClientAssertionReplayStore>();
            services.TryAddSingleton<ClientAssertionAuthenticator>();
            authentication.AddScheme<ClientAssertionAuthenticationSchemeOptions, ClientAssertionAuthenticationHandler>(
                surface.ClientAssertionSchemeName,
                schemeOptions =>
                {
                    schemeOptions.Surface = surface;
                    schemeOptions.PublicKeys = publicKeys;
                    schemeOptions.AdministratorsByKeyName = AdmissionsByCredentialName(
                        AdministratorConfiguration.AdministratorsByPublicKeyName(administrators),
                        admissions);
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
                    schemeOptions.AdministratorsByKeyName = AdmissionsByCredentialName(
                        AdministratorConfiguration.AdministratorsByApiKeyName(administrators),
                        admissions);
                });
        }

        if (oauthMethods.Count > 0)
        {
            AddMetadataBackchannel(services);
        }

        // One validator per authorization server however many administrators sign in through it. What a validated
        // token then becomes is decided by the issuer and subject it carries, which bind it to exactly one administrator.
        var tokenAdmissions = TokenAdmissionsByIdentity(
            AdministratorConfiguration.TokenBindingsByIdentity(administrators),
            admissions);

        foreach (var authorizationServer in authorizationServers)
        {
            var schemeName = surface.OAuthSchemeNameFor(authorizationServer.Name!);

            authentication.AddJwtBearer(schemeName);
            services.AddOptions<JwtBearerOptions>(schemeName)
                .Configure<IHttpClientFactory>((jwtOptions, transportFactory) =>
                    ConfigureAuthorizationServer(
                        jwtOptions,
                        authorizationServer,
                        oauthMethods[0],
                        transportFactory,
                        context => ReplacePrincipalWithAdministratorIdentity(context, tokenAdmissions)));
        }

        AddAuthorizationPolicy(services, surface, administrators);

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
    /// The refusal is silent to the caller — no result rather than a failure — so the answer is the same challenge an
    /// unauthenticated request receives, and the token is never read, validated, or recorded. It is not silent to the
    /// operator: behind a TLS-terminating proxy this refusal fires on every authenticated request the moment a
    /// forwarded scheme stops arriving, and a challenge indistinguishable from the anonymous one is the one symptom no
    /// amount of reading the client's logs explains. The record names the forwarded scheme the request carried, which
    /// is what separates a proxy that sends none from a proxy whose value this process does not believe.
    /// </para>
    /// </remarks>
    internal static Task RefuseATokenThatArrivedWithoutTransportEncryption(MessageReceivedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Request.IsHttps)
        {
            return Task.CompletedTask;
        }

        // The event fires for every request the scheme sees, and one that presented nothing has nothing refused, so
        // only a request that actually carried a credential is worth a record.
        if (context.Request.Headers.Authorization.Count > 0)
        {
            var forwardedProtocol = context.Request.Headers[ForwardedHeadersDefaults.XForwardedProtoHeaderName].ToString();
            var originalProtocol = context.Request.Headers[ForwardedHeadersDefaults.XOriginalProtoHeaderName].ToString();

            LogTokenRefusedOnAClearTextHop(
                context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger(typeof(TransportSecurityExtensions)),
                context.Scheme.Name,
                context.HttpContext.TraceIdentifier,
                context.Request.Method,
                context.Request.Path.Value ?? string.Empty,
                context.Request.Host.Value ?? string.Empty,
                context.Request.Scheme,
                string.IsNullOrEmpty(forwardedProtocol) ? "none" : forwardedProtocol,
                string.IsNullOrEmpty(originalProtocol) ? "none" : originalProtocol,
                context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                context.HttpContext.Connection.LocalPort);
        }

        context.NoResult();

        return Task.CompletedTask;
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message =
            "An access token presented to {AuthenticationScheme} arrived over a request this process currently sees as "
            + "clear text, so it was refused without being read. "
            + "Request: TraceIdentifier={TraceIdentifier}, Method={Method}, Path={Path}, Host={Host}, "
            + "Scheme={Scheme}, RemoteIp={RemoteIp}, LocalPort={LocalPort}. "
            + "Forwarded protocol state after forwarded-header processing: "
            + "X-Forwarded-Proto={ForwardedProtocol}, X-Original-Proto={OriginalProtocol}. "
            + "A remaining X-Forwarded-Proto value may be an unprocessed value or the unconsumed remainder of a forwarded "
            + "header chain; X-Original-Proto indicates that forwarded-header processing changed the request scheme at least "
            + "once. Correlate this request by TraceIdentifier with forwarded-header diagnostics to determine where the "
            + "request scheme became clear text.")]
    private static partial void LogTokenRefusedOnAClearTextHop(
        ILogger logger,
        string authenticationScheme,
        string traceIdentifier,
        string method,
        string path,
        string host,
        string scheme,
        string forwardedProtocol,
        string originalProtocol,
        string remoteIp,
        int localPort);

    /// <summary>Reports the scheme each configured issuer's tokens are validated by, keyed by the issuer a token names.</summary>
    /// <remarks>Composed here rather than in the selector because it is what a surface's registration already knows: which servers it registered, and what each is called.</remarks>
    private static Dictionary<string, string> OAuthSchemesByIssuer(
        TransportSurface surface,
        IReadOnlyList<AuthorizationServerOptions> authorizationServers) =>
        authorizationServers.ToDictionary(
            authorizationServer => authorizationServer.ValidatedIssuer(),
            authorizationServer => surface.OAuthSchemeNameFor(authorizationServer.Name!),
            StringComparer.Ordinal);

    /// <summary>Replaces each credential's administrator with the admission composed for it.</summary>
    private static Dictionary<string, AdministratorAdmission> AdmissionsByCredentialName(
        IReadOnlyDictionary<string, AdministratorOptions> administratorsByCredentialName,
        IReadOnlyDictionary<AdministratorOptions, AdministratorAdmission> admissions) =>
        administratorsByCredentialName.ToDictionary(
            entry => entry.Key,
            entry => admissions[entry.Value],
            StringComparer.Ordinal);

    /// <summary>Replaces each token identity's administrator with the admission composed for it.</summary>
    private static Dictionary<string, AdministratorAdmission> TokenAdmissionsByIdentity(
        IReadOnlyDictionary<string, AdministratorTokenBinding> bindingsByIdentity,
        IReadOnlyDictionary<AdministratorOptions, AdministratorAdmission> admissions) =>
        bindingsByIdentity.ToDictionary(
            entry => entry.Key,
            entry => admissions[entry.Value.Administrator],
            StringComparer.Ordinal);

    /// <summary>Registers the scheme that reads the presented credential and forwards it to the handler that judges it.</summary>
    /// <remarks>
    /// It is handed scheme names rather than the credentials behind them, which is what lets the configured axis and the
    /// user axis share one routing rule. Which handler judges a presented credential is decided by the shape of the
    /// credential and never by where the material that answers it is kept, so a surface whose keys are rows routes
    /// exactly as one whose keys are configuration entries does.
    /// </remarks>
    private static void AddRoutingScheme(
        AuthenticationBuilder authentication,
        TransportSurface surface,
        IReadOnlyDictionary<string, string> oauthSchemesByIssuer,
        string? apiKeySchemeName,
        string? clientAssertionSchemeName,
        string? basicSchemeName,
        string? sessionTokenSchemeName,
        string challengeSchemeName)
    {
        var schemeSelector = new CredentialSchemeSelector(
            oauthSchemesByIssuer,
            apiKeySchemeName,
            clientAssertionSchemeName,
            basicSchemeName,
            sessionTokenSchemeName,
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
    /// What a validated token then becomes is the caller's, because it is the one part the two axes genuinely differ
    /// on: a configured entry writes the grant it stated onto the identity, and a user-facing entry resolves the
    /// subject to a credential record and takes both the user and the grant from it. Everything above that — the
    /// issuer, the audience, the metadata retrieval, the clear-text refusal — is the same question asked of the same
    /// server, so it is asked once here.
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
        IHttpClientFactory transportFactory,
        Func<TokenValidatedContext, Task> onTokenValidated)
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
            OnTokenValidated = context => onTokenValidated(context),
        };
    }

    /// <summary>Reduces a validated token to the identity MailFathom keeps of it, binds it to its administrator, and writes the grant it holds.</summary>
    /// <remarks>
    /// <para>
    /// The validated principal carries every claim the authorization server chose to include, which routinely means a
    /// name, an address, and a set of groups. Replacing it here means nothing downstream can read one, so a later change
    /// cannot start depending on a claim the operator never mapped.
    /// </para>
    /// <para>
    /// A token whose issuer and subject name no administrator is refused here, as an authentication failure, rather than
    /// admitted and forbidden later: a validly signed token for somebody this deployment never named is no more a
    /// credential here than a key nobody configured. The same holds for an administrator's token arriving from outside
    /// the networks that administrator may act from.
    /// </para>
    /// </remarks>
    private static Task ReplacePrincipalWithAdministratorIdentity(
        TokenValidatedContext context,
        Dictionary<string, AdministratorAdmission> admissionsByIdentity)
    {
        var tokenIdentity = context.Principal is { } validatedPrincipal
            ? OAuthIdentity.FromValidatedToken(validatedPrincipal.Claims, context.Scheme.Name)
            : null;

        if (tokenIdentity is null)
        {
            context.Fail("The validated token names no subject.");

            return Task.CompletedTask;
        }

        if (OAuthIdentity.IdentityCarriedBy(new ClaimsPrincipal(tokenIdentity)) is not { } identity
            || !admissionsByIdentity.TryGetValue(identity, out var administrator))
        {
            context.Fail("The validated token names no configured administrator.");

            return Task.CompletedTask;
        }

        var logger = context.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(TransportSecurityExtensions));

        if (!administrator.AdmitsSourceOf(context.HttpContext, context.Scheme.Name, logger))
        {
            context.Fail("The administrator may not act from the network this request arrived from.");

            return Task.CompletedTask;
        }

        context.Principal = new ClaimsPrincipal(administrator.IdentityForToken(tokenIdentity, OAuthIdentity.RoleClaimType));

        return Task.CompletedTask;
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
        IReadOnlyList<AdministratorOptions> administrators)
    {
        var requiredScopesByIdentity = AdministratorConfiguration.TokenBindingsByIdentity(administrators)
            .ToDictionary(
                entry => entry.Key,
                entry => entry.Value.RequiredScopes,
                StringComparer.Ordinal);

        services.AddAuthorization(authorizationOptions => authorizationOptions.AddPolicy(
            surface.AccessPolicyName,
            policy => policy
                .AddAuthenticationSchemes(surface.RoutingSchemeName)
                .RequireAssertion(context =>
                    TransportAccessPolicy.IsAuthorized(context.User, requiredScopesByIdentity))));
    }
}
