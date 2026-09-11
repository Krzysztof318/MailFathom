// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Claims;
using MailFathom.Application.Access.Credentials;
using MailFathom.Application.Access.Sessions;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Access;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Security.Endpoints;
using MailFathom.Host.Security.Mcp;
using MailFathom.Host.Security.Sessions;
using MailFathom.Host.Security.Transport;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Security.OAuth;
using MailFathom.Infrastructure.Security.Passwords;
using MailFathom.Infrastructure.Security.Transport;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Net.Http.Headers;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Security.Endpoints;

/// <summary>Covers what the client endpoint's composition decides about browsers and about credentials.</summary>
/// <remarks>
/// The CORS policy is the control that decides whether a WebAssembly head gets past its first preflight. It is also
/// the control most easily written too wide, so what the policy carries is asserted rather than left to the day a
/// route needs more.
/// </remarks>
public sealed class ClientTransportSecurityExtensionsTests
{
    private const string MappedIssuer = "https://sso.example.test";

    private const string MappedSubject = "11111111-2222-3333-4444-555555555555";

    private static readonly Guid MappedCredentialId = new("0197c0de-0000-7000-8000-000000000004");

    [Fact]
    public void AddClientTransportSecurity_AConfiguredOriginList_ServesExactlyThoseOrigins()
    {
        // Arrange
        var endpointSettings = EnabledEndpoint();
        endpointSettings.Cors.AllowedOrigins.Add("https://client.example.test");

        // Act
        var policy = ClientCorsPolicyOf(endpointSettings);

        // Assert
        Assert.False(policy.AllowAnyOrigin);
        Assert.Equal(["https://client.example.test"], policy.Origins);
    }

    /// <summary>What a deployment that configured no list receives, because a page whose preflight is refused is a client that never starts.</summary>
    [Fact]
    public void AddClientTransportSecurity_ThePermissivePosture_ServesEveryBrowserOrigin()
    {
        // Arrange
        var endpointSettings = EnabledEndpoint();
        endpointSettings.Cors.ServeEveryBrowserOrigin();

        // Act, Assert
        Assert.True(ClientCorsPolicyOf(endpointSettings).AllowAnyOrigin);
    }

    /// <summary>
    /// Both native heads render in a webview and enforce CORS against the origin their shell served the bundle from, so
    /// the custom-protocol origin three of the five platforms send has to reach the policy intact — otherwise the only
    /// posture serving those heads is every origin.
    /// </summary>
    [Fact]
    public void AddClientTransportSecurity_TheOriginOfADownloadedHead_ReachesThePolicyUnchanged()
    {
        // Arrange
        var endpointSettings = EnabledEndpoint();
        endpointSettings.Cors.AllowedOrigins.Add("tauri://localhost");
        endpointSettings.Cors.AllowedOrigins.Add("http://tauri.localhost");

        // Act
        var policy = ClientCorsPolicyOf(endpointSettings);

        // Assert
        Assert.False(policy.AllowAnyOrigin);
        Assert.Equal(
            ["http://tauri.localhost", "tauri://localhost"],
            policy.Origins.Order(StringComparer.Ordinal));
    }

    /// <summary>An emptied list advertises nothing to a browser, which is what a deployment whose only client is the page it serves itself wants.</summary>
    [Fact]
    public void AddClientTransportSecurity_AnEmptiedOriginList_AdvertisesNothingToABrowser()
    {
        // Act
        var policy = ClientCorsPolicyOf(EnabledEndpoint());

        // Assert
        Assert.False(policy.AllowAnyOrigin);
        Assert.Empty(policy.Origins);
    }

    /// <summary>
    /// A browser that could attach an ambient cookie would let a page act as whoever is logged in somewhere else, and
    /// this surface's credential is a bearer token the client sets deliberately. The combination is also one the CORS
    /// specification forbids outright beside <c>AllowAnyOrigin</c>.
    /// </summary>
    [Fact]
    public void AddClientTransportSecurity_AnyPosture_NeverLetsABrowserAttachAmbientCredentials()
    {
        // Arrange
        var permissive = EnabledEndpoint();
        permissive.Cors.ServeEveryBrowserOrigin();

        var narrowed = EnabledEndpoint();
        narrowed.Cors.AllowedOrigins.Add("https://client.example.test");

        // Act, Assert
        Assert.False(ClientCorsPolicyOf(permissive).SupportsCredentials);
        Assert.False(ClientCorsPolicyOf(narrowed).SupportsCredentials);
    }

