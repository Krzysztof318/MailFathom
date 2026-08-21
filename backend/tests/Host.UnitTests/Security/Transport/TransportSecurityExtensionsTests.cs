// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Claims;
using MailFathom.Common.ClientAssertions;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Access;
using MailFathom.Host.Security.ApiKeys;
using MailFathom.Host.Security.ClientAssertions;
using MailFathom.Host.Security.Transport;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.Infrastructure.Security.OAuth;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace MailFathom.Host.UnitTests.Security.Transport;

/// <summary>Covers what a surface's registration decides, and what it keeps separate from every other surface.</summary>
/// <remarks>
/// The isolation assertions are the reason this code was made to take a surface at all. Two surfaces sharing a scheme
/// name would mean one endpoint's credential silently satisfying the other's policy, which no test of either surface on
/// its own would notice.
/// </remarks>
public sealed class TransportSecurityExtensionsTests
{
    /// <summary>
    /// An access token is a reusable credential, so presenting one over plain HTTP hands it to anybody watching the
    /// network. The refusal is silent, which is what makes the answer the same challenge an unauthenticated request
    /// receives rather than a statement about what the request carried.
    /// </summary>
    [Fact]
    public async Task RefuseATokenThatArrivedWithoutTransportEncryption_APlaintextRequest_AuthenticatesNobody()
    {
        // Arrange
        var context = MessageReceivedOver("http");

        // Act
        await TransportSecurityExtensions.RefuseATokenThatArrivedWithoutTransportEncryption(context);

        // Assert
        Assert.NotNull(context.Result);
        Assert.True(context.Result.None);
    }

