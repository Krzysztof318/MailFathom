// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Access;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Security.Endpoints;
using MailFathom.Host.Security.Mcp;
using MailFathom.Host.Security.Transport;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace MailFathom.Host.UnitTests.Security.Endpoints;

/// <summary>Covers what the administrative endpoint's composition decides about the credentials it accepts.</summary>
/// <remarks>
/// This surface reaches the same routing scheme through the same abstraction the MCP endpoint does, so the question a
/// credential-less request raises there is a question here too — and it is answered by a different scheme, which is why
/// it is asserted rather than inferred from the other surface passing.
/// </remarks>
public sealed class AdminTransportSecurityExtensionsTests
{
    /// <summary>What a deployment that configured no list receives, so a first run and a local orchestration answer a preflight.</summary>
    [Fact]
    public void AddAdminTransportSecurity_ThePermissivePosture_ServesEveryBrowserOrigin()
    {
        // Arrange
        var endpointSettings = new AdminEndpointOptions { Enabled = true };
        endpointSettings.Cors.ServeEveryBrowserOrigin();

        // Act, Assert
        Assert.True(AdminCorsPolicyOf(endpointSettings).AllowAnyOrigin);
    }

    /// <summary>The methods this surface actually serves, so a route added later widens the policy visibly.</summary>
    [Fact]
    public void AddAdminTransportSecurity_ThePolicy_AllowsTheMethodsTheSurfaceServes()
    {
        // Act
        var policy = AdminCorsPolicyOf(EnabledUnauthenticated());

        // Assert
        Assert.Equal(
            [HttpMethods.Get, HttpMethods.Post, HttpMethods.Put, HttpMethods.Delete],
            policy.Methods);
        Assert.Equal(
            [HeaderNames.Authorization, HeaderNames.ContentType, HeaderNames.Accept],
            policy.Headers);
    }

    /// <summary>The unauthenticated posture is served rather than refused, and a browser still has to be answered on it.</summary>
    [Fact]
    public void AddAdminTransportSecurity_AnEndpointRequiringNoCredential_RegistersThePolicyAndNoScheme()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAdminTransportSecurity(EnabledUnauthenticated());

        using var composed = services.BuildServiceProvider();

        // Act
        var policy = composed
            .GetRequiredService<IOptions<CorsOptions>>()
            .Value
            .GetPolicy(AdminTransportSecurityExtensions.CorsPolicyName);

        // Assert
        Assert.NotNull(policy);
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IAuthenticationService));
    }

    /// <summary>An endpoint resolves exactly one policy by name, so two surfaces sharing one would let either deployment's origins decide what the other answers.</summary>
    [Fact]
    public void CorsPolicyName_TheAdministrativeSurface_SharesNoPolicyWithTheOtherTwo()
    {
        Assert.NotEqual(
            McpTransportSecurityExtensions.CorsPolicyName,
            AdminTransportSecurityExtensions.CorsPolicyName,
            StringComparer.Ordinal);
        Assert.NotEqual(
            ClientTransportSecurityExtensions.CorsPolicyName,
            AdminTransportSecurityExtensions.CorsPolicyName,
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Every <c>mfctl login</c> begins with a request holding nothing, and so does every probe of a running deployment.
    /// The scheme such a request reaches has to authenticate nobody, so the pipeline can challenge; a scheme forwarding
    /// the question elsewhere answers a fault instead, and a fault is not a posture an operator can log in through.
    /// This surface names one of its own token validators, which reads no credential out of such a request and forwards
    /// nothing — but that is a property of the scheme it happens to name rather than of naming one, so it is asserted.
    /// </summary>
    [Fact]
    public async Task AddAdminTransportSecurity_AnOAuthOnlyEndpoint_AuthenticatesNobodyForARequestCarryingNoCredential()
    {
        // Arrange
        using var composed = ComposeOAuthOnlyEndpoint();

        var request = new DefaultHttpContext { RequestServices = composed };
        request.Request.Scheme = "https";
        request.Request.Host = new HostString("mail.example.test");

        // Act
        var result = await composed
            .GetRequiredService<IAuthenticationService>()
            .AuthenticateAsync(request, TransportSurface.Admin.RoutingSchemeName);

        // Assert
        Assert.True(result.None);
    }

    private static ServiceProvider ComposeOAuthOnlyEndpoint()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var endpointSettings = new AdminEndpointOptions { Enabled = true };
        var oauthSettings = new OAuthValidationOptions
        {
            Resource = $"https://mail.example.test:8090{AdminEndpointOptions.RoutePrefix}",
        };

        oauthSettings.AuthorizationServers.Add(new AuthorizationServerOptions
        {
            Name = "workforce",
            Issuer = "https://sso.example.test",
        });

        endpointSettings.Authentication.Add(new TransportAuthenticationOptions { OAuth = oauthSettings });

        services.AddAdminTransportSecurity(endpointSettings);

        return services.BuildServiceProvider();
    }

    private static AdminEndpointOptions EnabledUnauthenticated()
    {
        var settings = new AdminEndpointOptions { Enabled = true };
        settings.Cors.ServeEveryBrowserOrigin();
        return settings;
    }

    private static CorsPolicy AdminCorsPolicyOf(AdminEndpointOptions endpointSettings)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAdminTransportSecurity(endpointSettings);

        using var composed = services.BuildServiceProvider();

        return composed
            .GetRequiredService<IOptions<CorsOptions>>()
            .Value
            .GetPolicy(AdminTransportSecurityExtensions.CorsPolicyName)
            ?? throw new InvalidOperationException("The administrative CORS policy was not registered.");
    }
}
