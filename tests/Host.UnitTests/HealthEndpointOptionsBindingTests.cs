// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MailFathom.Host.UnitTests;

/// <summary>Covers that the health-endpoint section binds from configuration the way composition reads it.</summary>
/// <remarks>
/// The section decides which sockets a deployment opens and whether they carry TLS, so the difference between a key
/// that bound and a key that was ignored is the difference between the posture an operator wrote and one nobody chose.
/// Strict binding is what turns a misspelling into a startup failure, and that is asserted here rather than assumed.
/// </remarks>
public sealed class HealthEndpointOptionsBindingTests
{
    [Fact]
    public void ReadFrom_AnEmptyConfiguration_ServesTheProbesOnTheDefaultPort()
    {
        // Arrange
        var configuration = ConfigurationFrom([]);

        // Act
        var options = HealthEndpointOptions.ReadFrom(configuration);

        // Assert
        Assert.True(options.Enabled);
        Assert.Equal(8081, options.Port);
        Assert.Equal(HealthEndpointTransport.Http, options.Transport);
    }

    [Fact]
    public void ReadFrom_AConfiguredSection_ReadsEveryDecisionCompositionActsOn()
    {
        // Arrange
        var configuration = ConfigurationFrom(new Dictionary<string, string?>
        {
            ["HealthEndpoints:Enabled"] = "true",
            ["HealthEndpoints:BindAddress"] = "127.0.0.1",
            ["HealthEndpoints:Port"] = "9090",
            ["HealthEndpoints:HttpsPort"] = "9443",
            ["HealthEndpoints:Transport"] = "HttpAndHttps",
            ["HealthEndpoints:Domain"] = "probe.example.test",
            ["HealthEndpoints:ServerCertificate:Bundle:Name"] = "probe-bundle",
            ["HealthEndpoints:ServerCertificate:Bundle:SecretReference"] = "file:/run/secrets/probe.pfx",
        });

        // Act
        var options = HealthEndpointOptions.ReadFrom(configuration);

        // Assert
        Assert.Equal("127.0.0.1", options.BindAddress);
        Assert.Equal(9090, options.Port);
        Assert.Equal(9443, options.HttpsPort);
        Assert.Equal(HealthEndpointTransport.HttpAndHttps, options.Transport);
        Assert.Equal("probe.example.test", options.Domain);
        Assert.Equal("file:/run/secrets/probe.pfx", options.ServerCertificate.Bundle?.SecretReference);
        Assert.Empty(options.FindConfigurationErrors([8080]));
    }

    [Fact]
    public void ReadFrom_ADisabledSection_TurnsTheProbesOff()
    {
        // Arrange
        var configuration = ConfigurationFrom(new Dictionary<string, string?>
        {
            ["HealthEndpoints:Enabled"] = "false",
        });

        // Act
        var options = HealthEndpointOptions.ReadFrom(configuration);

        // Assert
        Assert.False(options.Enabled);
        Assert.Empty(options.ListenerPorts);
    }

    /// <summary>
    /// A misspelled key that bound quietly would leave the probes on a port an operator believed they had moved, which
    /// is a listener published to the wrong network rather than a setting that failed to apply.
    /// </summary>
    [Fact]
    public void ReadFrom_AMisspelledKey_FailsRatherThanBindingTheRest()
    {
        // Arrange
        var configuration = ConfigurationFrom(new Dictionary<string, string?>
        {
            ["HealthEndpoints:Prot"] = "9090",
        });

        // Act
        var readingTheSection = () => HealthEndpointOptions.ReadFrom(configuration);

        // Assert
        Assert.Throws<InvalidOperationException>(readingTheSection);
    }

    private static IConfiguration ConfigurationFrom(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