    /// <summary>What this surface serves rather than what an HTTP API might, so a route added later widens the policy visibly instead of finding it already wide.</summary>
    [Fact]
    public void AddClientTransportSecurity_ThePolicy_AllowsOnlyWhatTheSurfaceServes()
    {
        // Act
        var policy = ClientCorsPolicyOf(EnabledEndpoint());

        // Assert
        Assert.Equal([HttpMethods.Get, HttpMethods.Post], policy.Methods);
        Assert.Equal(
            [
                HeaderNames.Authorization,
                HeaderNames.ContentType,
                HeaderNames.Accept,
                ClientTransportSecurityExtensions.TraceContextHeaderName,
            ],
            policy.Headers);
    }

    /// <summary>A preflight that refused the trace context header would leave every cross-origin client's span an orphan.</summary>
    /// <remarks>
    /// The client sends <c>traceparent</c> on every request it makes, so a browser whose preflight was not told the
    /// header is allowed refuses the request itself rather than dropping the header — which is the whole surface
    /// failing rather than one trace being unjoined.
    /// </remarks>
    [Fact]
    public void AddClientTransportSecurity_ThePolicy_LetsAPageJoinItsSpanToTheDeploymentsOwn() =>
        Assert.Contains(
            ClientTransportSecurityExtensions.TraceContextHeaderName,
            ClientCorsPolicyOf(EnabledEndpoint()).Headers);

    /// <summary>Baggage carries values rather than identifiers, and this surface has no business taking one from a page.</summary>
    [Theory]
    [InlineData("baggage")]
    [InlineData("tracestate")]
    public void AddClientTransportSecurity_ThePolicy_AdmitsNoPropagationHeaderBeyondTheTraceIdentifier(string header) =>
        Assert.DoesNotContain(header, ClientCorsPolicyOf(EnabledEndpoint()).Headers);

    /// <summary>A refusal says where to authorize, and a browser cannot read a response header the policy does not name.</summary>
    [Fact]
    public void AddClientTransportSecurity_ThePolicy_LetsAPageReadTheChallengeThatTellsItWhereToAuthorize() =>
        Assert.Contains(HeaderNames.WWWAuthenticate, ClientCorsPolicyOf(EnabledEndpoint()).ExposedHeaders);

    /// <summary>A client told to hold cannot obey an interval its own browser hid from it.</summary>
    [Fact]
    public void AddClientTransportSecurity_ThePolicy_LetsAPageReadHowLongItWasAskedToHoldFor() =>
        Assert.Contains(HeaderNames.RetryAfter, ClientCorsPolicyOf(EnabledEndpoint()).ExposedHeaders);

    /// <summary>An endpoint resolves exactly one policy by name, so two surfaces sharing one would let either deployment's origins decide what the other answers.</summary>
    [Fact]
    public void CorsPolicyName_TheClientSurface_SharesNoPolicyWithTheMcpOne() =>
        Assert.NotEqual(
            McpTransportSecurityExtensions.CorsPolicyName,
            ClientTransportSecurityExtensions.CorsPolicyName,
            StringComparer.Ordinal);

