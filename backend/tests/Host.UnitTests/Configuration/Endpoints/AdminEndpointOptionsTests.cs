// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Host.Configuration.Access;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Security.Transport;
using MailFathom.Host.UnitTests.TestDoubles;
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
        Assert.True(settings.Cors.ServesEveryBrowserOrigin);
        Assert.Empty(settings.FindConfigurationErrors());
    }

    /// <summary>
    /// A deployment that wrote no list gets every browser origin, because a surface is protected by the credential a
    /// caller presents rather than by which page called it, and because a first run that failed a preflight would look
    /// like a broken deployment.
    /// </summary>
    [Fact]
    public void ReadFrom_NoOriginListAtAll_ServesEveryBrowserOrigin()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["AdminEndpoint:Enabled"] = "true",
        });

        // Act
        var settings = AdminEndpointOptions.ReadFrom(configuration);

        // Assert
        Assert.True(settings.Cors.ServesEveryBrowserOrigin);
        Assert.Empty(settings.FindConfigurationErrors());
    }

    /// <summary>An operator who narrowed the surface to the origin they serve must get exactly that.</summary>
    [Fact]
    public void ReadFrom_AConfiguredOriginList_ServesExactlyThoseOrigins()
    {
        // Arrange
        var configuration = ConfigurationFromJson("""
            {
              "AdminEndpoint": {
                "Enabled": true,
                "Cors": { "AllowedOrigins": [ "https://ops.example.test" ] }
              }
            }
            """);

        // Act
        var settings = AdminEndpointOptions.ReadFrom(configuration);

        // Assert
        Assert.False(settings.Cors.ServesEveryBrowserOrigin);
        Assert.Equal(["https://ops.example.test"], settings.Cors.AllowedOrigins);
        Assert.Empty(settings.FindConfigurationErrors());
    }

    /// <summary>An emptied list is the deployment that serves no browser at all, and it must not read as the absent one that serves every page on the internet.</summary>
    [Fact]
    public void ReadFrom_AnEmptiedOriginList_ServesNoBrowserOrigin()
    {
        // Arrange
        var configuration = ConfigurationFromJson("""
            {
              "AdminEndpoint": { "Enabled": true, "Cors": { "AllowedOrigins": [] } }
            }
            """);

        // Act
        var settings = AdminEndpointOptions.ReadFrom(configuration);

        // Assert
        Assert.False(settings.Cors.ServesEveryBrowserOrigin);
        Assert.Empty(settings.Cors.AllowedOrigins);
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
    public void ReadFrom_TheAuthenticationList_BindsEachMethodTheEndpointAccepts()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["AdminEndpoint:Enabled"] = "true",
            ["AdminEndpoint:Authentication:0:Method"] = "password",
            ["AdminEndpoint:Authentication:1:Method"] = "api-key",
            ["AdminEndpoint:Authentication:2:Method"] = "oauth-subject",
            ["AdminEndpoint:Authentication:2:OAuth:Resource"] = "https://mail.example.test:8090/api/admin",
            ["AdminEndpoint:Authentication:2:OAuth:AuthorizationServers:0:Name"] = "workforce",
            ["AdminEndpoint:Authentication:2:OAuth:AuthorizationServers:0:Issuer"] = "https://sso.example.test/realms/mailfathom",
        });

        // Act
        var settings = AdminEndpointOptions.ReadFrom(configuration);

        // Assert
        Assert.True(settings.AllowsBasic);
        Assert.True(settings.AllowsApiKey);
        Assert.True(settings.AllowsOAuth);
        Assert.True(settings.RequiresAuthentication);
        Assert.Empty(settings.FindConfigurationErrors());
    }

    /// <summary>
    /// A deployment still carrying the withdrawn section is stopped rather than started with the section ignored, because
    /// an endpoint whose only authentication was that section would otherwise start serving every caller as the default
    /// administrator. The refusal names what replaced it.
    /// </summary>
    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public void FindConfigurationErrors_TheWithdrawnAdministratorsSection_IsRefusedNamingWhatReplacedIt(string enabled)
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["AdminEndpoint:Enabled"] = enabled,
            ["AdminEndpoint:Administrators:0:Name"] = "alice",
            ["AdminEndpoint:Administrators:0:Credentials:0:ApiKey:Name"] = "alice-workstation",
            ["AdminEndpoint:Administrators:0:Credentials:0:ApiKey:SecretReference"] = "systemd-credential:admin-key",
        });

        // Act
        var reported = Assert.Single(AdminEndpointOptions.ReadFrom(configuration).FindConfigurationErrors());

        // Assert
        Assert.StartsWith("AdminEndpoint:Administrators is no longer read", reported, StringComparison.Ordinal);
        Assert.Contains("AdminEndpoint:Authentication", reported, StringComparison.Ordinal);
        Assert.Contains("mfctl credential create --surface admin", reported, StringComparison.Ordinal);
        Assert.Contains("MAILFATHOM_ADMIN_PASSWORD", reported, StringComparison.Ordinal);
    }

    /// <summary>A value where the list stood is the same withdrawn section, so it is refused the same way rather than reaching the binder.</summary>
    [Fact]
    public void FindConfigurationErrors_TheWithdrawnSectionWrittenAsAValue_IsRefusedTheSameWay()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["AdminEndpoint:Enabled"] = "true",
            ["AdminEndpoint:Administrators"] = "alice",
        });

        // Act
        var reported = Assert.Single(AdminEndpointOptions.ReadFrom(configuration).FindConfigurationErrors());

        // Assert
        Assert.StartsWith("AdminEndpoint:Administrators is no longer read", reported, StringComparison.Ordinal);
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
            settings.FindConfigurationErrors(),
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
            TransportSurface.Admin.RequestTimeoutPolicyName,
            TransportSurface.Admin.OAuthSchemeNameFor("workforce"),
        ];

        string[] mcpNames =
        [
            TransportSurface.Mcp.RoutingSchemeName,
            TransportSurface.Mcp.ApiKeySchemeName,
            TransportSurface.Mcp.AccessPolicyName,
            TransportSurface.Mcp.RateLimitingPolicyName,
            TransportSurface.Mcp.RequestTimeoutPolicyName,
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
            settings.FindConfigurationErrors(),
            error => error.Contains($"must be '{AdminEndpointOptions.RoutePrefix}'", StringComparison.Ordinal));
    }

    /// <summary>This endpoint's own rule composes its path like every shared one, so an operator is sent to the key they wrote rather than to the position the binder appended the entry at.</summary>
    [Fact]
    public void FindConfigurationErrors_AResourcePrefixRefusalOnAGappedSource_NamesTheKeyTheOperatorWrote()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["AdminEndpoint:Enabled"] = "true",
                ["AdminEndpoint:Authentication:0:Method"] = "api-key",
                ["AdminEndpoint:Authentication:5:Method"] = "oauth-subject",
                ["AdminEndpoint:Authentication:5:OAuth:Resource"] = "https://mail.example.test:8090/admin",
                ["AdminEndpoint:Authentication:5:OAuth:AuthorizationServers:0:Name"] = "workforce",
                ["AdminEndpoint:Authentication:5:OAuth:AuthorizationServers:0:Issuer"] = "https://sso.example.test/realms/mailfathom",
            })
            .Build();

        // Act
        var errors = AdminEndpointOptions.ReadFrom(configuration).FindConfigurationErrors();

        // Assert
        var reported = Assert.Single(errors);
        Assert.Contains("AdminEndpoint:Authentication:5:OAuth:Resource", reported, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://mail.example.test:8090/api/admin")]
    [InlineData("https://mail.example.test:8090/api/admin/")]
    public void FindConfigurationErrors_AResourceNamingTheRoutePrefix_IsAccepted(string resource) =>
        Assert.Empty(OAuthEndpoint(resource).FindConfigurationErrors());

    /// <summary>A resource that is not an identifier at all is reported as that, rather than as one naming the wrong path.</summary>
    [Fact]
    public void FindConfigurationErrors_AResourceThatIsNotAnIdentifier_ReportsOnlyThat()
    {
        // Arrange
        var settings = OAuthEndpoint("not-a-url");

        // Act
        var errors = settings.FindConfigurationErrors();

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
        Assert.Equal([8080, 8543], settings.ListenerPorts.Order());
    }

    [Fact]
    public void ListenerPorts_ATransportServingTlsAlone_ClaimsTheProfilePortsAlone() =>
        Assert.Equal(
            [8543],
            TlsTerminatingEndpoint(EndpointTransport.HttpsOnly).ListenerPorts.Order());

    /// <summary>The clear-text port is what the endpoint binds when it terminates no TLS, and there is nothing to redirect from.</summary>
    [Fact]
    public void ListenerPorts_AnEndpointTerminatingNoTls_ClaimsItsClearTextPortAlone() =>
        Assert.Equal([8080], EnabledEndpoint().ListenerPorts.Order());




    [Fact]
    public void ReadFrom_AConfiguredRedirect_BindsItAndRecordsThatItWasStated()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["AdminEndpoint:Enabled"] = "true",
            ["AdminEndpoint:Https:Redirect:Enabled"] = "true",
        });

        // Act
        var settings = AdminEndpointOptions.ReadFrom(configuration);

        // Assert
        Assert.True(settings.Https.Redirect.WasStated);
        Assert.True(settings.Https.Redirect.Enabled);
    }

    /// <summary>Stating a redirect for an endpoint that terminates no TLS is refused, because nothing would bind it and the endpoint is already served in clear text.</summary>
    [Fact]
    public void FindConfigurationErrors_ARedirectStatedUnderATransportThatCannotServeOne_IsRefused()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["AdminEndpoint:Enabled"] = "true",
            ["AdminEndpoint:Https:Redirect:Enabled"] = "true",
        });

        // Act, Assert
        Assert.Contains(
            AdminEndpointOptions.ReadFrom(configuration).FindConfigurationErrors(),
            error => error.Contains("a clear-text redirect is configured", StringComparison.Ordinal));
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
            ["AdminEndpoint:RateLimiting:MaxConcurrentRequestsPerUser"] = "2",
            ["AdminEndpoint:RateLimiting:TokenCapacity"] = "30",
            ["AdminEndpoint:RateLimiting:TokensPerReplenishmentPeriod"] = "30",
            ["AdminEndpoint:RateLimiting:ReplenishmentPeriod"] = "00:00:30",
        });

        // Act
        var settings = AdminEndpointOptions.ReadFrom(configuration);

        // Assert
        Assert.Equal(4, settings.RateLimiting.MaxConcurrentRequests);
        Assert.Equal(2, settings.RateLimiting.MaxConcurrentRequestsPerUser);
        Assert.Equal(30, settings.RateLimiting.TokenCapacity);
        Assert.Equal(TimeSpan.FromSeconds(30), settings.RateLimiting.ReplenishmentPeriod);
        Assert.Empty(settings.FindConfigurationErrors());
    }

    /// <summary>
    /// The administrative endpoint carries the same ceiling as the MCP one and configures it separately, which is the
    /// point worth asserting: it is the surface that reaches no AI provider, so it is the one an operator narrows
    /// without having to ask what a tool call needs.
    /// </summary>
    [Fact]
    public void ReadFrom_TheRequestTimeoutSection_BindsIndependentlyOfTheMcpEndpoint()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["AdminEndpoint:Enabled"] = "true",
            ["AdminEndpoint:RequestTimeout:Duration"] = "00:00:30",
        });

        // Act
        var settings = AdminEndpointOptions.ReadFrom(configuration);

        // Assert
        Assert.True(settings.RequestTimeout.Enabled);
        Assert.Equal(TimeSpan.FromSeconds(30), settings.RequestTimeout.Duration);
        Assert.Empty(settings.FindConfigurationErrors());
    }

    [Fact]
    public void RequestTimeout_WithNothingConfigured_BoundsTheEndpointOnTheProductDefault()
    {
        // Act
        var settings = AdminEndpointOptions.ReadFrom(new ConfigurationBuilder().Build());

        // Assert
        Assert.True(settings.RequestTimeout.Enabled);
        Assert.Equal(new TransportRequestTimeoutOptions().Duration, settings.RequestTimeout.Duration);
    }

    [Fact]
    public void FindConfigurationErrors_AnUnusableRequestCeiling_IsRefusedUnderThisEndpointsSection()
    {
        // Arrange
        var settings = EnabledEndpoint();
        settings.RequestTimeout.Duration = TimeSpan.Zero;

        // Act
        var errors = settings.FindConfigurationErrors();

        // Assert
        Assert.Contains(
            errors,
            error => error.StartsWith("AdminEndpoint:RequestTimeout:Duration", StringComparison.Ordinal));
    }

    /// <summary>The same rules, reported under this section's own path so an operator knows which endpoint to fix.</summary>
    [Fact]
    public void FindConfigurationErrors_AnUnusableRateLimit_IsRefusedUnderThisEndpointsSection()
    {
        // Arrange
        var settings = EnabledEndpoint();
        settings.RateLimiting.MaxConcurrentRequests = 0;

        // Act
        var errors = settings.FindConfigurationErrors();

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
        var errors = settings.FindConfigurationErrors();

        // Assert
        Assert.Empty(errors);
    }

    private static AdminEndpointOptions EnabledEndpoint() => new() { Enabled = true };

    private static AdminEndpointOptions TlsTerminatingEndpoint(
        EndpointTransport transport = EndpointTransport.HttpAndHttps)
    {
        var settings = EnabledEndpoint();
        settings.Transport = transport;
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
        AdminEndpointOptions settings = new() { Enabled = true };
        settings.Authentication.Add(ConfiguredAuthentication.AcceptingSubjectsFrom(resource));

        return settings;
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    /// <summary>Reads a JSON document, which is what tells an absent list from an emptied one; a dictionary provider can spell only the first.</summary>
    private static IConfiguration ConfigurationFromJson(string document)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(document));

        return new ConfigurationBuilder().AddJsonStream(stream).Build();
    }
}
