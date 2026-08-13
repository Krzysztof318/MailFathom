// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers.Text;
using System.Text;
using MailFathom.Host.Api;
using MailFathom.Host.Configuration.Access;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Security.Mcp;
using MailFathom.Host.Security.Transport;
using MailFathom.Infrastructure.Secrets.Discovery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using ModelContextProtocol.AspNetCore.Authentication;
using Xunit;

namespace MailFathom.Host.UnitTests.Security.Mcp;

/// <summary>Covers what the MCP endpoint's own composition decides, beyond the credentials every surface accepts.</summary>
/// <remarks>
/// A header a browser is not permitted to read is one no client can act on however correct the response is, which is
/// why the exposed header is asserted on the composed policy rather than on the call that configured it.
/// </remarks>
public sealed class McpTransportSecurityExtensionsTests
{
    private const string McpResource = "https://mail.example.test/mcp";

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

    /// <summary>
    /// The first request every MCP client makes carries no credential, and so does every request that presents something
    /// this endpoint cannot place. Both reach the scheme that answers the challenge, so that scheme has to authenticate
    /// nobody rather than forward the question somewhere. Forwarded, an OAuth-only endpoint answers a fault instead of a
    /// refusal, which is the whole of what a client sees: discovery never starts and no client reaches the point of being
    /// told how to authorize.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("Bearer an-opaque-credential-nobody-issued")]
    [InlineData("Bearer not.a.token")]
    public async Task AddMcpTransportSecurity_AnOAuthOnlyEndpoint_AuthenticatesNobodyForACredentialItCannotPlace(
        string? authorizationHeaderValue)
    {
        // Arrange
        using var composed = ComposeOAuthOnlyEndpoint();
        var request = RequestTo(composed, authorizationHeaderValue);

        // Act
        var result = await composed
            .GetRequiredService<IAuthenticationService>()
            .AuthenticateAsync(request, TransportSurface.Mcp.RoutingSchemeName);

        // Assert
        Assert.True(result.None);
    }

    /// <summary>
    /// And what that request is answered with. The pointer is how a client finds the authorization server at all, so a
    /// challenge carrying anything else leaves an endpoint that refuses correctly and is still unreachable.
    /// </summary>
    [Fact]
    public async Task AddMcpTransportSecurity_AnOAuthOnlyEndpoint_ChallengesWithThePointerToItsMetadataDocument()
    {
        // Arrange
        using var composed = ComposeOAuthOnlyEndpoint();
        var request = RequestTo(composed, authorizationHeaderValue: null);

        // Act
        await composed
            .GetRequiredService<IAuthenticationService>()
            .ChallengeAsync(request, TransportSurface.Mcp.RoutingSchemeName, properties: null);

        // Assert
        Assert.Equal(StatusCodes.Status401Unauthorized, request.Response.StatusCode);
        Assert.Equal(
            $"Bearer resource_metadata=\"{ProtectedResourceMetadataAddress.AddressFor(McpResource)}\"",
            request.Response.Headers.WWWAuthenticate);
    }

    /// <summary>
    /// A token naming a configured authorization server is judged by that server's validator rather than by the scheme
    /// answering the challenge, which is what keeps the routing above from swallowing the credentials the endpoint does
    /// accept. The validator is asked for by name and reached by name, so both halves are read: a scheme the routing
    /// names and the registration never added is the shape this whole defect took.
    /// </summary>
    [Fact]
    public async Task AddMcpTransportSecurity_AnOAuthOnlyEndpoint_RoutesAConfiguredIssuersTokenToThatIssuersValidator()
    {
        // Arrange
        using var composed = ComposeOAuthOnlyEndpoint();
        var routing = composed
            .GetRequiredService<IOptionsMonitor<PolicySchemeOptions>>()
            .Get(TransportSurface.Mcp.RoutingSchemeName);

        var request = RequestTo(composed, TokenIssuedBy("https://sso.example.test"));

        // Act
        var selectedScheme = routing.ForwardDefaultSelector?.Invoke(request);

        // Assert
        Assert.Equal(TransportSurface.Mcp.OAuthSchemeNameFor("workforce"), selectedScheme);
        Assert.NotNull(await composed
            .GetRequiredService<IAuthenticationSchemeProvider>()
            .GetSchemeAsync(selectedScheme!));
    }

    /// <summary>
    /// Two surfaces publish this deployment's RFC 9728 document — the MCP endpoint through the protocol SDK's type, the
    /// administrative endpoint through a record of this repository's own — and a client reads whichever one it reached.
    /// Composing the scope list twice is what would let them disagree, so the two are asserted against each other over
    /// settings where the answer is not simply the required list: a scope advertised without being checked has to reach
    /// both documents or the same deployment tells two clients to ask for different things.
    /// </summary>
    [Fact]
    public void AddMcpTransportSecurity_AnEndpointAdvertisingAScope_PublishesWhatTheAdministrativeDocumentPublishes()
    {
        // Arrange
        var oauthSettings = OAuthEntryFor("workforce", "https://sso.example.test");
        oauthSettings.RequiredScopes.Add("mailfathom.read");
        oauthSettings.AdvertisedScopes.Add("offline_access");

        var endpointSettings = new McpEndpointOptions { Enabled = true };
        endpointSettings.Authentication.Add(new TransportAuthenticationOptions { OAuth = oauthSettings });

        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddMcpTransportSecurity(endpointSettings);

        // Assert
        using var composed = services.BuildServiceProvider();
        var published = composed
            .GetRequiredService<IOptionsMonitor<McpAuthenticationOptions>>()
            .Get(McpAuthenticationDefaults.AuthenticationScheme)
            .ResourceMetadata;

        Assert.NotNull(published);
        Assert.Equal(["mailfathom.read", "offline_access"], published.ScopesSupported);
        Assert.Equal(
            ProtectedResourceMetadataDocument.For([oauthSettings]).ScopesSupported,
            published.ScopesSupported);
    }

    private static ServiceProvider ComposeOAuthOnlyEndpoint()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var endpointSettings = new McpEndpointOptions { Enabled = true };

        endpointSettings.Authentication.Add(new TransportAuthenticationOptions
        {
            OAuth = OAuthEntryFor("workforce", "https://sso.example.test"),
        });

        services.AddMcpTransportSecurity(endpointSettings);

        return services.BuildServiceProvider();
    }

    private static OAuthValidationOptions OAuthEntryFor(string name, string issuer)
    {
        var oauthSettings = new OAuthValidationOptions { Resource = McpResource };

        oauthSettings.AuthorizationServers.Add(new AuthorizationServerOptions { Name = name, Issuer = issuer });

        return oauthSettings;
    }

    private static DefaultHttpContext RequestTo(IServiceProvider composed, string? authorizationHeaderValue)
    {
        var request = new DefaultHttpContext { RequestServices = composed };
        request.Request.Scheme = "https";
        request.Request.Host = new HostString("mail.example.test");

        if (authorizationHeaderValue is not null)
        {
            request.Request.Headers.Authorization = authorizationHeaderValue;
        }

        return request;
    }

    /// <summary>Composes a token naming an issuer, which is all the routing reads of one before a validator is chosen.</summary>
    private static string TokenIssuedBy(string issuer)
    {
        var payload = Encode($$"""{"iss":"{{issuer}}","sub":"9f2c"}""");

        return $"Bearer {Encode("""{"alg":"RS256","typ":"JWT"}""")}.{payload}.signature";
    }

    private static string Encode(string document) =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(document));

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
