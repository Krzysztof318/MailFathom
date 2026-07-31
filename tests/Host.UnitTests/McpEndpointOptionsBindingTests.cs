// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.Text;
using MailMcp.Host.Configuration;
using MailMcp.Infrastructure.Secrets;
using MailMcp.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MailMcp.Host.UnitTests;

/// <summary>Covers that the endpoint section binds from configuration the way composition reads it.</summary>
/// <remarks>
/// <para>
/// Every other test in this project builds the options object directly, which proves what the rules do and nothing about
/// whether an operator's configuration ever reaches them. Several of the settings here bind into collections exposed
/// through getter-only properties, and a key list that silently stayed empty would leave every rule passing while no
/// client could authenticate. That gap is the reason this file binds real configuration instead.
/// </para>
/// <para>
/// The section is bound strictly, exactly as composition binds it, so the tests also state what a misspelling does. The
/// origin list is read from a JSON document rather than from an in-memory dictionary wherever the difference between an
/// absent list and an empty one is what is under test, because that difference is a property of the JSON provider and a
/// dictionary would only restate what this file assumed about it.
/// </para>
/// </remarks>
public sealed class McpEndpointOptionsBindingTests
{
    [Fact]
    public void ReadFrom_AConfiguredSection_ReadsEveryDecisionCompositionActsOn()
    {
        // Arrange
        var configuration = ConfigurationFrom(new Dictionary<string, string?>
        {
            ["McpEndpoint:Enabled"] = "true",
            ["McpEndpoint:Authentication"] = "ApiKey",
            ["McpEndpoint:ApiKeys:0:Name"] = "workstation",
            ["McpEndpoint:ApiKeys:0:SecretReference"] = "systemd-credential:mailmcp-mcp-workstation-key",
            ["McpEndpoint:ApiKeys:1:Name"] = "chatgpt-connector",
            ["McpEndpoint:ApiKeys:1:SecretReference"] = "file:/run/secrets/mailmcp-mcp-chatgpt-key",
            ["McpEndpoint:ApiKeys:1:Lifetime"] = "2027-01-31T00:00:00Z",
            ["McpEndpoint:Cors:AllowedOrigins:0"] = "https://client.example.test",
            ["McpEndpoint:Cors:AllowedOrigins:1"] = "https://console.example.test:8443",
            ["McpEndpoint:RateLimiting:MaxConcurrentRequests"] = "12",
            ["McpEndpoint:RateLimiting:TokenCapacity"] = "40",
            ["McpEndpoint:RateLimiting:TokensPerReplenishmentPeriod"] = "10",
            ["McpEndpoint:RateLimiting:ReplenishmentPeriod"] = "00:00:30",
        });

        // Act
        var options = McpEndpointOptions.ReadFrom(configuration);

        // Assert
        Assert.True(options.Enabled);
        Assert.Equal(McpTransportAuthenticationMethods.ApiKey, options.Authentication);
        Assert.Equal(["workstation", "chatgpt-connector"], options.ApiKeys.Select(key => key.Name));
        Assert.Equal(
            [SecretLifetime.NoLimitValue, "2027-01-31T00:00:00Z"],
            options.ApiKeys.Select(key => key.Lifetime));
        Assert.Equal(
            ["https://client.example.test", "https://console.example.test:8443"],
            options.Cors.AllowedOrigins);
        Assert.Equal(12, options.RateLimiting.MaxConcurrentRequests);
        Assert.Equal(40, options.RateLimiting.TokenCapacity);
        Assert.Equal(10, options.RateLimiting.TokensPerReplenishmentPeriod);
        Assert.Equal(TimeSpan.FromSeconds(30), options.RateLimiting.ReplenishmentPeriod);
        Assert.Empty(options.FindConfigurationErrors());
    }

    /// <summary>
    /// The limits are the one part of this section with product defaults, so an operator narrowing a single value must
    /// not silently reset the rest to zero the way a partially bound section otherwise would.
    /// </summary>
    [Fact]
    public void ReadFrom_OneConfiguredLimit_LeavesTheRemainingLimitsAtTheirDefaults()
    {
        // Arrange
        var configuration = ConfigurationFrom(new Dictionary<string, string?>
        {
            ["McpEndpoint:Enabled"] = "true",
            ["McpEndpoint:Authentication"] = "None",
            ["McpEndpoint:RateLimiting:MaxConcurrentRequests"] = "5",
        });

        // Act
        var options = McpEndpointOptions.ReadFrom(configuration);

        // Assert
        var defaults = new McpRateLimitingOptions();
        Assert.Equal(5, options.RateLimiting.MaxConcurrentRequests);
        Assert.Equal(defaults.TokenCapacity, options.RateLimiting.TokenCapacity);
        Assert.Equal(defaults.TokensPerReplenishmentPeriod, options.RateLimiting.TokensPerReplenishmentPeriod);
        Assert.Equal(defaults.ReplenishmentPeriod, options.RateLimiting.ReplenishmentPeriod);
        Assert.Empty(options.FindConfigurationErrors());
    }

