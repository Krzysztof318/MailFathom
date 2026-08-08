// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Access;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Security.Endpoints;
using MailFathom.Host.Security.Transport;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
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
}
