// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Access;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Security.Mcp;
using MailFathom.Host.Security.Transport;
using MailFathom.Infrastructure.Secrets.Discovery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace MailFathom.Host.UnitTests.Security.Mcp;

/// <summary>Covers what the MCP endpoint's own composition decides, beyond the credentials every surface accepts.</summary>
/// <remarks>
/// A header a browser is not permitted to read is one no client can act on however correct the response is, which is
/// why the exposed header is asserted on the composed policy rather than on the call that configured it.
/// </remarks>
public sealed class McpTransportSecurityExtensionsTests
{
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

    /// <summary>
    /// The challenge forwards to one named scheme, and it has to be one this endpoint actually registered. An endpoint
    /// accepting only client assertions registers no API key scheme, so naming that one would leave every request
    /// arriving without a credential — the ordinary first request of every client — forwarded to a scheme that does not
    /// exist, which is a fault rather than a refusal and would appear on no configuration a test happens to compose.
    /// </summary>
    [Fact]
    public void AddMcpTransportSecurity_AnEndpointAcceptingOnlyAssertions_ChallengesThroughARegisteredScheme()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        var endpointSettings = new McpEndpointOptions { Enabled = true };
        endpointSettings.Authentication.Add(new TransportAuthenticationOptions
        {
            PublicKey = new ConfiguredSecret { Name = "reporting-job", SecretReference = "plaintext:a-public-key" },
        });

        // Act
        services.AddMcpTransportSecurity(endpointSettings);

        // Assert
        using var composed = services.BuildServiceProvider();
        var schemes = composed.GetRequiredService<IOptions<AuthenticationOptions>>().Value.Schemes;

        Assert.Contains(schemes, scheme => scheme.Name == TransportSurface.Mcp.ClientAssertionSchemeName);
        Assert.DoesNotContain(schemes, scheme => scheme.Name == TransportSurface.Mcp.ApiKeySchemeName);
    }

    private static McpEndpointOptions BrowserFacingEndpoint()
    {
        var endpointSettings = new McpEndpointOptions
        {
            Enabled = true,
        };

        endpointSettings.Cors.AllowedOrigins.Add("https://client.example.test");

        return endpointSettings;
    }
}
