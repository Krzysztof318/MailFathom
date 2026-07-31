// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailFathom.Host.Configuration;
using MailFathom.Infrastructure.Certificates;
using MailFathom.Infrastructure.Secrets;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MailFathom.Host.UnitTests;

/// <summary>Covers the one listener an HTTPS profile cannot displace, and therefore has to refuse to start beside.</summary>
/// <remarks>
/// Kestrel binds the endpoints its own configuration section names as well as the ones the composition root binds in
/// code, and only the URL-shaped addresses are replaced. Without this rule a deployment that configured both would
/// serve the MCP route over the clear-text listener as well as over the profile, which is the state the HTTPS section
/// promises cannot exist.
/// </remarks>
public sealed class ConfiguredKestrelEndpointsTests
{
    [Fact]
    public void FindHttpsProfileConflicts_AConfiguredEndpointBesideAnHttpsProfile_IsRefused()
    {
        // Arrange
        var configuration = ConfigurationWith(("Kestrel:Endpoints:Http:Url", "http://0.0.0.0:8080"));

        // Act
        var error = Assert.Single(ConfiguredKestrelEndpoints.FindHttpsProfileConflicts(
            configuration,
            HttpsProfiles()));

        // Assert
        Assert.StartsWith("Kestrel:Endpoints:Http", error, StringComparison.Ordinal);
        Assert.Contains("Kestrel binds both", error, StringComparison.Ordinal);
    }

    /// <summary>Each configured endpoint binds its own socket, so an operator reads every one they have to remove in a single message.</summary>
    [Fact]
    public void FindHttpsProfileConflicts_SeveralConfiguredEndpoints_ReportsEachOfThem()
    {
        // Arrange
        var configuration = ConfigurationWith(
            ("Kestrel:Endpoints:Http:Url", "http://0.0.0.0:8080"),
            ("Kestrel:Endpoints:Legacy:Url", "https://0.0.0.0:9443"));

        // Act
        var conflicts = ConfiguredKestrelEndpoints.FindHttpsProfileConflicts(configuration, HttpsProfiles());

        // Assert
        Assert.Equal(
            ["Kestrel:Endpoints:Http", "Kestrel:Endpoints:Legacy"],
            conflicts.Select(static conflict => conflict[..conflict.IndexOf(' ', StringComparison.Ordinal)]));
    }

    /// <summary>Without a profile the configured listener is the one serving the endpoint, which is the supported clear-text posture.</summary>
    [Fact]
    public void FindHttpsProfileConflicts_NoHttpsProfile_LeavesTheConfiguredEndpointAlone()
    {
        // Arrange
        var configuration = ConfigurationWith(("Kestrel:Endpoints:Http:Url", "http://0.0.0.0:8080"));

        // Act, Assert
        Assert.Empty(ConfiguredKestrelEndpoints.FindHttpsProfileConflicts(configuration, new McpHttpsOptions()));
    }

    [Fact]
    public void FindHttpsProfileConflicts_NoKestrelSectionAtAll_ReportsNothing()
    {
        // Arrange
        var configuration = ConfigurationWith(("Logging:LogLevel:Default", "Information"));

        // Act, Assert
        Assert.Empty(ConfiguredKestrelEndpoints.FindHttpsProfileConflicts(configuration, HttpsProfiles()));
    }

    /// <summary>An endpoint carrying only defaults binds no socket, so it displaces nothing and conflicts with nothing.</summary>
    [Fact]
    public void FindHttpsProfileConflicts_AConfiguredEndpointWithoutAUrl_ReportsNothing()
    {
        // Arrange
        var configuration = ConfigurationWith(("Kestrel:Endpoints:Http:Protocols", "Http1AndHttp2"));

        // Act, Assert
        Assert.Empty(ConfiguredKestrelEndpoints.FindHttpsProfileConflicts(configuration, HttpsProfiles()));
    }

    private static IConfiguration ConfigurationWith(params (string Key, string Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(static setting =>
                new KeyValuePair<string, string?>(setting.Key, setting.Value)))
            .Build();

    private static McpHttpsOptions HttpsProfiles()
    {
        var options = new McpHttpsOptions();

        options.Endpoints.Add(new McpHttpsEndpointOptions
        {
            Name = "public",
            Domain = "mail.example.test",
            ServerCertificate = new TlsServerCertificateOptions
            {
                Bundle = new ConfiguredSecret
                {
                    Name = "bundle",
                    SecretReference = "file:/etc/mailfathom/tls/bundle.pfx",
                },
            },
        });

        return options;
    }
}