    /// <summary>A profile binds through three collections, any of which silently staying empty would leave a client certificate judged by less than was configured.</summary>
    [Fact]
    public void ReadFrom_ConfiguredClientCertificateProfiles_ReadsEachProfileWhole()
    {
        // Arrange
        var configuration = ConfigurationFrom(new Dictionary<string, string?>
        {
            ["McpEndpoint:Enabled"] = "true",
            ["McpEndpoint:Authentication"] = "None",
            ["McpEndpoint:ClientCertificateProfiles:0:Name"] = "chatgpt-connector",
            ["McpEndpoint:ClientCertificateProfiles:0:Requirement"] = "Optional",
            ["McpEndpoint:ClientCertificateProfiles:0:TrustAnchors:0:Name"] = "openai-connectors-ca",
            ["McpEndpoint:ClientCertificateProfiles:0:TrustAnchors:0:SecretReference"] = "file:/etc/mailmcp/openai-connectors-ca.pem",
            ["McpEndpoint:ClientCertificateProfiles:0:TrustAnchors:1:Name"] = "openai-connectors-ca-next",
            ["McpEndpoint:ClientCertificateProfiles:0:TrustAnchors:1:SecretReference"] = "file:/etc/mailmcp/openai-connectors-ca-next.pem",
            ["McpEndpoint:ClientCertificateProfiles:0:SubjectAlternativeNames:0"] = "mtls.prod.connectors.openai.com",
        });

        // Act
        var options = McpEndpointOptions.ReadFrom(configuration);

        // Assert
        var profile = Assert.Single(options.ClientCertificateProfiles);
        Assert.Equal("chatgpt-connector", profile.Name);
        Assert.Equal(McpClientCertificateRequirement.Optional, profile.Requirement);
        Assert.Equal(
            ["openai-connectors-ca", "openai-connectors-ca-next"],
            profile.TrustAnchors.Select(anchor => anchor.Name));
        Assert.Equal(["mtls.prod.connectors.openai.com"], profile.SubjectAlternativeNames);
        Assert.Empty(options.FindConfigurationErrors());
    }

    [Fact]
    public void ReadFrom_AnUnconfiguredDeployment_LeavesTheEndpointOffAndRequiresNothing()
    {
        // Arrange
        var configuration = ConfigurationFrom([]);

        // Act
        var options = McpEndpointOptions.ReadFrom(configuration);

        // Assert
        Assert.False(options.Enabled);
        Assert.Equal(McpTransportAuthenticationMethods.None, options.Authentication);
        Assert.Empty(options.ApiKeys);
        Assert.True(options.Cors.ServesEveryBrowserOrigin);
    }

    /// <summary>
    /// The reason the set is one scalar value rather than a collection. A single value either binds or fails startup,
    /// whereas a collection lets a binder drop an element it could not read and leave a shorter list behind — which for
    /// this setting would mean quietly turning a method off.
    /// </summary>
    [Fact]
    public void ReadFrom_BothMethodsWrittenAsOneValue_ReadsThemAsASet()
    {
        // Arrange
        var configuration = ConfigurationFrom(new Dictionary<string, string?>
        {
            ["McpEndpoint:Enabled"] = "true",
            ["McpEndpoint:Authentication"] = "ApiKey, OAuth",
            ["McpEndpoint:ApiKeys:0:Name"] = "nightly-digest",
            ["McpEndpoint:ApiKeys:0:SecretReference"] = "systemd-credential:mailmcp-mcp-digest-key",
            ["McpEndpoint:OAuth:Resource"] = "https://mail.example.test/mcp",
            ["McpEndpoint:OAuth:RequiredScopes:0"] = "mailmcp.read",
            ["McpEndpoint:OAuth:AuthorizationServers:0:Name"] = "workforce",
            ["McpEndpoint:OAuth:AuthorizationServers:0:Issuer"] = "https://sso.example.test/realms/mailmcp",
            ["McpEndpoint:OAuth:AuthorizationServers:0:AuthorizedSubjects:0"] = "9f2c",
        });

        // Act
        var options = McpEndpointOptions.ReadFrom(configuration);

        // Assert
        Assert.True(options.AllowsApiKey);
        Assert.True(options.AllowsOAuth);
        Assert.Equal("https://mail.example.test/mcp", options.OAuth.Resource);
        Assert.Equal(["mailmcp.read"], options.OAuth.RequiredScopes);
        Assert.Equal(["workforce"], options.OAuth.AuthorizationServers.Select(server => server.Name));
        Assert.Equal(["9f2c"], options.OAuth.AuthorizationServers.Single().AuthorizedSubjects);
        Assert.Empty(options.FindConfigurationErrors());
    }

