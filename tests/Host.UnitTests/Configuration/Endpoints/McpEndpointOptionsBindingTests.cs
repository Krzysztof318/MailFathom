// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Access;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Infrastructure.Secrets;
using MailFathom.Infrastructure.Security.ClientCertificates;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Endpoints;

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
            ["McpEndpoint:Authentication:0:ApiKey:Name"] = "workstation",
            ["McpEndpoint:Authentication:0:ApiKey:SecretReference"] = "systemd-credential:mailfathom-mcp-workstation-key",
            ["McpEndpoint:Authentication:1:ApiKey:Name"] = "chatgpt-connector",
            ["McpEndpoint:Authentication:1:ApiKey:SecretReference"] = "file:/run/secrets/mailfathom-mcp-chatgpt-key",
            ["McpEndpoint:Authentication:1:ApiKey:Lifetime"] = "2027-01-31T00:00:00Z",
            ["McpEndpoint:Cors:AllowedOrigins:0"] = "https://client.example.test",
            ["McpEndpoint:Cors:AllowedOrigins:1"] = "https://console.example.test:8443",
            ["McpEndpoint:RateLimiting:MaxConcurrentRequests"] = "12",
            ["McpEndpoint:RateLimiting:TokenCapacity"] = "40",
            ["McpEndpoint:RateLimiting:TokensPerReplenishmentPeriod"] = "10",
            ["McpEndpoint:RateLimiting:ReplenishmentPeriod"] = "00:00:30",
            ["McpEndpoint:RequestTimeout:Duration"] = "00:02:00",
        });

        // Act
        var options = McpEndpointOptions.ReadFrom(configuration);

        // Assert
        Assert.True(options.Enabled);
        var apiKeys = options.ApiKeys();
        Assert.Empty(options.OAuthMethods());
        Assert.Equal(["workstation", "chatgpt-connector"], apiKeys.Select(key => key.Name));
        Assert.Equal(
            [SecretLifetime.NoLimitValue, "2027-01-31T00:00:00Z"],
            apiKeys.Select(key => key.Lifetime));
        Assert.Equal(
            ["https://client.example.test", "https://console.example.test:8443"],
            options.Cors.AllowedOrigins);
        Assert.Equal(12, options.RateLimiting.MaxConcurrentRequests);
        Assert.Equal(40, options.RateLimiting.TokenCapacity);
        Assert.Equal(10, options.RateLimiting.TokensPerReplenishmentPeriod);
        Assert.Equal(TimeSpan.FromSeconds(30), options.RateLimiting.ReplenishmentPeriod);
        Assert.Equal(TimeSpan.FromMinutes(2), options.RequestTimeout.Duration);
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
            ["McpEndpoint:RateLimiting:MaxConcurrentRequests"] = "5",
        });

        // Act
        var options = McpEndpointOptions.ReadFrom(configuration);

        // Assert
        var defaults = new TransportRateLimitingOptions();
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
            ["McpEndpoint:Transport"] = "HttpsOnly",
            ["McpEndpoint:Https:Endpoints:0:Name"] = "public",
            ["McpEndpoint:Https:Endpoints:0:Domain"] = "mail.example.test",
            ["McpEndpoint:Https:Endpoints:0:ServerCertificate:Bundle:Name"] = "bundle",
            ["McpEndpoint:Https:Endpoints:0:ServerCertificate:Bundle:SecretReference"] = "file:/etc/mailfathom/tls/mail.pfx",
            ["McpEndpoint:ClientCertificateProfiles:0:Name"] = "chatgpt-connector",
            ["McpEndpoint:ClientCertificateProfiles:0:Requirement"] = "Optional",
            ["McpEndpoint:ClientCertificateProfiles:0:TrustAnchors:0:Name"] = "openai-connectors-ca",
            ["McpEndpoint:ClientCertificateProfiles:0:TrustAnchors:0:SecretReference"] = "file:/etc/mailfathom/openai-connectors-ca.pem",
            ["McpEndpoint:ClientCertificateProfiles:0:TrustAnchors:1:Name"] = "openai-connectors-ca-next",
            ["McpEndpoint:ClientCertificateProfiles:0:TrustAnchors:1:SecretReference"] = "file:/etc/mailfathom/openai-connectors-ca-next.pem",
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
        Assert.Empty(options.Authentication);
        Assert.Empty(options.ApiKeys());
        Assert.True(options.Cors.ServesEveryBrowserOrigin);
    }

    /// <summary>A surface accepting both kinds of caller carries one entry per method, and each entry binds whole.</summary>
    [Fact]
    public void ReadFrom_BothMethodsAsSeparateEntries_ReadsEachWithItsOwnSettings()
    {
        // Arrange
        var configuration = ConfigurationFrom(new Dictionary<string, string?>
        {
            ["McpEndpoint:Enabled"] = "true",
            ["McpEndpoint:Authentication:0:ApiKey:Name"] = "nightly-digest",
            ["McpEndpoint:Authentication:0:ApiKey:SecretReference"] = "systemd-credential:mailfathom-mcp-digest-key",
            ["McpEndpoint:Authentication:1:OAuth:Resource"] = "https://mail.example.test/mcp",
            ["McpEndpoint:Authentication:1:OAuth:RequiredScopes:0"] = "mailfathom.read",
            ["McpEndpoint:Authentication:1:OAuth:AuthorizationServers:0:Name"] = "workforce",
            ["McpEndpoint:Authentication:1:OAuth:AuthorizationServers:0:Issuer"] = "https://sso.example.test/realms/mailfathom",
            ["McpEndpoint:Authentication:1:OAuth:AuthorizationServers:0:AuthorizedSubjects:0"] = "9f2c",
        });

        // Act
        var options = McpEndpointOptions.ReadFrom(configuration);

        // Assert
        Assert.True(options.AllowsApiKey);
        Assert.True(options.AllowsOAuth);
        var oauth = Assert.Single(options.OAuthMethods());
        Assert.Equal(["nightly-digest"], options.ApiKeys().Select(key => key.Name));
        Assert.Equal("https://mail.example.test/mcp", oauth.Resource);
        Assert.Equal(["mailfathom.read"], oauth.RequiredScopes);
        Assert.Equal(["workforce"], oauth.AuthorizationServers.Select(server => server.Name));
        Assert.Equal(["9f2c"], oauth.AuthorizationServers.Single().AuthorizedSubjects);
        Assert.Empty(options.FindConfigurationErrors());
    }

    /// <summary>
    /// A value where the list belongs is the one misreading of this setting that could open a surface instead of
    /// closing it, so it must never bind to the empty list an unauthenticated deployment carries. The binder cannot
    /// convert one into a list and raises while the section is read, which is why no rule elsewhere restates it.
    /// </summary>
    [Fact]
    public void ReadFrom_AuthenticationWrittenAsAValue_FailsRatherThanBindingToNoMethodAtAll()
    {
        // Arrange
        var configuration = ConfigurationFrom(new Dictionary<string, string?>
        {
            ["McpEndpoint:Enabled"] = "true",
            ["McpEndpoint:Authentication"] = "ApiKey, OAuth",
        });

        // Act, Assert
        Assert.ThrowsAny<InvalidOperationException>(() => McpEndpointOptions.ReadFrom(configuration));
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

    /// <summary>
    /// The grant is read the way the origin list is, and the pair is pinned from real JSON for the same reason: an
    /// absent list and an emptied one bind identically, and only the section can say which the operator wrote.
    /// </summary>
    [Fact]
    public void ReadFrom_AnEntryWithNoPermissionsKey_ReachesTheWholeSurface()
    {
        // Arrange
        var configuration = ConfigurationFromJson("""
            {
              "McpEndpoint": {
                "Enabled": true,
                "Authentication": [
                  { "ApiKey": { "Name": "workstation", "SecretReference": "plaintext:a-key" } }
                ]
              }
            }
            """);

        // Act
        var options = McpEndpointOptions.ReadFrom(configuration);

        // Assert
        var entry = Assert.Single(options.Authentication);
        Assert.True(entry.GrantsTheWholeSurface);
        Assert.Equal(
            MailFathomPermission.PublishedFor(McpEndpointOptions.GrantedSurface),
            entry.GrantedPermissions(McpEndpointOptions.GrantedSurface));
    }

    /// <summary>An emptied grant retires a credential without deleting its entry, so it must not read as the entry that never narrowed.</summary>
    [Fact]
    public void ReadFrom_AnEntryWithAnEmptyPermissionsList_ReachesNothing()
    {
        // Arrange
        var configuration = ConfigurationFromJson("""
            {
              "McpEndpoint": {
                "Enabled": true,
                "Authentication": [
                  {
                    "ApiKey": { "Name": "workstation", "SecretReference": "plaintext:a-key" },
                    "Permissions": []
                  }
                ]
              }
            }
            """);

        // Act
        var options = McpEndpointOptions.ReadFrom(configuration);

        // Assert
        var entry = Assert.Single(options.Authentication);
        Assert.False(entry.GrantsTheWholeSurface);
        Assert.Empty(entry.GrantedPermissions(McpEndpointOptions.GrantedSurface));
        Assert.Empty(options.FindConfigurationErrors());
    }

    /// <summary>The grant belongs to the entry, so the read has to answer the question once per entry rather than once per section.</summary>
    [Fact]
    public void ReadFrom_TwoEntriesGrantedDifferently_ReadsTheGrantWrittenOnEachEntry()
    {
        // Arrange
        var configuration = ConfigurationFromJson("""
            {
              "McpEndpoint": {
                "Enabled": true,
                "Authentication": [
                  {
                    "ApiKey": { "Name": "reporting-job", "SecretReference": "plaintext:a-key" },
                    "Permissions": ["mailfathom.mail.read"]
                  },
                  { "ApiKey": { "Name": "workstation", "SecretReference": "plaintext:another-key" } }
                ]
              }
            }
            """);

        // Act
        var options = McpEndpointOptions.ReadFrom(configuration);

        // Assert
        Assert.Equal(["mailfathom.mail.read"], options.Authentication[0].Permissions);
        Assert.Equal(
            [MailFathomPermission.MailRead],
            options.Authentication[0].GrantedPermissions(McpEndpointOptions.GrantedSurface));
        Assert.Equal(
            MailFathomPermission.PublishedFor(McpEndpointOptions.GrantedSurface),
            options.Authentication[1].GrantedPermissions(McpEndpointOptions.GrantedSurface));
    }

    /// <summary>
    /// A configuration source numbering its entries with a gap — the environment-variable form a container deployment
    /// writes — binds them into consecutive list positions, so a grant read by position would land on the wrong entry
    /// and hand the narrowed one its surface's whole half.
    /// </summary>
    [Fact]
    public void ReadFrom_EntriesNumberedWithAGap_ReadsEachGrantFromTheEntryThatWroteIt()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["McpEndpoint:Enabled"] = "true",
                ["McpEndpoint:Authentication:0:ApiKey:Name"] = "workstation",
                ["McpEndpoint:Authentication:0:ApiKey:SecretReference"] = "plaintext:a-key",
                ["McpEndpoint:Authentication:2:ApiKey:Name"] = "reporting-job",
                ["McpEndpoint:Authentication:2:ApiKey:SecretReference"] = "plaintext:another-key",
                ["McpEndpoint:Authentication:2:Permissions:0"] = "mailfathom.mail.read",
            })
            .Build();

        // Act
        var options = McpEndpointOptions.ReadFrom(configuration);

        // Assert
        Assert.Equal(
            MailFathomPermission.PublishedFor(McpEndpointOptions.GrantedSurface),
            options.Authentication[0].GrantedPermissions(McpEndpointOptions.GrantedSurface));
        Assert.Equal(
            [MailFathomPermission.MailRead],
            options.Authentication[1].GrantedPermissions(McpEndpointOptions.GrantedSurface));
    }

    /// <summary>A refusal names the position an operator has to go and edit, and where the numbering has a gap that is the key they wrote rather than the one the binder appended at.</summary>
    [Fact]
    public void FindConfigurationErrors_EntriesNumberedWithAGap_NameTheKeyTheOperatorWrote()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["McpEndpoint:Enabled"] = "true",
                ["McpEndpoint:Authentication:0:ApiKey:Name"] = "workstation",
                ["McpEndpoint:Authentication:0:ApiKey:SecretReference"] = "plaintext:a-key",
                ["McpEndpoint:Authentication:2:ApiKey:Name"] = "reporting-job",
                ["McpEndpoint:Authentication:2:ApiKey:SecretReference"] = "plaintext:another-key",
                ["McpEndpoint:Authentication:2:Permissions:0"] = "mailfathom.mail.write",
            })
            .Build();

        // Act
        var errors = McpEndpointOptions.ReadFrom(configuration).FindConfigurationErrors();

        // Assert
        var reported = Assert.Single(errors);
        Assert.Contains("McpEndpoint:Authentication:2:Permissions:0", reported, StringComparison.Ordinal);
    }

    /// <summary>Every refusal against an entry names the key it was written under, not only the ones a grant adds — a path composed per rule would drift back to the bound position one rule at a time.</summary>
    [Fact]
    public void FindConfigurationErrors_EntriesNumberedWithAGap_NameTheKeyInARefusalNoGrantProduced()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["McpEndpoint:Enabled"] = "true",
                ["McpEndpoint:Authentication:0:OAuth:Resource"] = "https://mail.example.test/mcp",
                ["McpEndpoint:Authentication:0:OAuth:AuthorizationServers:0:Name"] = "workforce",
                ["McpEndpoint:Authentication:0:OAuth:AuthorizationServers:0:Issuer"] = "https://sso.example.test/realms/mailfathom",
                ["McpEndpoint:Authentication:0:OAuth:AuthorizationServers:0:AuthorizedSubjects:0"] = "11111111-2222-3333-4444-555555555555",
                ["McpEndpoint:Authentication:2:OAuth:Resource"] = "https://mail.example.test/elsewhere",
                ["McpEndpoint:Authentication:2:OAuth:AuthorizationServers:0:Name"] = "partners",
                ["McpEndpoint:Authentication:2:OAuth:AuthorizationServers:0:Issuer"] = "https://partners.example.test/realms/mailfathom",
                ["McpEndpoint:Authentication:2:OAuth:AuthorizationServers:0:AuthorizedSubjects:0"] = "22222222-3333-4444-5555-666666666666",
            })
            .Build();

        // Act
        var errors = McpEndpointOptions.ReadFrom(configuration).FindConfigurationErrors();

        // Assert
        var reported = Assert.Single(errors);
        Assert.Contains("McpEndpoint:Authentication:2:OAuth:Resource", reported, StringComparison.Ordinal);
    }

    /// <summary>
    /// An element carrying nothing binds to an entry rather than being dropped, so the children and the bound entries
    /// stay the same length and every grant is still read off the entry that wrote it. The empty element is refused
    /// for stating no method, which is the answer it already had before a grant could be written on one.
    /// </summary>
    [Fact]
    public void ReadFrom_AnEmptyElementInTheList_BindsAnEntryThatIsRefusedRatherThanShiftingTheGrants()
    {
        // Arrange
        var configuration = ConfigurationFromJson("""
            {
              "McpEndpoint": {
                "Enabled": true,
                "Authentication": [
                  null,
                  {
                    "ApiKey": { "Name": "retired", "SecretReference": "plaintext:a-key" },
                    "Permissions": []
                  }
                ]
              }
            }
            """);

        // Act
        var options = McpEndpointOptions.ReadFrom(configuration);

        // Assert
        Assert.Equal(2, options.Authentication.Count);
        Assert.Empty(options.Authentication[1].GrantedPermissions(McpEndpointOptions.GrantedSurface));

        var reported = Assert.Single(options.FindConfigurationErrors());
        Assert.Contains("McpEndpoint:Authentication:0", reported, StringComparison.Ordinal);
    }

    /// <summary>The setting decides whether a token holds the entry's whole ceiling or only what its own scopes carry, and nothing else in the suite would notice it silently ceasing to bind.</summary>
    [Fact]
    public void ReadFrom_AnEntryNarrowingByTokenScopes_BindsTheSetting()
    {
        // Arrange
        var configuration = ConfigurationFromJson("""
            {
              "McpEndpoint": {
                "Enabled": true,
                "Authentication": [
                  {
                    "OAuth": {
                      "Resource": "https://mail.example.test/mcp",
                      "AuthorizationServers": [
                        {
                          "Name": "workforce",
                          "Issuer": "https://sso.example.test/realms/mailfathom",
                          "AuthorizedSubjects": [ "11111111-2222-3333-4444-555555555555" ]
                        }
                      ]
                    },
                    "Permissions": ["mailfathom.mail.read"],
                    "PermissionsFromTokenScopes": true
                  }
                ]
              }
            }
            """);

        // Act
        var options = McpEndpointOptions.ReadFrom(configuration);

        // Assert
        var entry = Assert.Single(options.Authentication);
        Assert.True(entry.PermissionsFromTokenScopes);
        Assert.Empty(options.FindConfigurationErrors());
    }

    /// <summary>The whole point of a closed vocabulary is that a name nothing publishes fails startup instead of reading as a narrowed grant.</summary>
    [Fact]
    public void ReadFrom_AnEntryNamingAnUnpublishedPermission_IsRefusedNamingTheEntry()
    {
        // Arrange
        var configuration = ConfigurationFromJson("""
            {
              "McpEndpoint": {
                "Enabled": true,
                "Authentication": [
                  {
                    "ApiKey": { "Name": "workstation", "SecretReference": "plaintext:a-key" },
                    "Permissions": ["mailfathom.mail.write"]
                  }
                ]
              }
            }
            """);

        // Act
        var errors = McpEndpointOptions.ReadFrom(configuration).FindConfigurationErrors();

        // Assert
        var reported = Assert.Single(errors);
        Assert.Contains("McpEndpoint:Authentication:0:Permissions:0", reported, StringComparison.Ordinal);
    }

    /// <summary>A misspelling that bound quietly would leave a security decision reading as one nobody made.</summary>
    [Theory]
    [InlineData("McpEndpoint:Enabeld", "true")]
    [InlineData("McpEndpoint:Authentication:0:Permission:0", "mailfathom.mail.read")]
    [InlineData("McpEndpoint:Authentication:0:PermissionsFromTokenScope", "true")]
    [InlineData("McpEndpoint:Authentication:0:ApiKeys:Name", "workstation")]
    [InlineData("McpEndpoint:Authentication:0:ApiKey:Named", "workstation")]
    [InlineData("McpEndpoint:ApiKeys:0:Name", "workstation")]
    [InlineData("McpEndpoint:OAuth:Resource", "https://mail.example.test/mcp")]
    [InlineData("McpEndpoint:Cors:AllowedOrigin", "https://client.example.test")]
    [InlineData("McpEndpoint:RateLimiting:Enabeld", "false")]
    [InlineData("McpEndpoint:RateLimiting:MaxConcurrentRequest", "5")]
    [InlineData("McpEndpoint:RateLimit:MaxConcurrentRequests", "5")]
    [InlineData("McpEndpoint:RequestTimeout:Enabeld", "false")]
    [InlineData("McpEndpoint:RequestTimeout:Timeout", "00:02:00")]
    [InlineData("McpEndpoint:RequestTimeouts:Duration", "00:02:00")]
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

    /// <summary>
    /// A method an entry does not name is a startup failure rather than a silent fall back to the unauthenticated
    /// posture. The block name is the method, so a misspelling is an unknown key and strict binding is what refuses it.
    /// </summary>
    [Theory]
    [InlineData("McpEndpoint:Authentication:0:ApiKye:Name")]
    [InlineData("McpEndpoint:Authentication:0:OAtuh:Resource")]
    public void ReadFrom_AnEntryNamingNoKnownMethod_Fails(string key)
    {
        // Arrange
        var configuration = ConfigurationFrom(new Dictionary<string, string?>
        {
            ["McpEndpoint:Enabled"] = "true",
            [key] = "workstation",
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
            ["McpEndpoint:Transport"] = "HttpsOnly",
            ["McpEndpoint:Https:Endpoints:0:Name"] = "public",
            ["McpEndpoint:Https:Endpoints:0:Domain"] = "mail.example.test",
            ["McpEndpoint:Https:Endpoints:0:Port"] = "443",
            ["McpEndpoint:Https:Endpoints:0:MinimumTlsVersion"] = "Tls13",
            ["McpEndpoint:Https:Endpoints:0:HttpProtocols:0"] = "Http1",
            ["McpEndpoint:Https:Endpoints:0:HttpProtocols:1"] = "Http2",
            ["McpEndpoint:Https:Endpoints:0:ServerCertificate:CertificateChain:Name"] = "public-chain",
            ["McpEndpoint:Https:Endpoints:0:ServerCertificate:CertificateChain:SecretReference"] = "file:/etc/mailfathom/tls/fullchain.pem",
            ["McpEndpoint:Https:Endpoints:0:ServerCertificate:PrivateKey:Name"] = "public-key",
            ["McpEndpoint:Https:Endpoints:0:ServerCertificate:PrivateKey:SecretReference"] = "file:/etc/mailfathom/tls/privkey.pem",
            ["McpEndpoint:Https:Endpoints:1:Name"] = "connector",
            ["McpEndpoint:Https:Endpoints:1:Domain"] = "connector.example.test",
            ["McpEndpoint:Https:Endpoints:1:Port"] = "443",
            ["McpEndpoint:Https:Endpoints:1:ServerCertificate:Bundle:Name"] = "connector-bundle",
            ["McpEndpoint:Https:Endpoints:1:ServerCertificate:Bundle:SecretReference"] = "file:/etc/mailfathom/tls/connector.pfx",
            ["McpEndpoint:Https:Endpoints:1:ServerCertificate:Bundle:Password:Name"] = "connector-bundle-password",
            ["McpEndpoint:Https:Endpoints:1:ServerCertificate:Bundle:Password:SecretReference"] = "systemd-credential:mailfathom-tls-password",
        });

        // Act
        var options = McpEndpointOptions.ReadFrom(configuration);

        // Assert
        Assert.Equal(["public", "connector"], options.Https.Endpoints.Select(endpoint => endpoint.Name));
        Assert.Equal(TransportMinimumTlsVersion.Tls13, options.Https.Endpoints[0].MinimumTlsVersion);
        Assert.Equal(
            [TransportHttpProtocol.Http1, TransportHttpProtocol.Http2],
            options.Https.Endpoints[0].ServedHttpProtocols);
        Assert.Equal(
            "file:/etc/mailfathom/tls/privkey.pem",
            options.Https.Endpoints[0].ServerCertificate.PrivateKey?.SecretReference);
        Assert.Equal(
            "systemd-credential:mailfathom-tls-password",
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
            ["McpEndpoint:Https:Endpoints:0:Name"] = "public",
            ["McpEndpoint:Https:Endpoints:0:Domain"] = "mail.example.test",
            ["McpEndpoint:Https:Endpoints:0:ServerCertificate:Bundle:Name"] = "bundle",
            ["McpEndpoint:Https:Endpoints:0:ServerCertificate:Bundle:SecretReference"] = "file:/etc/mailfathom/tls/bundle.pfx",
        });

        // Act
        var profile = Assert.Single(McpEndpointOptions.ReadFrom(configuration).Https.Endpoints);

        // Assert
        Assert.Null(profile.HttpProtocols);
        Assert.Equal([TransportHttpProtocol.Http1, TransportHttpProtocol.Http2], profile.ServedHttpProtocols);
    }

    /// <summary>
    /// The redirect is on by default, so an absent section and one an operator wrote bind to identical values. Only
    /// configuration can tell them apart, which is what makes a redirect stated for a surface terminating no TLS a startup
    /// error rather than a default nobody asked for.
    /// </summary>
    [Fact]
    public void ReadFrom_ASectionWithNoRedirectOfItsOwn_LeavesTheRedirectUnstated()
    {
        // Arrange
        var configuration = ConfigurationFrom(new Dictionary<string, string?>
        {
            ["McpEndpoint:Enabled"] = "true",
        });

        // Act
        var settings = McpEndpointOptions.ReadFrom(configuration);

        // Assert
        Assert.False(settings.Https.Redirect.WasStated);
        Assert.True(settings.Https.Redirect.Enabled);
    }

    [Fact]
    public void ReadFrom_AConfiguredRedirect_BindsItAndRecordsThatItWasStated()
    {
        // Arrange
        var configuration = ConfigurationFrom(new Dictionary<string, string?>
        {
            ["McpEndpoint:Enabled"] = "true",
            ["McpEndpoint:Https:Redirect:Enabled"] = "true",
        });

        // Act
        var settings = McpEndpointOptions.ReadFrom(configuration);

        // Assert
        Assert.True(settings.Https.Redirect.WasStated);
        Assert.True(settings.Https.Redirect.Enabled);
    }

    /// <summary>Turning it off is stating it, which is what makes the setting readable as a decision rather than as silence.</summary>
    [Fact]
    public void ReadFrom_ARedirectTurnedOff_BindsAsStatedAndDisabled()
    {
        // Arrange
        var configuration = ConfigurationFrom(new Dictionary<string, string?>
        {
            ["McpEndpoint:Enabled"] = "true",
            ["McpEndpoint:Https:Redirect:Enabled"] = "false",
        });

        // Act
        var settings = McpEndpointOptions.ReadFrom(configuration);

        // Assert
        Assert.True(settings.Https.Redirect.WasStated);
        Assert.False(settings.Https.Redirect.Enabled);
    }


    /// <summary>
    /// Every socket the surface opens, so the probes and the administrative endpoint are checked against the redirect port
    /// as well and a conflict is reported against a section rather than as an address already in use.
    /// </summary>
    [Fact]
    public void ListenerPorts_BothSchemes_ClaimsTheClearTextPortBesideTheProfiles()
    {
        // Arrange
        var configuration = ConfigurationFrom(HttpsProfile(new Dictionary<string, string?>
        {
            ["McpEndpoint:Transport"] = "HttpAndHttps",
            ["McpEndpoint:Https:Endpoints:0:Port"] = "9443",
        }));

        // Act, Assert
        Assert.Equal([8080, 9443], McpEndpointOptions.ReadFrom(configuration).ListenerPorts.Order());
    }

    /// <summary>The clear-text socket is claimed whether it redirects or serves the routes, because either way it is bound.</summary>
    [Fact]
    public void ListenerPorts_BothSchemesWithTheRedirectOff_StillClaimsTheClearTextPort()
    {
        // Arrange
        var configuration = ConfigurationFrom(HttpsProfile(new Dictionary<string, string?>
        {
            ["McpEndpoint:Transport"] = "HttpAndHttps",
            ["McpEndpoint:Https:Redirect:Enabled"] = "false",
        }));

        // Act, Assert
        Assert.Equal([8080, 8443], McpEndpointOptions.ReadFrom(configuration).ListenerPorts.Order());
    }

    [Fact]
    public void ListenerPorts_TlsAlone_ClaimsTheProfilePortsAlone() =>
        Assert.Equal(
            [8443],
            McpEndpointOptions.ReadFrom(ConfigurationFrom(HttpsProfile([]))).ListenerPorts.Order());

    /// <summary>A surface nobody enabled binds nothing, so it claims no port for another surface to be refused over.</summary>
    [Fact]
    public void ListenerPorts_ASurfaceNobodyEnabled_ClaimsNothing() =>
        Assert.Empty(McpEndpointOptions.ReadFrom(ConfigurationFrom([])).ListenerPorts);

    /// <summary>Clear text is the default, so a deployment that states only that the endpoint is on binds one socket.</summary>
    [Fact]
    public void ListenerPorts_ClearTextAlone_ClaimsTheEndpointsOwnPort() =>
        Assert.Equal(
            [8080],
            McpEndpointOptions.ReadFrom(ConfigurationFrom(new Dictionary<string, string?>
            {
                ["McpEndpoint:Enabled"] = "true",
            })).ListenerPorts.Order());

    private static Dictionary<string, string?> HttpsProfile(Dictionary<string, string?> extraValues)
    {
        var values = new Dictionary<string, string?>(extraValues);

        foreach (var (key, value) in new Dictionary<string, string?>
        {
            ["McpEndpoint:Enabled"] = "true",
            ["McpEndpoint:Transport"] = "HttpsOnly",
            ["McpEndpoint:Https:Endpoints:0:Name"] = "public",
            ["McpEndpoint:Https:Endpoints:0:Domain"] = "mail.example.test",
            ["McpEndpoint:Https:Endpoints:0:ServerCertificate:Bundle:Name"] = "bundle",
            ["McpEndpoint:Https:Endpoints:0:ServerCertificate:Bundle:SecretReference"] = "file:/etc/mailfathom/tls/bundle.pfx",
        })
        {
            // The caller's own values win, so a test states the one key it is about and inherits a usable profile
            // around it.
            values.TryAdd(key, value);
        }

        return values;
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
