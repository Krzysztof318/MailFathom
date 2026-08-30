// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Domain.Access;
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
    public void ReadFrom_TheAuthenticationList_BindsTheSameEntriesTheMcpEndpointTakes()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["AdminEndpoint:Enabled"] = "true",
            ["AdminEndpoint:Authentication:0:ApiKey:Name"] = "workstation",
            ["AdminEndpoint:Authentication:0:ApiKey:SecretReference"] = "systemd-credential:admin-key",
            ["AdminEndpoint:Authentication:1:OAuth:Resource"] = "https://mail.example.test:8090/api/admin",
            ["AdminEndpoint:Authentication:1:OAuth:AuthorizationServers:0:Name"] = "workforce",
            ["AdminEndpoint:Authentication:1:OAuth:AuthorizationServers:0:Issuer"] = "https://sso.example.test/realms/mailfathom",
            ["AdminEndpoint:Authentication:1:OAuth:AuthorizationServers:0:AuthorizedSubjects:0"] = "11111111-2222-3333-4444-555555555555",
        });

        // Act
        var settings = AdminEndpointOptions.ReadFrom(configuration);

        // Assert
        Assert.True(settings.AllowsApiKey);
        Assert.True(settings.AllowsOAuth);
        Assert.Empty(settings.FindConfigurationErrors());
    }

    /// <summary>
    /// Both endpoints read the grant through one method, so the pair that decides everything about it — an absent key
    /// against an emptied list — has to answer here exactly as it does on the MCP endpoint. A reading that differed
    /// would grant one surface what it refused the other from the same configuration.
    /// </summary>
    [Fact]
    public void ReadFrom_TheGrantOnEachEntry_TellsAnAbsentKeyFromAnEmptiedList()
    {
        // Arrange
        var configuration = ConfigurationFromJson("""
            {
              "AdminEndpoint": {
                "Enabled": true,
                "Authentication": [
                  { "ApiKey": { "Name": "workstation", "SecretReference": "plaintext:a-key" } },
                  {
                    "ApiKey": { "Name": "retired", "SecretReference": "plaintext:another-key" },
                    "Permissions": []
                  },
                  {
                    "ApiKey": { "Name": "reporting-job", "SecretReference": "plaintext:a-third-key" },
                    "Permissions": ["mailfathom.admin.read"]
                  }
                ]
              }
            }
            """);

        // Act
        var settings = AdminEndpointOptions.ReadFrom(configuration);

        // Assert
        Assert.Equal(
            MailFathomPermission.PublishedFor(AdminEndpointOptions.GrantedSurface),
            settings.Authentication[0].GrantedPermissions(AdminEndpointOptions.GrantedSurface));
        Assert.Empty(settings.Authentication[1].GrantedPermissions(AdminEndpointOptions.GrantedSurface));
        Assert.Equal(
            [MailFathomPermission.AdminRead],
            settings.Authentication[2].GrantedPermissions(AdminEndpointOptions.GrantedSurface));
        Assert.Empty(settings.FindConfigurationErrors());
    }

    /// <summary>The setting decides whether a token holds the entry's whole ceiling or only what its own scopes carry, and nothing else in the suite would notice it silently ceasing to bind.</summary>
    [Fact]
    public void ReadFrom_AnEntryNarrowingByTokenScopes_BindsTheSetting()
    {
        // Arrange
        var configuration = ConfigurationFromJson("""
            {
              "AdminEndpoint": {
                "Enabled": true,
                "Authentication": [
                  {
                    "OAuth": {
                      "Resource": "https://mail.example.test/api/admin",
                      "AuthorizationServers": [
                        {
                          "Name": "workforce",
                          "Issuer": "https://sso.example.test/realms/mailfathom",
                          "AuthorizedSubjects": [ "11111111-2222-3333-4444-555555555555" ]
                        }
                      ]
                    },
                    "Permissions": ["mailfathom.admin.read"],
                    "PermissionsFromTokenScopes": true
                  }
                ]
              }
            }
            """);

        // Act
        var settings = AdminEndpointOptions.ReadFrom(configuration);

        // Assert
        var entry = Assert.Single(settings.Authentication);
        Assert.True(entry.PermissionsFromTokenScopes);
        Assert.Empty(settings.FindConfigurationErrors());
    }

    /// <summary>The half a grant draws from is the endpoint's own, so a mail permission written here grants nothing and is refused rather than left in the file.</summary>
    [Fact]
    public void FindConfigurationErrors_AGrantNamingAMailPermission_IsRefusedAsBelongingToTheOtherSurface()
    {
        // Arrange
        var configuration = ConfigurationFromJson("""
            {
              "AdminEndpoint": {
                "Enabled": true,
                "Authentication": [
                  {
                    "ApiKey": { "Name": "workstation", "SecretReference": "plaintext:a-key" },
                    "Permissions": ["mailfathom.mail.read"]
                  }
                ]
              }
            }
            """);

        // Act
        var errors = AdminEndpointOptions.ReadFrom(configuration).FindConfigurationErrors();

        // Assert
        var reported = Assert.Single(errors);
        Assert.Contains("AdminEndpoint:Authentication:0:Permissions", reported, StringComparison.Ordinal);
        Assert.Contains("mailfathom.mail.read", reported, StringComparison.Ordinal);
    }

    /// <summary>A value where the list belongs must never read as an unauthenticated deployment, which is what makes the binder raising on it a contract rather than an accident.</summary>
    [Fact]
    public void ReadFrom_AuthenticationWrittenAsAValue_FailsRatherThanReadingAsRequiringNothing()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["AdminEndpoint:Enabled"] = "true",
            ["AdminEndpoint:Authentication"] = "ApiKey",
        });

        // Act, Assert
        Assert.ThrowsAny<InvalidOperationException>(() => AdminEndpointOptions.ReadFrom(configuration));
    }

    /// <summary>An entry stating nothing registers no scheme, so it is refused rather than left to read as a configured method.</summary>
    [Fact]
    public void FindConfigurationErrors_AnEntryStatingNoCredential_IsRefusedRatherThanOpeningTheEndpoint()
    {
        // Arrange
        var settings = EnabledEndpoint();
        settings.Authentication.Add(new TransportAuthenticationOptions());

        // Act, Assert
        Assert.Contains(
            settings.FindConfigurationErrors(),
            error => error.Contains("states no credential", StringComparison.Ordinal));
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
                ["AdminEndpoint:Authentication:0:ApiKey:Name"] = "workstation",
                ["AdminEndpoint:Authentication:0:ApiKey:SecretReference"] = "plaintext:a-key",
                ["AdminEndpoint:Authentication:2:OAuth:Resource"] = "https://mail.example.test:8090/admin",
                ["AdminEndpoint:Authentication:2:OAuth:AuthorizationServers:0:Name"] = "workforce",
                ["AdminEndpoint:Authentication:2:OAuth:AuthorizationServers:0:Issuer"] = "https://sso.example.test/realms/mailfathom",
                ["AdminEndpoint:Authentication:2:OAuth:AuthorizationServers:0:AuthorizedSubjects:0"] = "11111111-2222-3333-4444-555555555555",
            })
            .Build();

        // Act
        var errors = AdminEndpointOptions.ReadFrom(configuration).FindConfigurationErrors();

        // Assert
        var reported = Assert.Single(errors);
        Assert.Contains("AdminEndpoint:Authentication:2:OAuth:Resource", reported, StringComparison.Ordinal);
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

        var oauth = new OAuthValidationOptions { Resource = resource };

        oauth.AuthorizationServers.Add(new AuthorizationServerOptions
        {
            Name = "workforce",
            Issuer = "https://sso.example.test/realms/mailfathom",
        });

        oauth.AuthorizationServers[0].AuthorizedSubjects.Add("11111111-2222-3333-4444-555555555555");

        settings.Authentication.Add(new TransportAuthenticationOptions { OAuth = oauth });

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