    /// <summary>A configured list replaces the default rather than being added to it, which a pre-populated collection could not achieve.</summary>
    [Fact]
    public void ReadFrom_AConfiguredOriginList_CarriesThoseOriginsAndNotTheDefault()
    {
        // Arrange
        var configuration = ConfigurationFromJson("""
            { "McpEndpoint": { "Cors": { "AllowedOrigins": ["https://client.example.test"] } } }
            """);

        // Act
        var options = McpEndpointOptions.ReadFrom(configuration);

        // Assert
        Assert.Equal(["https://client.example.test"], options.Cors.AllowedOrigins);
        Assert.False(options.Cors.ServesEveryBrowserOrigin);
    }

    /// <summary>An empty list states the posture that serves no browser, which an absent list must not be read as.</summary>
    [Fact]
    public void ReadFrom_AnEmptyOriginList_ServesNoBrowserRatherThanEveryOne()
    {
        // Arrange
        var configuration = ConfigurationFromJson("""
            { "McpEndpoint": { "Cors": { "AllowedOrigins": [] } } }
            """);

        // Act
        var options = McpEndpointOptions.ReadFrom(configuration);

        // Assert
        Assert.Empty(options.Cors.AllowedOrigins);
        Assert.False(options.Cors.ServesEveryBrowserOrigin);
        Assert.Empty(options.FindConfigurationErrors());
    }

    /// <summary>A misspelling that bound quietly would leave a security decision reading as one nobody made.</summary>
    [Theory]
    [InlineData("McpEndpoint:Enabeld", "true")]
    [InlineData("McpEndpoint:Authentication ", "None")]
    [InlineData("McpEndpoint:ApiKey", "workstation")]
    [InlineData("McpEndpoint:Cors:AllowedOrigin", "https://client.example.test")]
    [InlineData("McpEndpoint:RateLimiting:Enabeld", "false")]
    [InlineData("McpEndpoint:RateLimiting:MaxConcurrentRequest", "5")]
    [InlineData("McpEndpoint:RateLimit:MaxConcurrentRequests", "5")]
    public void ReadFrom_AnUnrecognizedKey_FailsRatherThanBeingIgnored(string key, string value)
    {
        // Arrange
        var configuration = ConfigurationFrom(new Dictionary<string, string?>
        {
            ["McpEndpoint:Enabled"] = "true",
            [key] = value,
        });

        // Act, Assert
        Assert.ThrowsAny<InvalidOperationException>(() => McpEndpointOptions.ReadFrom(configuration));
    }

    /// <summary>The removed allow-any switch is refused rather than ignored, so a deployment carrying it is corrected instead of quietly widened.</summary>
    [Fact]
    public void ReadFrom_TheRemovedAllowAnyOriginKey_FailsRatherThanBeingIgnored()
    {
        // Arrange
        var configuration = ConfigurationFrom(new Dictionary<string, string?>
        {
            ["McpEndpoint:Enabled"] = "true",
            ["McpEndpoint:Cors:AllowAnyOrigin"] = "false",
        });

        // Act, Assert
        Assert.ThrowsAny<InvalidOperationException>(() => McpEndpointOptions.ReadFrom(configuration));
    }

    /// <summary>A method name the binder cannot read is a startup failure, never a silent fall back to the unauthenticated posture.</summary>
    [Theory]
    [InlineData("Nonee")]
    [InlineData("ApiKey, OAuht")]
    public void ReadFrom_AnAuthenticationMethodNoMemberNames_Fails(string authentication)
    {
        // Arrange
        var configuration = ConfigurationFrom(new Dictionary<string, string?>
        {
            ["McpEndpoint:Enabled"] = "true",
            ["McpEndpoint:Authentication"] = authentication,
        });

        // Act, Assert
        Assert.ThrowsAny<InvalidOperationException>(() => McpEndpointOptions.ReadFrom(configuration));
    }

