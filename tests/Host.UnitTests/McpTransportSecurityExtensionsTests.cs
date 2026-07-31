// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailFathom.Host.Configuration;
using MailFathom.Host.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace MailFathom.Host.UnitTests;

/// <summary>Covers the decisions composition makes about the transport an MCP request arrives on.</summary>
/// <remarks>
/// Both are decisions nothing downstream can recover from: a token read off an unencrypted request has already been
/// disclosed by the time any handler sees it, and a header a browser is not permitted to read is one no client can act
/// on however correct the response is.
/// </remarks>
public sealed class McpTransportSecurityExtensionsTests
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
        await McpTransportSecurityExtensions.RefuseATokenThatArrivedWithoutTransportEncryption(context);

        // Assert
        Assert.NotNull(context.Result);
        Assert.True(context.Result.None);
    }

    [Fact]
    public async Task RefuseATokenThatArrivedWithoutTransportEncryption_AnEncryptedRequest_LeavesTheTokenToBeValidated()
    {
        // Arrange
        var context = MessageReceivedOver("https");

        // Act
        await McpTransportSecurityExtensions.RefuseATokenThatArrivedWithoutTransportEncryption(context);

        // Assert
        Assert.Null(context.Result);
    }

    /// <summary>
    /// The challenge names where to authorize and which scopes are required, and a browser cannot read a response header
    /// the policy does not expose. Without it the one answer that tells a page how to proceed is the one it cannot see.
    /// </summary>
    [Fact]
    public void AddMcpTransportSecurity_TheEndpointPolicy_LetsABrowserReadTheAuthenticationChallenge()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddMcpTransportSecurity(BrowserFacingEndpoint());

        // Assert
        using var composed = services.BuildServiceProvider();
        var policy = composed
            .GetRequiredService<IOptions<CorsOptions>>()
            .Value
            .GetPolicy(McpTransportSecurityExtensions.CorsPolicyName);

        Assert.NotNull(policy);
        Assert.Contains(HeaderNames.WWWAuthenticate, policy.ExposedHeaders);
    }

    private static McpEndpointOptions BrowserFacingEndpoint()
    {
        var endpointSettings = new McpEndpointOptions
        {
            Enabled = true,
            Authentication = McpTransportAuthenticationMethods.None,
        };

        endpointSettings.Cors.AllowedOrigins.Add("https://client.example.test");

        return endpointSettings;
    }

    private static MessageReceivedContext MessageReceivedOver(string scheme)
    {
        var request = new DefaultHttpContext();
        request.Request.Scheme = scheme;

        return new MessageReceivedContext(
            request,
            new AuthenticationScheme("MailFathomOAuth:workforce", displayName: null, typeof(JwtBearerHandler)),
            new JwtBearerOptions());
    }
}