    /// <summary>
    /// The MCP surface registers its own origin policy as a service, and its DNS-rebinding check reads that
    /// registration. Registering a second instance here would leave which surface's origins that check enforced decided
    /// by the order composition happened to run in, so this surface builds its policy and registers nothing.
    /// </summary>
    [Fact]
    public void AddClientTransportSecurity_ComposedBesideTheMcpSurface_LeavesTheMcpOriginCheckReadingItsOwnOrigins()
    {
        // Arrange
        var mcpEndpointSettings = new McpEndpointOptions { Enabled = true };
        mcpEndpointSettings.Cors.AllowedOrigins.Add("https://agent.example.test");

        var clientEndpointSettings = EnabledEndpoint();
        clientEndpointSettings.Cors.ServeEveryBrowserOrigin();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMcpTransportSecurity(mcpEndpointSettings);
        services.AddClientTransportSecurity(clientEndpointSettings);

        using var composed = services.BuildServiceProvider();

        // Act
        var originPolicy = composed.GetRequiredService<BrowserOriginPolicy>();

        // Assert
        Assert.False(originPolicy.AllowsAnyOrigin);
        Assert.Equal(["https://agent.example.test"], originPolicy.AllowedOrigins);
    }

    /// <summary>
    /// A page begins holding nothing, and so does every probe of a running deployment. The scheme such a request reaches
    /// has to authenticate nobody so the pipeline can challenge; a scheme forwarding the question elsewhere answers a
    /// fault instead, and a fault tells a client nothing it can act on.
    /// </summary>
    [Fact]
    public async Task AddClientTransportSecurity_AnOAuthOnlyEndpoint_AuthenticatesNobodyForARequestCarryingNoCredential()
    {
        // Arrange
        using var composed = ComposeOAuthOnlyEndpoint();

        var request = new DefaultHttpContext { RequestServices = composed };
        request.Request.Scheme = "https";
        request.Request.Host = new HostString("mail.example.test");

        // Act
        var result = await composed
            .GetRequiredService<IAuthenticationService>()
            .AuthenticateAsync(request, TransportSurface.Client.RoutingSchemeName);

        // Assert
        Assert.True(result.None);
    }

    /// <summary>A token this deployment mapped names the credential behind it, so a session it is exchanged for is one that credential ends.</summary>
    /// <remarks>
    /// The principal an access token produces is named by the issuer and the subject rather than by the credential, so
    /// this is the one method where the exchange has nothing to read unless the claim is written. A session minted
    /// without it would be live until its own expiry whatever an operator did to the mapping.
    /// </remarks>
    [Fact]
    public async Task AddClientTransportSecurity_AnOAuthOnlyEndpoint_CarriesTheCredentialTheSubjectResolved()
    {
        // Arrange
        using var composed = ComposeOAuthOnlyEndpoint(MapsTheSubject);
        var validated = ValidatedTokenReaching(composed);

        // Act
        await composed
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(TransportSurface.Client.OAuthSchemeNameFor("workforce"))
            .Events!
            .OnTokenValidated(validated);

        // Assert
        Assert.NotNull(validated.Principal);
        Assert.Equal(MappedCredentialId, TransportCallerCredential.CarriedBy(validated.Principal));
    }

    /// <summary>A session this deployment minted authenticates on the composed surface and is admitted by the requirement its routes carry.</summary>
    /// <remarks>
    /// The seam nothing else reaches: the handler, the scheme selector and the access policy are each covered on their
    /// own, and every endpoint test calls a route method directly. A scheme registered under a name the selector does
    /// not return, a store resolved per scope rather than per process, or a policy that does not recognize a session
    /// principal would each sign every client out on the request after it signed in, with all of those still green.
    /// </remarks>
    [Fact]
    public async Task AddClientTransportSecurity_ASessionThisDeploymentMinted_AuthenticatesAndIsAdmittedByTheSurface()
    {
        // Arrange
        using var composed = ComposeOAuthOnlyEndpoint();
        var held = (await composed.GetRequiredService<ClientSessionTokens>()
            .MintAsync(AdmittedByACredential(), TestContext.Current.CancellationToken)).Token!;

        var request = new DefaultHttpContext { RequestServices = composed };
        request.Request.Scheme = "https";
        request.Request.Host = new HostString("mail.example.test");
        request.Request.Headers[HeaderNames.Authorization] = $"Bearer {held.Value}";

        // Act
        var authenticated = await composed
            .GetRequiredService<IAuthenticationService>()
            .AuthenticateAsync(request, TransportSurface.Client.RoutingSchemeName);

        var admitted = await composed
            .GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(authenticated.Principal!, resource: null, TransportSurface.Client.AccessPolicyName);

        // Assert
        Assert.True(authenticated.Succeeded);
        Assert.True(admitted.Succeeded);
    }

