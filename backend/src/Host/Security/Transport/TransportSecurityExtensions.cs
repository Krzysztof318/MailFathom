// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Access;
using MailFathom.Host.Security.Mcp;
using MailFathom.Infrastructure.Security.OAuth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
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

    /// <summary>Registers the scheme that reads the presented credential and forwards it to the handler that judges it.</summary>
    /// <remarks>
    /// It is handed scheme names rather than the credentials behind them. Which handler judges a presented credential is
    /// decided by the shape of the credential and never by where the material that answers it is kept.
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
    /// What a validated token then becomes is the caller's: the subject resolves a credential record, which names the
    /// user. Everything above that — the issuer, the audience, the metadata retrieval, the clear-text refusal — is the
    /// same question asked of the same server on every surface, so it is asked once here.
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
}
