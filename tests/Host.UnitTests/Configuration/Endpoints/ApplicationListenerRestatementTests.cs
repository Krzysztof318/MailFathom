// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Infrastructure.Certificates;
using MailFathom.Infrastructure.Secrets.Discovery;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Endpoints;

/// <summary>Covers which compositions keep the application's own addresses and which ones would drop them.</summary>
/// <remarks>
/// Binding any listener in code makes Kestrel ignore the URL-shaped addresses, so the question is not which endpoint is
/// being opened but whether any of them is. A composition that answers it from one endpoint's setting serves nothing on
/// the address its clients connect to whenever that endpoint is the one switched off, and starts successfully doing it.
/// </remarks>
public sealed class ApplicationListenerRestatementTests
{
    /// <summary>The administrative endpoint binds its listener whether or not the probes are served, so it is what decides here.</summary>
    [Fact]
    public void IsRequired_TheAdministrativeEndpointAloneBinding_KeepsTheApplicationAddresses()
    {
        // Arrange
        var healthEndpointSettings = new HealthEndpointOptions { Enabled = false };
        var adminEndpointSettings = new AdminEndpointOptions { Enabled = true };

        // Act
        var isRequired = ApplicationListenerRestatement.IsRequired(
            EmptyConfiguration,
            ClearTextMcpEndpoint(),
            healthEndpointSettings,
            adminEndpointSettings);

        // Assert
        Assert.True(isRequired);
    }

    [Fact]
    public void IsRequired_TheProbeEndpointAloneBinding_KeepsTheApplicationAddresses()
    {
        // Arrange
        var healthEndpointSettings = new HealthEndpointOptions { Enabled = true };
        var adminEndpointSettings = new AdminEndpointOptions { Enabled = false };

        // Act
        var isRequired = ApplicationListenerRestatement.IsRequired(
            EmptyConfiguration,
            ClearTextMcpEndpoint(),
            healthEndpointSettings,
            adminEndpointSettings);

        // Assert
        Assert.True(isRequired);
    }

    /// <summary>Nothing opens a socket of its own, so Kestrel binds the addresses itself and restating them would say the same thing twice.</summary>
    [Fact]
    public void IsRequired_NoEndpointBindingAListenerOfItsOwn_RestatesNothing()
    {
        // Arrange
        var healthEndpointSettings = new HealthEndpointOptions { Enabled = false };
        var adminEndpointSettings = new AdminEndpointOptions { Enabled = false };

        // Act
        var isRequired = ApplicationListenerRestatement.IsRequired(
            EmptyConfiguration,
            ClearTextMcpEndpoint(),
            healthEndpointSettings,
            adminEndpointSettings);

        // Assert
        Assert.False(isRequired);
    }

    /// <summary>
    /// The HTTPS profiles replace the application listener rather than joining it. Restating the addresses beside them
    /// would reopen the clear-text socket the profiles exist to close.
    /// </summary>
    [Fact]
    public void IsRequired_TheMcpEndpointTerminatingTls_RestatesNothingEvenWhileAnotherEndpointBinds()
    {
        // Arrange
        var healthEndpointSettings = new HealthEndpointOptions { Enabled = true };
        var adminEndpointSettings = new AdminEndpointOptions { Enabled = true };

        // Act
        var isRequired = ApplicationListenerRestatement.IsRequired(
            EmptyConfiguration,
            TlsTerminatingMcpEndpoint(enabled: true),
            healthEndpointSettings,
            adminEndpointSettings);

        // Assert
        Assert.False(isRequired);
    }

    /// <summary>A profile on an endpoint nobody serves binds no listener, so the addresses are still the ones the process answers on.</summary>
    [Fact]
    public void IsRequired_HttpsProfilesOnADisabledMcpEndpoint_KeepsTheApplicationAddresses()
    {
        // Arrange
        var healthEndpointSettings = new HealthEndpointOptions { Enabled = false };
        var adminEndpointSettings = new AdminEndpointOptions { Enabled = true };

        // Act
        var isRequired = ApplicationListenerRestatement.IsRequired(
            EmptyConfiguration,
            TlsTerminatingMcpEndpoint(enabled: false),
            healthEndpointSettings,
            adminEndpointSettings);

        // Assert
        Assert.True(isRequired);
    }

    /// <summary>A deployment naming its own endpoints is one whose URL-shaped addresses Kestrel was already ignoring, and reinstating them would open a listener the operator had replaced.</summary>
    [Fact]
    public void IsRequired_ADeploymentNamingItsOwnKestrelEndpoints_RestatesNothing()
    {
        // Arrange
        var configuration = ConfigurationFrom(new Dictionary<string, string?>
        {
            ["Kestrel:Endpoints:Application:Url"] = "http://0.0.0.0:8080",
        });

        var healthEndpointSettings = new HealthEndpointOptions { Enabled = false };
        var adminEndpointSettings = new AdminEndpointOptions { Enabled = true };

        // Act
        var isRequired = ApplicationListenerRestatement.IsRequired(
            configuration,
            ClearTextMcpEndpoint(),
            healthEndpointSettings,
            adminEndpointSettings);

        // Assert
        Assert.False(isRequired);
    }

    private static IConfiguration EmptyConfiguration => new ConfigurationBuilder().Build();

    private static McpEndpointOptions ClearTextMcpEndpoint() => new() { Enabled = true };

    private static McpEndpointOptions TlsTerminatingMcpEndpoint(bool enabled)
    {
        var settings = new McpEndpointOptions { Enabled = enabled };
        settings.Https.Endpoints.Add(new TransportHttpsEndpointOptions
        {
            Name = "mcp",
            Domain = "mcp.example.test",
            Port = 8443,
            ServerCertificate = new TlsServerCertificateOptions
            {
                Bundle = new ConfiguredSecret { Name = "bundle", SecretReference = "file:/etc/mailfathom/tls/mcp.pfx" },
            },
        });

        return settings;
    }

    private static IConfiguration ConfigurationFrom(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