    /// <summary>A request presenting a session derives no key, which is the whole of what exchanging the password for one buys.</summary>
    /// <remarks>
    /// Asserted against a surface that takes passwords as well, because a surface taking none could not derive
    /// anything whatever the composition did. The hasher counts what a request asks of it — the derivation a
    /// verification performs and the one a rehash performs — rather than the decoy, which is derived once while the
    /// process composes and belongs to no request.
    /// </remarks>
    [Fact]
    public async Task AddClientTransportSecurity_ASessionPresentedWherePasswordsAreTakenToo_AuthenticatesWithoutDerivingAKey()
    {
        // Arrange
        var hasher = new CountingPasswordHasher();
        using var composed = ComposeEndpointTakingPasswordsAndSessions(hasher);
        var held = (await composed.GetRequiredService<ClientSessionTokens>()
            .MintAsync(AdmittedByACredential(), TestContext.Current.CancellationToken)).Token!;

        var request = new DefaultHttpContext { RequestServices = composed };
        request.Request.Headers[HeaderNames.Authorization] = $"Bearer {held.Value}";

        // Act
        var authenticated = await composed
            .GetRequiredService<IAuthenticationService>()
            .AuthenticateAsync(request, TransportSurface.Client.RoutingSchemeName);

        // Assert
        Assert.True(authenticated.Succeeded);
        Assert.Equal(0, hasher.Derivations);
    }

    /// <summary>The unauthenticated posture is served rather than refused, and a browser still has to be answered on it.</summary>
    [Fact]
    public void AddClientTransportSecurity_AnEndpointRequiringNoCredential_RegistersThePolicyAndNoScheme()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddClientTransportSecurity(EnabledEndpoint());

        using var composed = services.BuildServiceProvider();

        // Act
        var policy = composed
            .GetRequiredService<IOptions<CorsOptions>>()
            .Value
            .GetPolicy(ClientTransportSecurityExtensions.CorsPolicyName);

