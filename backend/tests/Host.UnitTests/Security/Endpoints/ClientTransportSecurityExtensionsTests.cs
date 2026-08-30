// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Access;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Security.Endpoints;
using MailFathom.Host.Security.Mcp;
using MailFathom.Host.Security.Transport;
using MailFathom.Infrastructure.Security.Transport;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
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

    /// <summary>An emptied list advertises nothing to a browser, which is what a deployment whose client is a desktop or mobile head wants.</summary>
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
        Assert.Equal([HttpMethods.Get], policy.Methods);
        Assert.Equal(
            [HeaderNames.Authorization, HeaderNames.ContentType, HeaderNames.Accept],
            policy.Headers);
    }

    /// <summary>A refusal says where to authorize, and a browser cannot read a response header the policy does not name.</summary>
    [Fact]
    public void AddClientTransportSecurity_ThePolicy_LetsAPageReadTheChallengeThatTellsItWhereToAuthorize() =>
        Assert.Contains(HeaderNames.WWWAuthenticate, ClientCorsPolicyOf(EnabledEndpoint()).ExposedHeaders);

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

    private static ServiceProvider ComposeOAuthOnlyEndpoint()
    {
        var services = new ServiceCollection();
        services.AddLogging();

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

        endpointSettings.Authentication.Add(new OwnerFacingAuthenticationOptions
        {
            Method = OwnerCredentialMethod.OAuthSubject.Name,
            OAuth = oauthSettings,
        });

        services.AddClientTransportSecurity(endpointSettings);

        return services.BuildServiceProvider();
    }
}