    /// <summary>
    /// The caller cannot tell this refusal from the challenge an anonymous request receives, which is deliberate and
    /// which is also why the operator has to be able to. Behind a proxy that stopped forwarding a scheme, this record
    /// is the only place the cause appears at all.
    /// </summary>
    [Fact]
    public async Task RefuseATokenThatArrivedWithoutTransportEncryption_APresentedToken_RecordsThatNoSchemeWasForwarded()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(logs));
        using var services = new ServiceCollection()
            .AddSingleton(loggerFactory)
            .BuildServiceProvider();

        var context = MessageReceivedOver("http", authorization: "Bearer a-token", services: services);

        // Act
        await TransportSecurityExtensions.RefuseATokenThatArrivedWithoutTransportEncryption(context);

        // Assert
        Assert.True(context.Result?.None);

        var record = Assert.Single(logs.Records);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal("none", Assert.Contains("ForwardedProtocol", record.Properties));
    }

    /// <summary>
    /// A forwarded scheme that arrived and was not applied is a different fault from one that was never sent — the
    /// first is who this process believes, the second is what the proxy sends — so the record names which it was.
    /// </summary>
    [Fact]
    public async Task RefuseATokenThatArrivedWithoutTransportEncryption_AForwardedSchemeThatWasNotApplied_RecordsIt()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(logs));
        using var services = new ServiceCollection()
            .AddSingleton(loggerFactory)
            .BuildServiceProvider();

        var context = MessageReceivedOver(
            "http",
            authorization: "Bearer a-token",
            forwardedProtocol: "https",
            services: services);

        // Act
        await TransportSecurityExtensions.RefuseATokenThatArrivedWithoutTransportEncryption(context);

        // Assert
        Assert.True(context.Result?.None);
        Assert.Equal("https", Assert.Contains("ForwardedProtocol", Assert.Single(logs.Records).Properties));
    }

    /// <summary>A request that presented nothing has nothing refused, so it is not worth a line in an operator's log.</summary>
    [Fact]
    public async Task RefuseATokenThatArrivedWithoutTransportEncryption_APlaintextRequestCarryingNoToken_RecordsNothing()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(logs));
        using var services = new ServiceCollection()
            .AddSingleton(loggerFactory)
            .BuildServiceProvider();

        var context = MessageReceivedOver("http", services: services);

        // Act
        await TransportSecurityExtensions.RefuseATokenThatArrivedWithoutTransportEncryption(context);

        // Assert
        Assert.True(context.Result?.None);
        Assert.Empty(logs.Records);
    }

    [Fact]
    public async Task RefuseATokenThatArrivedWithoutTransportEncryption_AnEncryptedRequest_LeavesTheTokenToBeValidated()
    {
        // Arrange
        var context = MessageReceivedOver("https");

        // Act
        await TransportSecurityExtensions.RefuseATokenThatArrivedWithoutTransportEncryption(context);

        // Assert
        Assert.Null(context.Result);
    }

    /// <summary>
    /// A surface registers its own schemes and claims nothing about the application. There is one default scheme and
    /// one authentication middleware running it over every request, so a surface holding it would be authenticating the
    /// other surface's requests with its own handlers — which is what the composition root's own scheme exists to stop.
    /// </summary>
    [Fact]
    public void AddTransportAuthentication_ASurface_LeavesTheApplicationDefaultUnclaimed()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        AddApiKeyAuthentication(services, TransportSurface.Mcp);

        // Assert
        using var composed = services.BuildServiceProvider();
        var authentication = composed.GetRequiredService<IOptions<AuthenticationOptions>>().Value;

        Assert.Null(authentication.DefaultScheme);
    }

    /// <summary>
    /// The policy names one routing scheme, and that is the whole of what keeps two surfaces apart. Naming the other
    /// surface's scheme as well — or naming none, which lets the application default answer — would let a credential
    /// issued for one endpoint satisfy the other's requirement without any check noticing.
    /// </summary>
    [Fact]
    public void AddTransportAuthentication_TheSurfacesPolicy_ConsultsThatSurfacesRoutingSchemeAlone()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        AddApiKeyAuthentication(services, TransportSurface.Mcp);

        // Assert
        using var composed = services.BuildServiceProvider();
        var policy = composed
            .GetRequiredService<IOptions<AuthorizationOptions>>()
            .Value
            .GetPolicy(TransportSurface.Mcp.AccessPolicyName);

        Assert.NotNull(policy);
        Assert.Equal([TransportSurface.Mcp.RoutingSchemeName], policy.AuthenticationSchemes);
    }

    /// <summary>
    /// A surface accepting assertions registers the scheme its routing forwards to, over its own key list and its own
    /// audience. Forwarding to a scheme nothing registered would answer every request with a framework failure rather
    /// than a refusal, and an audience taken from anywhere but the surface would let a credential minted to read a
    /// mailbox administer the service.
    /// </summary>
    [Fact]
    public void AddTransportAuthentication_ASurfaceAcceptingAssertions_RegistersTheSchemeOverItsOwnKeysAndAudience()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var publicKey = new ConfiguredSecret { Name = "reporting-job", SecretReference = "plaintext:a-public-key" };

        // Act
        services.AddTransportAuthentication(
            TransportSurface.Admin,
            [new TransportAuthenticationOptions { PublicKey = publicKey }],
            TransportSurface.Admin.ClientAssertionSchemeName);

        // Assert
        using var composed = services.BuildServiceProvider();
        var schemeOptions = composed
            .GetRequiredService<IOptionsMonitor<ClientAssertionAuthenticationSchemeOptions>>()
            .Get(TransportSurface.Admin.ClientAssertionSchemeName);

        Assert.Equal([publicKey], schemeOptions.PublicKeys);
        Assert.Equal(ClientAssertion.AdminAudience, schemeOptions.Surface.ClientAssertionAudience);
    }

    /// <summary>A surface accepting no assertion registers no verifier, so nothing resolves a key list it was never given.</summary>
    [Fact]
    public void AddTransportAuthentication_ASurfaceAcceptingNoAssertion_RegistersNoAssertionScheme()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        AddApiKeyAuthentication(services, TransportSurface.Mcp);

        // Assert
        using var composed = services.BuildServiceProvider();
        var schemes = composed.GetRequiredService<IOptions<AuthenticationOptions>>().Value.Schemes;

        Assert.DoesNotContain(
            schemes,
            scheme => scheme.Name == TransportSurface.Mcp.ClientAssertionSchemeName);
    }

    /// <summary>
    /// Every name a surface registers is composed from the surface, so no two surfaces can collide on one. A shared
    /// name would merge two policies into whichever registration ran last, and the endpoint that lost would be
    /// protected by settings its operator never wrote.
    /// </summary>
    [Fact]
    public void TransportSurface_TwoSurfaces_ShareNoSchemeOrPolicyName()
    {
        // Arrange: the second surface is built the way a further one would be, through the same public shape.
        var mcp = TransportSurface.Mcp;

        // Act
        string[] mcpNames =
        [
            mcp.RoutingSchemeName,
            mcp.ApiKeySchemeName,
            mcp.ClientAssertionSchemeName,
            mcp.AccessPolicyName,
            mcp.OAuthSchemeNameFor("workforce"),
        ];

        // Assert: every name carries the surface, so a surface named otherwise produces a disjoint set.
        Assert.All(mcpNames, name => Assert.Contains($":{mcp.Name}:", name, StringComparison.Ordinal));
        Assert.Equal(mcpNames.Length, mcpNames.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void AddTransportAuthentication_TheStructDefaultAsASurface_IsRefusedRatherThanRegisteringUnnamedSchemes()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act, Assert
        Assert.Throws<ArgumentException>(() => AddApiKeyAuthentication(services, default));
    }

    /// <summary>
    /// And registering two surfaces leaves it unclaimed as well, so which surface was composed first decides nothing.
    /// It used to: the later registration held the default, and with it the scheme the authentication middleware ran to
    /// populate the principal the MCP rate limiter partitions on — so enabling the administrative endpoint silently
    /// collapsed every authenticated MCP client into the shared anonymous bucket.
    /// </summary>
    [Fact]
    public void AddTransportAuthentication_TwoSurfaces_LeaveTheApplicationDefaultUnclaimed()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        AddApiKeyAuthentication(services, TransportSurface.Mcp);
        AddApiKeyAuthentication(services, TransportSurface.Admin);

        // Assert
        using var composed = services.BuildServiceProvider();

        Assert.Null(composed.GetRequiredService<IOptions<AuthenticationOptions>>().Value.DefaultScheme);
    }

    /// <summary>
    /// Every setting a token is judged by is applied through one options registration, and nothing fails if that
    /// registration never runs — the scheme would simply validate with framework defaults: no configured metadata
    /// address, error details returned to the caller, and inbound claims renamed out from under the identity mapping.
    /// Reading the options back is what turns a silent misconfiguration into a failing test.
    /// </summary>
    [Fact]
    public void AddTransportAuthentication_AnOAuthSurface_ConfiguresTheSchemeTheTokenIsJudgedBy()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTransportAuthentication(
            TransportSurface.Mcp,
            [new TransportAuthenticationOptions { OAuth = AnAuthorizationServer() }],
            TransportSurface.Mcp.OAuthSchemeNameFor("workforce"));

        using var composed = services.BuildServiceProvider();

        // Act
        var schemeOptions = composed.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(TransportSurface.Mcp.OAuthSchemeNameFor("workforce"));

        // Assert
        Assert.Equal(
            "https://sso.example.test/.well-known/oauth-authorization-server",
            schemeOptions.MetadataAddress);
        Assert.True(schemeOptions.RequireHttpsMetadata);
        Assert.False(schemeOptions.MapInboundClaims);
        Assert.False(schemeOptions.IncludeErrorDetails);
    }

    /// <summary>
    /// The backchannel is resolved from the client factory rather than constructed, so the bounds it carries are the
    /// registration's. A scheme reaching this point with the framework's own client would follow a redirect away from
    /// the configured server and read an unbounded body during a key refresh, both inside a request's authentication
    /// path, and every assertion about metadata retrieval elsewhere would be describing a client nothing built.
    /// </summary>
    [Fact]
    public void AddTransportAuthentication_AnOAuthSurface_GivesTheSchemeTheRegisteredMetadataBackchannel()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTransportAuthentication(
            TransportSurface.Mcp,
            [new TransportAuthenticationOptions { OAuth = AnAuthorizationServer() }],
            TransportSurface.Mcp.OAuthSchemeNameFor("workforce"));

        using var composed = services.BuildServiceProvider();

        // Act
        var schemeOptions = composed.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(TransportSurface.Mcp.OAuthSchemeNameFor("workforce"));

        // Assert
        Assert.NotNull(schemeOptions.Backchannel);
        Assert.Equal(OAuthValidationOptions.MetadataRetrievalTimeout, schemeOptions.Backchannel.Timeout);
    }

    /// <summary>
    /// The backchannel is held for the life of the scheme that owns it, so no handler rotation reaches it and the
    /// connection it pools is the only thing that ever makes the address be resolved again. Without the lifetime an
    /// authorization server that moves is reached at where it used to be until the process restarts, and without the
    /// redirect refusal a key refresh can be sent somewhere the configuration never named. Both are settings rather
    /// than behaviour a request could reveal, so a test that did not read them back would leave the defect this change
    /// exists to fix free to return with every other assertion still passing.
    /// </summary>
    [Fact]
    public void AddTransportAuthentication_AnOAuthSurface_BoundsTheBackchannelConnection()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTransportAuthentication(
            TransportSurface.Mcp,
            [new TransportAuthenticationOptions { OAuth = AnAuthorizationServer() }],
            TransportSurface.Mcp.OAuthSchemeNameFor("workforce"));

        using var composed = services.BuildServiceProvider();

        // Act
        var chain = HandlersOf(composed.GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(TransportSecurityExtensions.MetadataBackchannelTransportName));

        // Assert
        Assert.Contains(chain, handler => handler is BoundedMetadataHttpMessageHandler);

        var connection = Assert.IsType<SocketsHttpHandler>(chain[^1]);
        Assert.Equal(OAuthValidationOptions.MetadataConnectionLifetime, connection.PooledConnectionLifetime);
        Assert.False(connection.AllowAutoRedirect);
    }

    /// <summary>
    /// Both surfaces may enable OAuth, and each registers the one metadata transport under the same name, so the
    /// registration is written to assign rather than append. Nothing about that is visible in a deployment serving one
    /// surface, which is every other test here: a registration that appended would leave the shared client carrying a
    /// second bounded handler, and the response body of every key refresh would be buffered and length-checked twice
    /// over on the deployments that enable both.
    /// </summary>
    [Fact]
    public void AddTransportAuthentication_TwoOAuthSurfaces_LeaveOneBoundedHandlerOnTheSharedBackchannel()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        foreach (var surface in new[] { TransportSurface.Mcp, TransportSurface.Admin })
        {
            services.AddTransportAuthentication(
                surface,
                [new TransportAuthenticationOptions { OAuth = AnAuthorizationServer() }],
                surface.OAuthSchemeNameFor("workforce"));
        }

        using var composed = services.BuildServiceProvider();
        var chain = HandlersOf(composed.GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(TransportSecurityExtensions.MetadataBackchannelTransportName));

        // Assert
        Assert.Single(chain, handler => handler is BoundedMetadataHttpMessageHandler);

        var connection = Assert.IsType<SocketsHttpHandler>(chain[^1]);
        Assert.Equal(OAuthValidationOptions.MetadataConnectionLifetime, connection.PooledConnectionLifetime);
    }

    /// <summary>
    /// The grant belongs to the entry, so the scheme has to be registered with each key's own entry's grant rather
    /// than with one set for the surface. Registering a shared set would give a key an operator narrowed whatever the
    /// entry beside it holds, which is the failure the entry-scoped grant exists to prevent.
    /// </summary>
    [Fact]
    public void AddTransportAuthentication_TwoKeysGrantedDifferently_GivesTheApiKeySchemeTheGrantOfEachEntry()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        var narrowed = new TransportAuthenticationOptions
        {
            ApiKey = new ConfiguredSecret { Name = "reporting-job", SecretReference = "plaintext:a-key" },
        };

        narrowed.Permissions.Add(MailFathomPermission.MailRead.Name);

        var unnarrowed = new TransportAuthenticationOptions
        {
            ApiKey = new ConfiguredSecret { Name = "workstation", SecretReference = "plaintext:another-key" },
        };

        unnarrowed.GrantTheWholeSurface();

        // Act
        services.AddTransportAuthentication(
            TransportSurface.Mcp,
            [narrowed, unnarrowed],
            TransportSurface.Mcp.ApiKeySchemeName);

        // Assert
        using var composed = services.BuildServiceProvider();
        var schemeOptions = composed
            .GetRequiredService<IOptionsMonitor<ApiKeyAuthenticationSchemeOptions>>()
            .Get(TransportSurface.Mcp.ApiKeySchemeName);

        Assert.Equal([MailFathomPermission.MailRead], schemeOptions.GrantsByKeyName["reporting-job"]);
        Assert.Equal(
            MailFathomPermission.PublishedFor(ProtectedSurface.Mail),
            schemeOptions.GrantsByKeyName["workstation"]);
    }

    /// <summary>The assertion scheme carries the same map for the same reason, because a public key's entry is where its grant was written too.</summary>
    [Fact]
    public void AddTransportAuthentication_AnAssertionEntryGrantedNothing_GivesTheSchemeAnEmptyGrantForThatKey()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        var retired = new TransportAuthenticationOptions
        {
            PublicKey = new ConfiguredSecret { Name = "nightly", SecretReference = "plaintext:a-public-key" },
        };


        // Act
        services.AddTransportAuthentication(
            TransportSurface.Admin,
            [retired],
            TransportSurface.Admin.ClientAssertionSchemeName);

        // Assert
        using var composed = services.BuildServiceProvider();
        var schemeOptions = composed
            .GetRequiredService<IOptionsMonitor<ClientAssertionAuthenticationSchemeOptions>>()
            .Get(TransportSurface.Admin.ClientAssertionSchemeName);

        Assert.Empty(schemeOptions.GrantsByKeyName["nightly"]);
    }

    /// <summary>Without the narrowing setting the deployment wrote the grant and the authorization server was never asked, so every token the entry admits holds the whole ceiling.</summary>
    [Fact]
    public async Task OnTokenValidated_AnEntryGrantingFromConfiguration_GivesEveryTokenTheWholeGrant()
    {
        // Arrange
        var entry = new TransportAuthenticationOptions { OAuth = AnAuthorizationServer() };
        entry.Permissions.Add(MailFathomPermission.MailRead.Name);

        var context = TokenValidatedFor(entry, tokenScopes: "mailfathom.mail.ask");

        // Act
        await ValidatedTokenEventOf(entry).Invoke(context);

        // Assert
        Assert.Equal(
            [MailFathomPermission.MailRead],
            TransportGrant.PermissionsCarriedBy(context.Principal!));
    }

    /// <summary>With it, the authorization server decides per subject within the bound the deployment fixed, so a token holds the intersection and nothing else.</summary>
    [Fact]
    public async Task OnTokenValidated_AnEntryNarrowedByTokenScopes_GivesTheTokenOnlyWhatItsScopesCarry()
    {
        // Arrange
        var entry = new TransportAuthenticationOptions
        {
            OAuth = AnAuthorizationServer(),
            PermissionsFromTokenScopes = true,
        };

        entry.Permissions.Add(MailFathomPermission.MailRead.Name);
        entry.Permissions.Add(MailFathomPermission.MailAsk.Name);

        var context = TokenValidatedFor(entry, tokenScopes: "mailfathom.mail.read offline_access");

        // Act
        await ValidatedTokenEventOf(entry).Invoke(context);

        // Assert
        Assert.Equal(
            [MailFathomPermission.MailRead],
            TransportGrant.PermissionsCarriedBy(context.Principal!));
    }

    /// <summary>A scope naming a permission the entry never granted must not widen it, or the authorization server would be deciding the ceiling rather than the deployment.</summary>
    [Fact]
    public async Task OnTokenValidated_ATokenCarryingAPermissionOutsideTheCeiling_HoldsNoneOfIt()
    {
        // Arrange
        var entry = new TransportAuthenticationOptions
        {
            OAuth = AnAuthorizationServer(),
            PermissionsFromTokenScopes = true,
        };

        entry.Permissions.Add(MailFathomPermission.MailRead.Name);

        var context = TokenValidatedFor(entry, tokenScopes: "mailfathom.mail.ask");

        // Act
        await ValidatedTokenEventOf(entry).Invoke(context);

        // Assert
        Assert.Empty(TransportGrant.PermissionsCarriedBy(context.Principal!));
    }

    /// <summary>A scope is compared byte for byte, so the shorthand a deployment may write in its own configuration grants nothing when a token brings it instead.</summary>
    [Fact]
    public async Task OnTokenValidated_ATokenCarryingASubtreePattern_HoldsNoneOfIt()
    {
        // Arrange
        var entry = new TransportAuthenticationOptions
        {
            OAuth = AnAuthorizationServer(),
            PermissionsFromTokenScopes = true,
        };

        entry.Permissions.Add("mailfathom.mail.*");

        var context = TokenValidatedFor(entry, tokenScopes: "mailfathom.mail.*");

        // Act
        await ValidatedTokenEventOf(entry).Invoke(context);

        // Assert
        Assert.Empty(TransportGrant.PermissionsCarriedBy(context.Principal!));
    }

    /// <summary>Reads the registered event that reduces a validated token to the identity this host keeps and writes the grant onto it.</summary>
    /// <remarks>Reached through the registration rather than called directly, because what is under test is that the entry's grant was captured where the scheme was configured.</remarks>
    private static Func<TokenValidatedContext, Task> ValidatedTokenEventOf(TransportAuthenticationOptions entry)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTransportAuthentication(
            TransportSurface.Mcp,
            [entry],
            TransportSurface.Mcp.OAuthSchemeNameFor("workforce"));

        using var composed = services.BuildServiceProvider();

        var schemeOptions = composed.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(TransportSurface.Mcp.OAuthSchemeNameFor("workforce"));

        return schemeOptions.Events!.OnTokenValidated;
    }

    /// <summary>Builds the context the authentication framework hands the event once a token's signature, issuer, audience, and lifetime have been checked.</summary>
    private static TokenValidatedContext TokenValidatedFor(TransportAuthenticationOptions entry, string tokenScopes)
    {
        var scheme = new AuthenticationScheme(
            TransportSurface.Mcp.OAuthSchemeNameFor("workforce"),
            displayName: null,
            typeof(JwtBearerHandler));

        return new TokenValidatedContext(new DefaultHttpContext(), scheme, new JwtBearerOptions())
        {
            Principal = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim("iss", entry.OAuth!.AuthorizationServers[0].Issuer!),
                    new Claim("sub", "11111111-2222-3333-4444-555555555555"),
                    new Claim("scope", tokenScopes),
                ],
                "test")),
        };
    }

    /// <summary>Reads a built handler chain from its outermost handler down to the one that opens the connection.</summary>
    /// <remarks><see cref="DelegatingHandler.InnerHandler" /> is public, so the walk needs no reflection; the chain ends at the first handler that delegates to nothing.</remarks>
    private static List<HttpMessageHandler> HandlersOf(HttpMessageHandler outermost)
    {
        var chain = new List<HttpMessageHandler>();

        for (var handler = outermost; handler is not null; handler = (handler as DelegatingHandler)?.InnerHandler)
        {
            chain.Add(handler);
        }

        return chain;
    }

    private static OAuthValidationOptions AnAuthorizationServer()
    {
        var oauthSettings = new OAuthValidationOptions { Resource = "https://mail.example.test/mcp" };
        oauthSettings.AuthorizationServers.Add(new AuthorizationServerOptions
        {
            Name = "workforce",
            Issuer = "https://sso.example.test",
        });

        return oauthSettings;
    }

    private static void AddApiKeyAuthentication(IServiceCollection services, TransportSurface surface) =>
        services.AddTransportAuthentication(
            surface,
            [
                new TransportAuthenticationOptions
                {
                    ApiKey = new ConfiguredSecret { Name = "workstation", SecretReference = "plaintext:not-a-real-key" },
                },
            ],
            surface.IsSpecified ? surface.ApiKeySchemeName : "unused");

    private static MessageReceivedContext MessageReceivedOver(
        string scheme,
        string? authorization = null,
        string? forwardedProtocol = null,
        IServiceProvider? services = null)
    {
        var request = new DefaultHttpContext();
        request.Request.Scheme = scheme;

        if (authorization is not null)
        {
            request.Request.Headers.Authorization = authorization;
        }

        if (forwardedProtocol is not null)
        {
            request.Request.Headers["X-Forwarded-Proto"] = forwardedProtocol;
        }

        if (services is not null)
        {
            request.RequestServices = services;
        }

        return new MessageReceivedContext(
            request,
            new AuthenticationScheme(
                TransportSurface.Mcp.OAuthSchemeNameFor("workforce"),
                displayName: null,
                typeof(JwtBearerHandler)),
            new JwtBearerOptions());
    }
}
