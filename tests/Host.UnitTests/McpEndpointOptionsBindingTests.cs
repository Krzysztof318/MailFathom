// Copyright © 2026 Krzysztof Kasprowicz

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
        Assert.Equal(McpTransportAuthenticationMode.ApiKey, options.Authentication);
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
    public void ReadFrom_AnUnconfiguredDeployment_LeavesTheEndpointOffAndNamesNoMode()
    {
        // Arrange
        var configuration = ConfigurationFrom([]);

        // Act
        var options = McpEndpointOptions.ReadFrom(configuration);

        // Assert
        Assert.False(options.Enabled);
        Assert.Null(options.Authentication);
        Assert.Empty(options.ApiKeys);
        Assert.True(options.Cors.ServesEveryBrowserOrigin);
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

    /// <summary>A mode the binder cannot read is a startup failure, never a silent fall back to the unauthenticated posture.</summary>
    [Fact]
    public void ReadFrom_AnAuthenticationModeThatIsNotOneOfTheTwo_Fails()
    {
        // Arrange
        var configuration = ConfigurationFrom(new Dictionary<string, string?>
        {
            ["McpEndpoint:Enabled"] = "true",
            ["McpEndpoint:Authentication"] = "Nonee",
        });

        // Act, Assert
        Assert.ThrowsAny<InvalidOperationException>(() => McpEndpointOptions.ReadFrom(configuration));
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