    /// <summary>
    /// The HTTPS profiles bind through three shapes that each fail quietly when they are wrong: a getter-only list of
    /// objects, a list of enum members, and the nested secret blocks the certificate material is named through. A
    /// profile that bound empty would leave the endpoint on the clear-text posture while an operator read a configured
    /// certificate.
    /// </summary>
    [Fact]
    public void ReadFrom_ConfiguredHttpsProfiles_ReadsTheDomainsProtocolsAndCertificateMaterial()
    {
        // Arrange
        var configuration = ConfigurationFrom(new Dictionary<string, string?>
        {
            ["McpEndpoint:Enabled"] = "true",
            ["McpEndpoint:Authentication"] = "None",
            ["McpEndpoint:Https:Endpoints:0:Name"] = "public",
            ["McpEndpoint:Https:Endpoints:0:Domain"] = "mail.example.test",
            ["McpEndpoint:Https:Endpoints:0:Port"] = "443",
            ["McpEndpoint:Https:Endpoints:0:MinimumTlsVersion"] = "Tls13",
            ["McpEndpoint:Https:Endpoints:0:HttpProtocols:0"] = "Http1",
            ["McpEndpoint:Https:Endpoints:0:HttpProtocols:1"] = "Http2",
            ["McpEndpoint:Https:Endpoints:0:ServerCertificate:CertificateChain:Name"] = "public-chain",
            ["McpEndpoint:Https:Endpoints:0:ServerCertificate:CertificateChain:SecretReference"] = "file:/etc/mailmcp/tls/fullchain.pem",
            ["McpEndpoint:Https:Endpoints:0:ServerCertificate:PrivateKey:Name"] = "public-key",
            ["McpEndpoint:Https:Endpoints:0:ServerCertificate:PrivateKey:SecretReference"] = "file:/etc/mailmcp/tls/privkey.pem",
            ["McpEndpoint:Https:Endpoints:1:Name"] = "connector",
            ["McpEndpoint:Https:Endpoints:1:Domain"] = "connector.example.test",
            ["McpEndpoint:Https:Endpoints:1:Port"] = "443",
            ["McpEndpoint:Https:Endpoints:1:ServerCertificate:Bundle:Name"] = "connector-bundle",
            ["McpEndpoint:Https:Endpoints:1:ServerCertificate:Bundle:SecretReference"] = "file:/etc/mailmcp/tls/connector.pfx",
            ["McpEndpoint:Https:Endpoints:1:ServerCertificate:Bundle:Password:Name"] = "connector-bundle-password",
            ["McpEndpoint:Https:Endpoints:1:ServerCertificate:Bundle:Password:SecretReference"] = "systemd-credential:mailmcp-tls-password",
        });

        // Act
        var options = McpEndpointOptions.ReadFrom(configuration);

        // Assert
        Assert.True(options.Https.TerminatesTls);
        Assert.Equal(["public", "connector"], options.Https.Endpoints.Select(endpoint => endpoint.Name));
        Assert.Equal(McpMinimumTlsVersion.Tls13, options.Https.Endpoints[0].MinimumTlsVersion);
        Assert.Equal(
            [McpHttpProtocol.Http1, McpHttpProtocol.Http2],
            options.Https.Endpoints[0].ServedHttpProtocols);
        Assert.Equal(
            "file:/etc/mailmcp/tls/privkey.pem",
            options.Https.Endpoints[0].ServerCertificate.PrivateKey?.SecretReference);
        Assert.Equal(
            "systemd-credential:mailmcp-tls-password",
            options.Https.Endpoints[1].ServerCertificate.Bundle?.Password?.SecretReference);
        Assert.Empty(options.FindConfigurationErrors());
    }

    /// <summary>Absent is the documented default of HTTP/1.1 and HTTP/2, and it has to stay distinguishable from a list an operator emptied.</summary>
    [Fact]
    public void ReadFrom_AProfileNamingNoHttpVersions_LeavesTheListUnsetRatherThanEmpty()
    {
        // Arrange
        var configuration = ConfigurationFrom(new Dictionary<string, string?>
        {
            ["McpEndpoint:Enabled"] = "true",
            ["McpEndpoint:Authentication"] = "None",
            ["McpEndpoint:Https:Endpoints:0:Name"] = "public",
            ["McpEndpoint:Https:Endpoints:0:Domain"] = "mail.example.test",
            ["McpEndpoint:Https:Endpoints:0:ServerCertificate:Bundle:Name"] = "bundle",
            ["McpEndpoint:Https:Endpoints:0:ServerCertificate:Bundle:SecretReference"] = "file:/etc/mailmcp/tls/bundle.pfx",
        });

        // Act
        var profile = Assert.Single(McpEndpointOptions.ReadFrom(configuration).Https.Endpoints);

        // Assert
        Assert.Null(profile.HttpProtocols);
        Assert.Equal([McpHttpProtocol.Http1, McpHttpProtocol.Http2], profile.ServedHttpProtocols);
    }

    private static IConfiguration ConfigurationFrom(Dictionary<string, string?> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

    private static IConfiguration ConfigurationFromJson(string document)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(document));

        return new ConfigurationBuilder()
            .AddJsonStream(stream)
            .Build();
    }
}
