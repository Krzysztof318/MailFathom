// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Access;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Security.Transport;
using MailFathom.Infrastructure.Certificates;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.Infrastructure.Security.Transport;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Endpoints;

/// <summary>Covers what a deployment gets for configuring the administrative endpoint, and what it is refused.</summary>
/// <remarks>
/// This section decides whether a network can administer the service, so the defaults matter as much as the validation:
/// what a deployment that writes nothing gets is the answer most deployments will live with.
/// </remarks>
public sealed class AdminEndpointOptionsTests
{
    [Fact]
    public void ReadFrom_ADeploymentThatConfiguresNothing_ServesNoAdministrativeSurface()
    {
        // Act
        var settings = AdminEndpointOptions.ReadFrom(new ConfigurationBuilder().Build());

        // Assert: off, and requiring no credential is then irrelevant because nothing is served.
        Assert.False(settings.Enabled);
        Assert.False(settings.RequiresAuthentication);
        Assert.Empty(settings.FindConfigurationErrors([]));
    }

    /// <summary>A misspelled key must fail rather than bind a default, or an operator reads their own configuration as though it took effect.</summary>
    [Fact]
    public void ReadFrom_AMisspelledKey_FailsRatherThanServingAPostureNobodySelected()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["AdminEndpoint:Enabeld"] = "true",
        });

        // Act, Assert
        Assert.ThrowsAny<InvalidOperationException>(() => AdminEndpointOptions.ReadFrom(configuration));
    }

    [Fact]
    public void ReadFrom_TheAuthenticationSet_BindsTheSameSpellingsTheMcpEndpointTakes()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["AdminEndpoint:Enabled"] = "true",
            ["AdminEndpoint:Authentication"] = "ApiKey, OAuth",
        });

        // Act
        var settings = AdminEndpointOptions.ReadFrom(configuration);

        // Assert
        Assert.True(settings.AllowsApiKey);
        Assert.True(settings.AllowsOAuth);
    }

    [Fact]
    public void FindConfigurationErrors_ApiKeyAuthenticationWithNoKey_IsRefused()
    {
        // Arrange
        var settings = EnabledEndpoint();
        settings.Authentication = TransportAuthenticationMethods.ApiKey;

        // Act, Assert
        Assert.Contains(
            settings.FindConfigurationErrors([]),
            error => error.Contains("no key is configured", StringComparison.Ordinal));
    }

    /// <summary>Settings nothing reads are a deployment believing it is protected, which is worse than one that knows it is not.</summary>
    [Fact]
    public void FindConfigurationErrors_KeysConfiguredWhileApiKeyAuthenticationIsOff_IsRefused()
    {
        // Arrange
        var settings = EnabledEndpoint();
        settings.ApiKeys.Add(new ConfiguredSecret { Name = "workstation", SecretReference = "systemd-credential:admin-key" });

        // Act, Assert
        Assert.Contains(
            settings.FindConfigurationErrors([]),
            error => error.Contains("none of them is checked", StringComparison.Ordinal));
    }

    [Fact]
    public void FindConfigurationErrors_AnAuthenticationValueNamingNoMethod_IsRefusedRatherThanOpeningTheEndpoint()
    {
        // Arrange: the binder accepts any number for an enum, and this one answers 'no' to every method check.
        var settings = EnabledEndpoint();
        settings.Authentication = (TransportAuthenticationMethods)4;

        // Act, Assert
        Assert.Contains(
            settings.FindConfigurationErrors([]),
            error => error.Contains("names no authentication method", StringComparison.Ordinal));
    }

    /// <summary>
    /// Two endpoints on one port would leave whichever bound first deciding which credentials guarded the other's
    /// routes, and the operating system's own failure names a socket rather than the section that asked for it.
    /// </summary>
    [Fact]
    public void FindConfigurationErrors_APortAnotherListenerBinds_IsRefusedBeforeAnythingBinds()
    {
        // Arrange
        var settings = EnabledEndpoint();
        settings.Port = 8080;

        // Act, Assert
        Assert.Contains(
            settings.FindConfigurationErrors([8080]),
            error => error.Contains("already bound by another listener", StringComparison.Ordinal));
    }

    [Fact]
    public void FindConfigurationErrors_ADisabledEndpointOnAClaimedPort_ReportsNothing()
    {
        // Arrange: nothing binds, so nothing collides.
        var settings = new AdminEndpointOptions { Port = 8080 };

        // Act, Assert
        Assert.Empty(settings.FindConfigurationErrors([8080]));
    }

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("")]
    public void FindConfigurationErrors_ABindAddressThatIsNotAnIpAddress_IsRefused(string bindAddress)
    {
        // Arrange
        var settings = EnabledEndpoint();
        settings.BindAddress = bindAddress;

        // Act, Assert
        Assert.Contains(
            settings.FindConfigurationErrors([]),
            error => error.Contains(nameof(AdminEndpointOptions.BindAddress), StringComparison.Ordinal));
    }

    /// <summary>The routes are the surface's own address, so the two sides of the wire have to agree on it exactly.</summary>
    [Fact]
    public void RoutePrefix_IsTheAddressTheCommandAppendsTo() =>
        Assert.Equal("/api/admin", AdminEndpointOptions.RoutePrefix);

    /// <summary>
    /// The two surfaces are kept apart by the names their schemes and policies carry. Sharing one would merge the two
    /// policies into whichever registration ran last, and the endpoint that lost would be guarded by settings its
    /// operator never wrote.
    /// </summary>
    [Fact]
    public void TransportSurface_TheAdministrativeSurface_SharesNoNameWithTheMcpOne()
    {
        // Act
        string[] adminNames =
        [
            TransportSurface.Admin.RoutingSchemeName,
            TransportSurface.Admin.ApiKeySchemeName,
            TransportSurface.Admin.AccessPolicyName,
            TransportSurface.Admin.RateLimitingPolicyName,
            TransportSurface.Admin.OAuthSchemeNameFor("workforce"),
        ];

        string[] mcpNames =
        [
            TransportSurface.Mcp.RoutingSchemeName,
            TransportSurface.Mcp.ApiKeySchemeName,
            TransportSurface.Mcp.AccessPolicyName,
            TransportSurface.Mcp.RateLimitingPolicyName,
            TransportSurface.Mcp.OAuthSchemeNameFor("workforce"),
        ];

        // Assert
        Assert.Empty(adminNames.Intersect(mcpNames, StringComparer.Ordinal));
    }

    /// <summary>
    /// The resource is a name rather than an address to fetch, so nothing about OAuth ties it to a route. Discovery does:
    /// <c>mfctl</c> finds the metadata document by appending the route prefix to the address it was handed, and that
    /// composition reaches the document's RFC 9728 location only when the resource names the same prefix.
    /// </summary>
    [Theory]
    [InlineData("https://mail.example.test:8090")]
    [InlineData("https://mail.example.test:8090/admin")]
    [InlineData("https://mail.example.test:8090/api/admin/session")]
    public void FindConfigurationErrors_AResourceThatDoesNotNameTheRoutePrefix_IsRefused(string resource)
    {
        // Arrange
        var settings = OAuthEndpoint(resource);

        // Act & Assert
        Assert.Contains(
            settings.FindConfigurationErrors([]),
            error => error.Contains($"must be '{AdminEndpointOptions.RoutePrefix}'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("https://mail.example.test:8090/api/admin")]
    [InlineData("https://mail.example.test:8090/api/admin/")]
    public void FindConfigurationErrors_AResourceNamingTheRoutePrefix_IsAccepted(string resource) =>
        Assert.Empty(OAuthEndpoint(resource).FindConfigurationErrors([]));

    /// <summary>A resource that is not an identifier at all is reported as that, rather than as one naming the wrong path.</summary>
    [Fact]
    public void FindConfigurationErrors_AResourceThatIsNotAnIdentifier_ReportsOnlyThat()
    {
        // Arrange
        var settings = OAuthEndpoint("not-a-url");

        // Act
        var errors = settings.FindConfigurationErrors([]);

        // Assert
        Assert.Contains(errors, error => error.Contains("not a canonical resource URL", StringComparison.Ordinal));
        Assert.DoesNotContain(errors, error => error.Contains("must be '/api/admin'", StringComparison.Ordinal));
    }

    /// <summary>
    /// The redirect's port is one this endpoint binds, so it is checked against every other listener in the process. Left
    /// out, a deployment could hand it to the probes or to the MCP surface and meet an address-in-use failure naming a
    /// socket rather than a section.
    /// </summary>
    [Fact]
    public void ListenerPorts_AnEndpointTerminatingTls_ClaimsTheRedirectPortBesideTheProfiles()
    {
        // Arrange
        var settings = TlsTerminatingEndpoint();

        // Act, Assert
        Assert.Equal([8091, 8543], settings.ListenerPorts.Order());
    }

    [Fact]
    public void ListenerPorts_ARedirectTurnedOff_ClaimsTheProfilePortsAlone()
    {
        // Arrange
        var settings = TlsTerminatingEndpoint();
        settings.Https.Redirect.Enabled = false;

        // Act, Assert
        Assert.Equal([8543], settings.ListenerPorts.Order());
    }

    /// <summary>The clear-text port is what the endpoint binds when it terminates no TLS, and there is nothing to redirect from.</summary>
    [Fact]
    public void ListenerPorts_AnEndpointTerminatingNoTls_ClaimsItsClearTextPortAlone() =>
        Assert.Equal([8090], EnabledEndpoint().ListenerPorts.Order());

    [Fact]
    public void FindConfigurationErrors_ARedirectPortAnotherListenerBinds_IsRefusedBeforeAnythingBinds()
    {
        // Arrange
        var settings = TlsTerminatingEndpoint();

        // Act, Assert
        Assert.Contains(
            settings.FindConfigurationErrors([8091]),
            error => error.Contains("already bound by another listener", StringComparison.Ordinal));
    }

    /// <summary>A port nothing binds collides with nothing, so a disabled redirect is not compared against the other listeners.</summary>
    [Fact]
    public void FindConfigurationErrors_ADisabledRedirectOnAClaimedPort_ReportsNothing()
    {
        // Arrange
        var settings = TlsTerminatingEndpoint();
        settings.Https.Redirect.Enabled = false;

        // Act, Assert
        Assert.Empty(settings.FindConfigurationErrors([8091]));
    }

    /// <summary>Each surface redirects to its own profiles, so the two defaults have to differ or enabling both would collide.</summary>
    [Fact]
    public void ClearTextRedirectPort_TheAdministrativeDefault_IsNotTheMcpEndpointsDefault() =>
        Assert.NotEqual(
            McpEndpointOptions.DefaultClearTextRedirectPort,
            AdminEndpointOptions.DefaultClearTextRedirectPort);

    [Fact]
    public void ReadFrom_AConfiguredRedirect_BindsItAndRecordsThatItWasStated()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["AdminEndpoint:Enabled"] = "true",
            ["AdminEndpoint:Https:Redirect:Port"] = "8092",
        });

        // Act
        var settings = AdminEndpointOptions.ReadFrom(configuration);

        // Assert
        Assert.True(settings.Https.Redirect.WasStated);
        Assert.Equal(8092, settings.ClearTextRedirectPort);
    }

    /// <summary>Stating a redirect for an endpoint that terminates no TLS is refused, because nothing would bind it and the endpoint is already served in clear text.</summary>
    [Fact]
    public void FindConfigurationErrors_ARedirectStatedForAnEndpointTerminatingNoTls_IsRefused()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["AdminEndpoint:Enabled"] = "true",
            ["AdminEndpoint:Https:Redirect:Port"] = "8092",
        });

        // Act, Assert
        Assert.Contains(
            AdminEndpointOptions.ReadFrom(configuration).FindConfigurationErrors([]),
            error => error.Contains("nothing to redirect to", StringComparison.Ordinal));
    }

    /// <summary>
    /// The section that must apply whether or not anyone wrote it: an administrative endpoint reachable from a network
    /// with no limit is unbounded key guessing, and the surface where a successful guess is worth the most.
    /// </summary>
    [Fact]
    public void RateLimiting_WithNothingConfigured_BoundsTheEndpointOnTheProductDefaults()
    {
        // Act
        var settings = AdminEndpointOptions.ReadFrom(new ConfigurationBuilder().Build());

        // Assert
        Assert.True(settings.RateLimiting.Enabled);
        Assert.Equal(TransportRateLimits.Default.MaxConcurrentRequests, settings.RateLimiting.MaxConcurrentRequests);
        Assert.Equal(TransportRateLimits.Default.TokenCapacity, settings.RateLimiting.TokenCapacity);
    }

    [Fact]
    public void ReadFrom_TheRateLimitingSection_BindsTheSameKeysTheMcpEndpointTakes()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["AdminEndpoint:Enabled"] = "true",
            ["AdminEndpoint:RateLimiting:MaxConcurrentRequests"] = "4",
            ["AdminEndpoint:RateLimiting:TokenCapacity"] = "30",
            ["AdminEndpoint:RateLimiting:TokensPerReplenishmentPeriod"] = "30",
            ["AdminEndpoint:RateLimiting:ReplenishmentPeriod"] = "00:00:30",
        });

        // Act
        var settings = AdminEndpointOptions.ReadFrom(configuration);

        // Assert
        Assert.Equal(4, settings.RateLimiting.MaxConcurrentRequests);
        Assert.Equal(30, settings.RateLimiting.TokenCapacity);
        Assert.Equal(TimeSpan.FromSeconds(30), settings.RateLimiting.ReplenishmentPeriod);
        Assert.Empty(settings.FindConfigurationErrors([]));
    }

    /// <summary>The same rules, reported under this section's own path so an operator knows which endpoint to fix.</summary>
    [Fact]
    public void FindConfigurationErrors_AnUnusableRateLimit_IsRefusedUnderThisEndpointsSection()
    {
        // Arrange
        var settings = EnabledEndpoint();
        settings.RateLimiting.MaxConcurrentRequests = 0;

        // Act
        var errors = settings.FindConfigurationErrors([]);

        // Assert
        Assert.Contains(
            errors,
            error => error.StartsWith("AdminEndpoint:RateLimiting:MaxConcurrentRequests", StringComparison.Ordinal));
    }

    [Fact]
    public void FindConfigurationErrors_ADisabledEndpointWithAnUnusableRateLimit_ReportsNothing()
    {
        // Arrange
        var settings = new AdminEndpointOptions();
        settings.RateLimiting.TokenCapacity = 0;

        // Act
        var errors = settings.FindConfigurationErrors([]);

        // Assert
        Assert.Empty(errors);
    }

    private static AdminEndpointOptions EnabledEndpoint() => new() { Enabled = true };

    private static AdminEndpointOptions TlsTerminatingEndpoint()
    {
        var settings = EnabledEndpoint();
        settings.Https.Endpoints.Add(new TransportHttpsEndpointOptions
        {
            Name = "admin",
            Domain = "admin.example.test",
            Port = 8543,
            ServerCertificate = new TlsServerCertificateOptions
            {
                Bundle = new ConfiguredSecret { Name = "bundle", SecretReference = "file:/etc/mailfathom/tls/admin.pfx" },
            },
        });

        return settings;
    }

    private static AdminEndpointOptions OAuthEndpoint(string resource)
    {
        AdminEndpointOptions settings = new()
        {
            Enabled = true,
            Authentication = TransportAuthenticationMethods.OAuth,
            OAuth = new OAuthValidationOptions { Resource = resource },
        };

        settings.OAuth.AuthorizationServers.Add(new AuthorizationServerOptions
        {
            Name = "workforce",
            Issuer = "https://sso.example.test/realms/mailfathom",
        });

        settings.OAuth.AuthorizationServers[0].AuthorizedSubjects.Add("11111111-2222-3333-4444-555555555555");

        return settings;
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
