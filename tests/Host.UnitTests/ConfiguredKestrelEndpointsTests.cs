// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

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
        Assert.Empty(ConfiguredKestrelEndpoints.FindHttpsProfileConflicts(configuration, new TransportHttpsOptions()));
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

    /// <summary>
    /// A deployment naming its own endpoints is one whose URL-shaped addresses Kestrel already ignores, which is what
    /// the health-endpoint listener must not undo by restating them beside the endpoints an operator wrote.
    /// </summary>
    [Fact]
    public void AnyConfigured_AConfiguredEndpoint_ReportsThatTheDeploymentNamesItsOwnListeners()
    {
        // Arrange
        var configuration = ConfigurationWith(("Kestrel:Endpoints:Http:Url", "http://0.0.0.0:8080"));

        // Act, Assert
        Assert.True(ConfiguredKestrelEndpoints.AnyConfigured(configuration));
    }

    [Fact]
    public void AnyConfigured_AnEndpointWithoutAUrl_ReportsNoConfiguredListener()
    {
        // Arrange
        var configuration = ConfigurationWith(("Kestrel:Endpoints:Http:Protocols", "Http1AndHttp2"));

        // Act, Assert
        Assert.False(ConfiguredKestrelEndpoints.AnyConfigured(configuration));
    }

    [Fact]
    public void AnyConfigured_NoKestrelSectionAtAll_ReportsNoConfiguredListener()
    {
        // Arrange
        var configuration = ConfigurationWith(("Logging:LogLevel:Default", "Information"));

        // Act, Assert
        Assert.False(ConfiguredKestrelEndpoints.AnyConfigured(configuration));
    }

    /// <summary>
    /// These are the application listener's ports whenever the section is populated, because the URL-shaped addresses
    /// stop binding as soon as it is. A second listener that has to avoid the application's reads them from here, or
    /// its collision check compares against sockets nothing opens.
    /// </summary>
    [Fact]
    public void ListenerPorts_ConfiguredEndpoints_ReadsThePortsTheyBind()
    {
        // Arrange
        var configuration = ConfigurationWith(
            ("Kestrel:Endpoints:Http:Url", "http://127.0.0.1:8081"),
            ("Kestrel:Endpoints:Https:Url", "https://0.0.0.0:8443"),
            ("Kestrel:Endpoints:Defaults:Protocols", "Http1AndHttp2"));

        // Act
        var ports = ConfiguredKestrelEndpoints.ListenerPorts(configuration);

        // Assert
        Assert.Equal([8081, 8443], ports);
    }

    [Fact]
    public void ListenerPorts_NoConfiguredEndpoint_ReadsNoPort()
    {
        // Arrange
        var configuration = ConfigurationWith(("Logging:LogLevel:Default", "Information"));

        // Act, Assert
        Assert.Empty(ConfiguredKestrelEndpoints.ListenerPorts(configuration));
    }

    private static IConfiguration ConfigurationWith(params (string Key, string Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(static setting =>
                new KeyValuePair<string, string?>(setting.Key, setting.Value)))
            .Build();

    private static TransportHttpsOptions HttpsProfiles()
    {
        var options = new TransportHttpsOptions();

        options.Endpoints.Add(new TransportHttpsEndpointOptions
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