        // Assert
        Assert.NotNull(policy);
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IAuthenticationService));
    }

    /// <summary>An endpoint requiring no credential still holds the store, so the exchange it serves answers rather than faulting.</summary>
    /// <remarks>
    /// The exchange is mapped into the client group whatever the endpoint's posture is, and a client signs in the one
    /// way rather than asking which posture it is talking to first. Without the store it would resolve nothing and the
    /// route would fault on the first sign-in of a deployment that had deliberately asked for no credential.
    /// </remarks>
    [Fact]
    public void AddClientTransportSecurity_AnEndpointRequiringNoCredential_StillHoldsTheStoreTheExchangeMintsFrom()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddClientTransportSecurity(EnabledEndpoint());

        // Assert
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ClientSessionTokens));
    }

    private static CorsPolicy ClientCorsPolicyOf(ClientEndpointOptions endpointSettings)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddClientTransportSecurity(endpointSettings);

        using var composed = services.BuildServiceProvider();

        return composed
            .GetRequiredService<IOptions<CorsOptions>>()
            .Value
            .GetPolicy(ClientTransportSecurityExtensions.CorsPolicyName)!;
    }

    private static ClientEndpointOptions EnabledEndpoint() => new() { Enabled = true };

    private static AdmittedUserCredential AdmittedByACredential() => new(
        MappedCredentialId,
        MailUserId.Create(new Guid("0197c0de-0000-7000-8000-00000000ffff")),
        [MailFathomPermission.MailRead]);

    private static void MapsTheSubject(IServiceCollection services)
    {
        var credentials = Substitute.For<IUserCredentialStore>();
        Assert.True(UserCredentialLookup.TryCreateForOAuthSubject(MappedIssuer, MappedSubject, out var lookup));

        credentials.FindAsync(UserCredentialMethod.OAuthSubject, lookup, Arg.Any<CancellationToken>())
            .Returns(new ResolvedUserCredential(
                MappedCredentialId,
                MailUserId.Create(new Guid("0197c0de-0000-7000-8000-00000000ffff")),
                UserCredentialMethod.OAuthSubject,
                [MailFathomPermission.MailRead],
                Enabled: true,
                Material: null));

        services.AddSingleton(credentials);
        services.AddScoped<UserOAuthSubjectResolver>();
    }

    /// <summary>The context the authentication framework hands the event once a token's signature, issuer, audience, and lifetime have been checked.</summary>
    private static TokenValidatedContext ValidatedTokenReaching(ServiceProvider composed)
    {
        var scheme = new AuthenticationScheme(
            TransportSurface.Client.OAuthSchemeNameFor("workforce"),
            displayName: null,
            typeof(JwtBearerHandler));

        return new TokenValidatedContext(
            new DefaultHttpContext { RequestServices = composed },
            scheme,
            new JwtBearerOptions())
        {
            Principal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("iss", MappedIssuer), new Claim("sub", MappedSubject)],
                "test")),
        };
    }

    private static ServiceProvider ComposeOAuthOnlyEndpoint(Action<IServiceCollection>? alsoRegistering = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IClientSessionStore, InMemoryClientSessionStore>();
        alsoRegistering?.Invoke(services);

        var endpointSettings = EnabledEndpoint();
        var oauthSettings = new OAuthValidationOptions
        {
            Resource = $"https://mail.example.test:8080{ClientEndpointOptions.RoutePrefix}",
        };

        oauthSettings.AuthorizationServers.Add(new AuthorizationServerOptions
        {
            Name = "workforce",
            Issuer = "https://sso.example.test",
        });

        endpointSettings.Authentication.Add(new UserFacingAuthenticationOptions
        {
            Method = UserCredentialMethod.OAuthSubject.Name,
            OAuth = oauthSettings,
        });

        services.AddClientTransportSecurity(endpointSettings);

        return services.BuildServiceProvider();
    }

    /// <summary>Composes a client endpoint that takes a password and mints sessions, with the whole password graph behind it.</summary>
    /// <remarks>The credential store holds nobody, because what is asserted is which handler a value reaches rather than what a password would have resolved to.</remarks>
    private static ServiceProvider ComposeEndpointTakingPasswordsAndSessions(IPasswordHasher passwordHasher)
    {
        var credentials = Substitute.For<IUserCredentialStore>();
        credentials.FindAsync(
                Arg.Any<UserCredentialMethod>(),
                Arg.Any<UserCredentialLookup>(),
                Arg.Any<CancellationToken>())
            .Returns((ResolvedUserCredential?)null);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IClientSessionStore, InMemoryClientSessionStore>();
        services.AddSingleton(credentials);
        services.AddSingleton(passwordHasher);
        services.AddSingleton<TimeProvider>(new FakeTimeProvider());
        services.AddSingleton<PasswordAttemptLimiter>();
        services.AddSingleton<DecoyPasswordHash>();
        services.AddSingleton<UserPasswordAuthenticator>();

        var endpointSettings = EnabledEndpoint();
        endpointSettings.Authentication.Add(new UserFacingAuthenticationOptions
        {
            Method = UserCredentialMethod.Password.Name,
        });

        services.AddClientTransportSecurity(endpointSettings);

        return services.BuildServiceProvider();
    }

    /// <summary>Counts the key derivations a request asked for, and answers with a fixed stored representation.</summary>
    /// <remarks>Hand-written rather than substituted, because the members take the password as a <see cref="ReadOnlySpan{T}" /> and a dynamic proxy cannot carry a by-ref-like argument through its invocation.</remarks>
    private sealed class CountingPasswordHasher : IPasswordHasher
    {
        private const string StoredHash = "$mf1$stored$";

        internal int Derivations { get; private set; }

        public string HashDecoy() => StoredHash;

        public string Hash(ReadOnlySpan<char> password)
        {
            this.Derivations++;

            return StoredHash;
        }

        public PasswordVerification Verify(string storedHash, ReadOnlySpan<char> password)
        {
            this.Derivations++;

            return PasswordVerification.Failed;
        }
    }
}
