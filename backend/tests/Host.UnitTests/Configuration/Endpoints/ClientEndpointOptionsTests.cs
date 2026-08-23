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

/// <summary>Covers what a deployment gets for configuring the client endpoint, and what it is refused.</summary>
/// <remarks>
/// This is the surface a person's mail client reaches, so two of its answers matter more here than on either existing
/// endpoint: what a deployment that wrote nothing serves, which must be nothing at all, and what a browser is told it
/// may do, which is the one section the administrative endpoint has no use for and the one a page cannot start without.
/// </remarks>
public sealed class ClientEndpointOptionsTests
{
    [Fact]
    public void ReadFrom_ADeploymentThatConfiguresNothing_ServesNoClientSurface()
    {
        // Act
        var settings = ClientEndpointOptions.ReadFrom(new ConfigurationBuilder().Build());

        // Assert: off, so an upgrade opens no new network door onto a mailbox.
        Assert.False(settings.Enabled);
        Assert.False(settings.RequiresAuthentication);
        Assert.Empty(settings.ListenerPorts);
        Assert.Empty(settings.DeclareListeners());
        Assert.Empty(settings.FindConfigurationErrors());
    }

    /// <summary>A misspelled key must fail rather than bind a default, or an operator reads their own configuration as though it took effect.</summary>
    [Fact]
    public void ReadFrom_AMisspelledKey_FailsRatherThanServingAPostureNobodySelected()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["ClientEndpoint:Enabeld"] = "true",
        });

        // Act, Assert
        Assert.ThrowsAny<InvalidOperationException>(() => ClientEndpointOptions.ReadFrom(configuration));
    }

    /// <summary>A value where the list belongs must never read as an unauthenticated deployment, which is what makes the binder raising on it a contract rather than an accident.</summary>
    [Fact]
    public void ReadFrom_AuthenticationWrittenAsAValue_FailsRatherThanReadingAsRequiringNothing()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["ClientEndpoint:Enabled"] = "true",
            ["ClientEndpoint:Authentication"] = "OAuth",
        });

        // Act, Assert
        Assert.ThrowsAny<InvalidOperationException>(() => ClientEndpointOptions.ReadFrom(configuration));
    }

    /// <summary>The credential the issue is written around: a token a page obtained under authorization code with PKCE, which is an ordinary OAuth entry to this section.</summary>
    [Fact]
    public void ReadFrom_TheAuthenticationList_BindsTheSameEntriesTheOtherEndpointsTake()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["ClientEndpoint:Enabled"] = "true",
            ["ClientEndpoint:Authentication:0:ApiKey:Name"] = "desktop-client",
            ["ClientEndpoint:Authentication:0:ApiKey:SecretReference"] = "systemd-credential:client-key",
            ["ClientEndpoint:Authentication:1:OAuth:Resource"] = "https://mail.example.test:8080/api/client",
            ["ClientEndpoint:Authentication:1:OAuth:AuthorizationServers:0:Name"] = "workforce",
            ["ClientEndpoint:Authentication:1:OAuth:AuthorizationServers:0:Issuer"] = "https://sso.example.test/realms/mailfathom",
            ["ClientEndpoint:Authentication:1:OAuth:AuthorizationServers:0:AuthorizedSubjects:0"] = "11111111-2222-3333-4444-555555555555",
        });

        // Act
        var settings = ClientEndpointOptions.ReadFrom(configuration);

        // Assert
        Assert.True(settings.AllowsApiKey);
        Assert.True(settings.AllowsOAuth);
        Assert.Empty(settings.FindConfigurationErrors());
    }

    /// <summary>
    /// The grant is drawn from the mailbox's half, so a permission belonging to the administrative one grants nothing
    /// here and is refused rather than left in the file reading as authority somebody configured.
    /// </summary>
    [Fact]
    public void FindConfigurationErrors_AGrantNamingAnAdministrativePermission_IsRefusedAsBelongingToTheOtherSurface()
    {
        // Arrange
        var configuration = ConfigurationFromJson("""
            {
              "ClientEndpoint": {
                "Enabled": true,
                "Authentication": [
                  {
                    "ApiKey": { "Name": "desktop-client", "SecretReference": "plaintext:a-key" },
                    "Permissions": ["mailfathom.admin.read"]
                  }
                ]
              }
            }
            """);

        // Act
        var errors = ClientEndpointOptions.ReadFrom(configuration).FindConfigurationErrors();

        // Assert
        var reported = Assert.Single(errors);
        Assert.Contains("ClientEndpoint:Authentication:0:Permissions", reported, StringComparison.Ordinal);
        Assert.Contains("mailfathom.admin.read", reported, StringComparison.Ordinal);
    }

    /// <summary>The reading that decides how much of the mailbox an entry carries has to answer here exactly as it does on the two existing endpoints.</summary>
    [Fact]
    public void ReadFrom_TheGrantOnEachEntry_TellsAnAbsentKeyFromAnEmptiedList()
    {
        // Arrange
        var configuration = ConfigurationFromJson("""
            {
              "ClientEndpoint": {
                "Enabled": true,
                "Authentication": [
                  { "ApiKey": { "Name": "desktop-client", "SecretReference": "plaintext:a-key" } },
                  {
                    "ApiKey": { "Name": "retired", "SecretReference": "plaintext:another-key" },
                    "Permissions": []
                  },
                  {
                    "ApiKey": { "Name": "reader", "SecretReference": "plaintext:a-third-key" },
                    "Permissions": ["mailfathom.mail.read"]
                  }
                ]
              }
            }
            """);

        // Act
        var settings = ClientEndpointOptions.ReadFrom(configuration);

        // Assert
        Assert.Equal(
            MailFathomPermission.PublishedFor(ClientEndpointOptions.GrantedSurface),
            settings.Authentication[0].GrantedPermissions(ClientEndpointOptions.GrantedSurface));
        Assert.Empty(settings.Authentication[1].GrantedPermissions(ClientEndpointOptions.GrantedSurface));
        Assert.Equal(
            [MailFathomPermission.MailRead],
            settings.Authentication[2].GrantedPermissions(ClientEndpointOptions.GrantedSurface));
        Assert.Empty(settings.FindConfigurationErrors());
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
            error => error.Contains(nameof(ClientEndpointOptions.BindAddress), StringComparison.Ordinal));
    }

    /// <summary>A deployment that configures nothing serves no page either, so an upgrade puts none in front of a mailbox.</summary>
    [Fact]
    public void ReadFrom_ADeploymentThatConfiguresNothing_ServesNoClient()
    {
        // Act
        var settings = ClientEndpointOptions.ReadFrom(new ConfigurationBuilder().Build());

        // Assert
        Assert.False(settings.Application.Enabled);
        Assert.False(settings.Application.AllowClearText);
    }

    /// <summary>The page is served on this surface's listeners, so serving it without the surface is a client that reaches nothing.</summary>
    [Fact]
    public void FindConfigurationErrors_TheClientServedWhileTheEndpointIsOff_IsRefusedNamingBoth()
    {
        // Arrange
        ClientEndpointOptions settings = new();
        settings.Application.Enabled = true;

        // Act, Assert
        Assert.Contains(
            settings.FindConfigurationErrors(),
            error => error.Contains("ClientEndpoint:Application:Enabled", StringComparison.Ordinal)
                && error.Contains("ClientEndpoint:Enabled", StringComparison.Ordinal));
    }

    /// <summary>The page and every token a browser presents from it cross a public hop, so a clear-text socket is refused rather than warned about.</summary>
    [Fact]
    public void FindConfigurationErrors_TheClientServedOverClearText_IsRefusedAndNamesBothWaysOut()
    {
        // Arrange
        var settings = EnabledEndpoint();
        settings.Application.Enabled = true;

        // Act
        var errors = settings.FindConfigurationErrors();

        // Assert
        Assert.Contains(
            errors,
            error => error.Contains(nameof(EndpointTransport.HttpsOnly), StringComparison.Ordinal)
                && error.Contains("ClientEndpoint:Application:AllowClearText", StringComparison.Ordinal));
    }

    /// <summary>An operator who knows what stands in front of this process is the only one who can answer, and this is them answering.</summary>
    [Fact]
    public void FindConfigurationErrors_TheClientServedOverPermittedClearText_IsAccepted()
    {
        // Arrange
        var settings = EnabledEndpoint();
        settings.Application.Enabled = true;
        settings.Application.AllowClearText = true;

        // Act, Assert
        Assert.Empty(settings.FindConfigurationErrors());
    }

    /// <summary>Terminating TLS here answers the same question, so a deployment that did needs no permission to state.</summary>
    [Fact]
    public void FindConfigurationErrors_TheClientServedOverTls_NeedsNoPermission()
    {
        // Arrange
        var settings = TlsTerminatingEndpoint(EndpointTransport.HttpsOnly);
        settings.Application.Enabled = true;

        // Act, Assert
        Assert.Empty(settings.FindConfigurationErrors());
    }

    /// <summary>A clear-text socket that answers every request with the address of the TLS one carries no page, so it is not one of these.</summary>
    [Fact]
    public void FindConfigurationErrors_TheClientServedBesideARedirectingClearTextSocket_NeedsNoPermission()
    {
        // Arrange
        var settings = TlsTerminatingEndpoint();
        settings.Application.Enabled = true;

        // Act, Assert
        Assert.False(settings.ServesClearText);
        Assert.Empty(settings.FindConfigurationErrors());
    }

    /// <summary>The routes are the surface's own address, and a client appends the rest to what it was configured with.</summary>
    [Fact]
    public void RoutePrefix_IsTheAddressAClientAppendsTo() =>
        Assert.Equal("/api/client", ClientEndpointOptions.RoutePrefix);

    /// <summary>The mailbox's half rather than a vocabulary of its own, so a permission is one thing to grant and one thing to revoke however the mail is read.</summary>
    [Fact]
    public void GrantedSurface_IsTheSameHalfTheMcpEndpointDrawsOn() =>
        Assert.Equal(McpEndpointOptions.GrantedSurface, ClientEndpointOptions.GrantedSurface);

    /// <summary>
    /// The three surfaces are kept apart by the names their schemes and policies carry. Sharing one would merge two
    /// registrations into whichever ran last, and the surface that lost would be guarded by settings its operator never
    /// wrote — which here would hand a page whatever the MCP endpoint's keys admit.
    /// </summary>
    [Fact]
    public void TransportSurface_TheClientSurface_SharesNoNameWithTheOtherTwo()
    {
        // Act
        var clientNames = NamesOf(TransportSurface.Client);

        // Assert
        Assert.Empty(clientNames.Intersect(NamesOf(TransportSurface.Mcp), StringComparer.Ordinal));
        Assert.Empty(clientNames.Intersect(NamesOf(TransportSurface.Admin), StringComparer.Ordinal));
    }

    /// <summary>
    /// A page finds the metadata document by appending the prefix it is about to call to the address it was handed, and
    /// that composition reaches the document's RFC 9728 location only when the resource names the same prefix. There is
    /// nobody to tell the address by hand here, which is why the rule is not advice.
    /// </summary>
    [Theory]
    [InlineData("https://mail.example.test:8080")]
    [InlineData("https://mail.example.test:8080/client")]
    [InlineData("https://mail.example.test:8080/api/client/session")]
    public void FindConfigurationErrors_AResourceThatDoesNotNameTheRoutePrefix_IsRefused(string resource) =>
        Assert.Contains(
            OAuthEndpoint(resource).FindConfigurationErrors(),
            error => error.Contains($"must be '{ClientEndpointOptions.RoutePrefix}'", StringComparison.Ordinal));

    [Theory]
    [InlineData("https://mail.example.test:8080/api/client")]
    [InlineData("https://mail.example.test:8080/api/client/")]
    public void FindConfigurationErrors_AResourceNamingTheRoutePrefix_IsAccepted(string resource) =>
        Assert.Empty(OAuthEndpoint(resource).FindConfigurationErrors());

    /// <summary>A resource that is not an identifier at all is reported as that, rather than as one naming the wrong path.</summary>
    [Fact]
    public void FindConfigurationErrors_AResourceThatIsNotAnIdentifier_ReportsOnlyThat()
    {
        // Act
        var errors = OAuthEndpoint("not-a-url").FindConfigurationErrors();

        // Assert
        Assert.Contains(errors, error => error.Contains("not a canonical resource URL", StringComparison.Ordinal));
        Assert.DoesNotContain(errors, error => error.Contains("must be '/api/client'", StringComparison.Ordinal));
    }

    /// <summary>This endpoint's own rule composes its path like every shared one, so an operator is sent to the key they wrote rather than to the position the binder appended the entry at.</summary>
    [Fact]
    public void FindConfigurationErrors_AResourcePrefixRefusalOnAGappedSource_NamesTheKeyTheOperatorWrote()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["ClientEndpoint:Enabled"] = "true",
            ["ClientEndpoint:Authentication:0:ApiKey:Name"] = "desktop-client",
            ["ClientEndpoint:Authentication:0:ApiKey:SecretReference"] = "plaintext:a-key",
            ["ClientEndpoint:Authentication:2:OAuth:Resource"] = "https://mail.example.test:8080/client",
            ["ClientEndpoint:Authentication:2:OAuth:AuthorizationServers:0:Name"] = "workforce",
            ["ClientEndpoint:Authentication:2:OAuth:AuthorizationServers:0:Issuer"] = "https://sso.example.test/realms/mailfathom",
            ["ClientEndpoint:Authentication:2:OAuth:AuthorizationServers:0:AuthorizedSubjects:0"] = "11111111-2222-3333-4444-555555555555",
        });

        // Act
        var errors = ClientEndpointOptions.ReadFrom(configuration).FindConfigurationErrors();

        // Assert
        var reported = Assert.Single(errors);
        Assert.Contains("ClientEndpoint:Authentication:2:OAuth:Resource", reported, StringComparison.Ordinal);
    }

    /// <summary>
    /// The setting that separates this endpoint from the administrative one. A deployment that wrote no list gets every
    /// browser origin, because a page whose preflight is refused is a client that never starts, and because an origin
    /// authenticates nobody — the credential does.
    /// </summary>
    [Fact]
    public void ReadFrom_NoOriginListAtAll_ServesEveryBrowserOrigin()
    {
        // Act
        var settings = ClientEndpointOptions.ReadFrom(new ConfigurationBuilder().Build());

        // Assert
        Assert.True(settings.Cors.ServesEveryBrowserOrigin);
    }

    /// <summary>An operator who narrowed the surface to the head they deployed must get exactly that, and the binder cannot tell that list from an absent one on its own.</summary>
    [Fact]
    public void ReadFrom_AConfiguredOriginList_ServesExactlyThoseOrigins()
    {
        // Arrange
        var configuration = ConfigurationFromJson("""
            {
              "ClientEndpoint": {
                "Enabled": true,
                "Cors": { "AllowedOrigins": [ "https://client.example.test" ] }
              }
            }
            """);

        // Act
        var settings = ClientEndpointOptions.ReadFrom(configuration);

        // Assert
        Assert.False(settings.Cors.ServesEveryBrowserOrigin);
        Assert.Equal(["https://client.example.test"], settings.Cors.AllowedOrigins);
        Assert.Empty(settings.FindConfigurationErrors());
    }

    /// <summary>An emptied list is the deployment that serves no browser at all, and it must not read as the absent one that serves every page on the internet.</summary>
    [Fact]
    public void ReadFrom_AnEmptiedOriginList_ServesNoBrowserOrigin()
    {
        // Arrange
        var configuration = ConfigurationFromJson("""
            {
              "ClientEndpoint": { "Enabled": true, "Cors": { "AllowedOrigins": [] } }
            }
            """);

        // Act
        var settings = ClientEndpointOptions.ReadFrom(configuration);

        // Assert
        Assert.False(settings.Cors.ServesEveryBrowserOrigin);
        Assert.Empty(settings.Cors.AllowedOrigins);
    }

    /// <summary>The same rules the MCP section applies, reported under this section's own path so an operator knows which surface to fix.</summary>
    [Fact]
    public void FindConfigurationErrors_AnUnusableOrigin_IsRefusedUnderThisEndpointsSection()
    {
        // Arrange
        var settings = EnabledEndpoint();
        settings.Cors.AllowedOrigins.Add("client.example.test");

        // Act
        var errors = settings.FindConfigurationErrors();

        // Assert
        Assert.Contains(
            errors,
            error => error.StartsWith("ClientEndpoint:Cors:AllowedOrigins:0", StringComparison.Ordinal));
    }

    /// <summary>
    /// The section that must apply whether or not anyone wrote it: a surface a page reaches with no limit is unbounded
    /// key guessing from every browser on the internet.
    /// </summary>
    [Fact]
    public void RateLimiting_WithNothingConfigured_BoundsTheEndpointOnTheProductDefaults()
    {
        // Act
        var settings = ClientEndpointOptions.ReadFrom(new ConfigurationBuilder().Build());

        // Assert
        Assert.True(settings.RateLimiting.Enabled);
        Assert.Equal(TransportRateLimits.Default.MaxConcurrentRequests, settings.RateLimiting.MaxConcurrentRequests);
        Assert.Equal(TransportRateLimits.Default.TokenCapacity, settings.RateLimiting.TokenCapacity);
    }

    [Fact]
    public void ReadFrom_TheRateLimitingSection_BindsIndependentlyOfTheOtherEndpoints()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["ClientEndpoint:Enabled"] = "true",
            ["ClientEndpoint:RateLimiting:MaxConcurrentRequests"] = "6",
            ["ClientEndpoint:RateLimiting:TokenCapacity"] = "60",
            ["ClientEndpoint:RateLimiting:ReplenishmentPeriod"] = "00:00:15",
        });

        // Act
        var settings = ClientEndpointOptions.ReadFrom(configuration);

        // Assert
        Assert.Equal(6, settings.RateLimiting.MaxConcurrentRequests);
        Assert.Equal(60, settings.RateLimiting.TokenCapacity);
        Assert.Equal(TimeSpan.FromSeconds(15), settings.RateLimiting.ReplenishmentPeriod);
        Assert.Empty(settings.FindConfigurationErrors());
    }

    [Fact]
    public void FindConfigurationErrors_AnUnusableRateLimit_IsRefusedUnderThisEndpointsSection()
    {
        // Arrange
        var settings = EnabledEndpoint();
        settings.RateLimiting.MaxConcurrentRequests = 0;

        // Act, Assert
        Assert.Contains(
            settings.FindConfigurationErrors(),
            error => error.StartsWith("ClientEndpoint:RateLimiting:MaxConcurrentRequests", StringComparison.Ordinal));
    }

    [Fact]
    public void RequestTimeout_WithNothingConfigured_BoundsTheEndpointOnTheProductDefault()
    {
        // Act
        var settings = ClientEndpointOptions.ReadFrom(new ConfigurationBuilder().Build());

        // Assert
        Assert.True(settings.RequestTimeout.Enabled);
        Assert.Equal(new TransportRequestTimeoutOptions().Duration, settings.RequestTimeout.Duration);
    }

    [Fact]
    public void ReadFrom_TheRequestTimeoutSection_BindsIndependentlyOfTheOtherEndpoints()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["ClientEndpoint:Enabled"] = "true",
            ["ClientEndpoint:RequestTimeout:Duration"] = "00:00:20",
        });

        // Act
        var settings = ClientEndpointOptions.ReadFrom(configuration);

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(20), settings.RequestTimeout.Duration);
        Assert.Empty(settings.FindConfigurationErrors());
    }

    [Fact]
    public void FindConfigurationErrors_AnUnusableRequestCeiling_IsRefusedUnderThisEndpointsSection()
    {
        // Arrange
        var settings = EnabledEndpoint();
        settings.RequestTimeout.Duration = TimeSpan.Zero;

        // Act, Assert
        Assert.Contains(
            settings.FindConfigurationErrors(),
            error => error.StartsWith("ClientEndpoint:RequestTimeout:Duration", StringComparison.Ordinal));
    }

    /// <summary>A surface nobody serves is refused nothing, because there is no listener for a faulty setting to spoil.</summary>
    [Fact]
    public void FindConfigurationErrors_ADisabledEndpointWithAnUnusableRateLimit_ReportsNothing()
    {
        // Arrange
        var settings = new ClientEndpointOptions();
        settings.RateLimiting.TokenCapacity = 0;

        // Act, Assert
        Assert.Empty(settings.FindConfigurationErrors());
    }

    [Fact]
    public void ReadFrom_AConfiguredRedirect_BindsItAndRecordsThatItWasStated()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["ClientEndpoint:Enabled"] = "true",
            ["ClientEndpoint:Https:Redirect:Enabled"] = "true",
        });

        // Act
        var settings = ClientEndpointOptions.ReadFrom(configuration);

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
            ["ClientEndpoint:Enabled"] = "true",
            ["ClientEndpoint:Https:Redirect:Enabled"] = "true",
        });

        // Act, Assert
        Assert.Contains(
            ClientEndpointOptions.ReadFrom(configuration).FindConfigurationErrors(),
            error => error.Contains("a clear-text redirect is configured", StringComparison.Ordinal));
    }

    /// <summary>The redirect's port is one this endpoint binds, so it is claimed here and checked against every other listener in the process.</summary>
    [Fact]
    public void ListenerPorts_AnEndpointTerminatingTls_ClaimsTheRedirectPortBesideTheProfiles() =>
        Assert.Equal([8080, 8643], TlsTerminatingEndpoint().ListenerPorts.Order());

    [Fact]
    public void ListenerPorts_ATransportServingTlsAlone_ClaimsTheProfilePortsAlone() =>
        Assert.Equal([8643], TlsTerminatingEndpoint(EndpointTransport.HttpsOnly).ListenerPorts.Order());

    [Fact]
    public void ListenerPorts_AnEndpointTerminatingNoTls_ClaimsItsClearTextPortAlone() =>
        Assert.Equal([8080], EnabledEndpoint().ListenerPorts.Order());

    /// <summary>The listener carries the surface it serves, which is what keeps a mailbox off a port published for an agent or an operator.</summary>
    [Fact]
    public void DeclareListeners_AnEnabledEndpoint_DeclaresTheClientSurfaceUnderItsOwnSection()
    {
        // Act
        var listener = Assert.Single(EnabledEndpoint().DeclareListeners());

        // Assert
        Assert.Equal(ServedSurfaces.Client, listener.Surface);
        Assert.Equal(ClientEndpointOptions.SectionName, listener.SectionName);
    }

    private static string[] NamesOf(TransportSurface surface) =>
    [
        surface.RoutingSchemeName,
        surface.ApiKeySchemeName,
        surface.AccessPolicyName,
        surface.RateLimitingPolicyName,
        surface.RequestTimeoutPolicyName,
        surface.OAuthSchemeNameFor("workforce"),
    ];

    private static ClientEndpointOptions EnabledEndpoint() => new() { Enabled = true };

    private static ClientEndpointOptions TlsTerminatingEndpoint(
        EndpointTransport transport = EndpointTransport.HttpAndHttps)
    {
        var settings = EnabledEndpoint();
        settings.Transport = transport;
        settings.Https.Endpoints.Add(new TransportHttpsEndpointOptions
        {
            Name = "client",
            Domain = "client.example.test",
            Port = 8643,
            ServerCertificate = new TlsServerCertificateOptions
            {
                Bundle = new ConfiguredSecret { Name = "bundle", SecretReference = "file:/etc/mailfathom/tls/client.pfx" },
            },
        });

        return settings;
    }

    private static ClientEndpointOptions OAuthEndpoint(string resource)
    {
        ClientEndpointOptions settings = new() { Enabled = true };

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
