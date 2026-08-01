// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailFathom.Host.Configuration;
using MailFathom.Infrastructure.Certificates;
using MailFathom.Infrastructure.Secrets;
using Xunit;

namespace MailFathom.Host.UnitTests;

/// <summary>Covers what an operator may configure about the probe listener, and what fails startup instead of binding.</summary>
/// <remarks>
/// Every rule here refuses a configuration that would otherwise produce a listener nobody asked for: a probe port
/// serving the mailbox's clients, a TLS mode with nothing to present, or a value the binder accepted and no member
/// declares. A socket bound under any of those is one an operator believes is something it is not.
/// </remarks>
public sealed class HealthEndpointOptionsTests
{
    private static readonly int[] ApplicationPorts = [8080];

    [Fact]
    public void Defaults_AnUnconfiguredSection_ServesThreeProbesOnItsOwnClearTextPort()
    {
        // Arrange
        var options = new HealthEndpointOptions();

        // Act
        var errors = options.FindConfigurationErrors(ApplicationPorts);

        // Assert
        Assert.True(options.Enabled);
        Assert.Equal(8081, options.Port);
        Assert.Equal("0.0.0.0", options.BindAddress);
        Assert.Equal(HealthEndpointTransport.Http, options.Transport);
        Assert.Null(options.HttpsPort);
        Assert.Equal([8081], options.ListenerPorts);
        Assert.Empty(errors);
    }

    /// <summary>
    /// The disabled state is the one in which nothing about the probes exists: no route to reach, no socket to publish,
    /// and no configuration left to be wrong.
    /// </summary>
    [Fact]
    public void ListenerPorts_ADisabledSection_OpensNoListenerAndRefusesNothing()
    {
        // Arrange
        var options = new HealthEndpointOptions
        {
            Enabled = false,
            Port = 8080,
            Transport = HealthEndpointTransport.HttpsOnly,
        };

        // Act
        var errors = options.FindConfigurationErrors(ApplicationPorts);

        // Assert
        Assert.Empty(options.ListenerPorts);
        Assert.Empty(errors);
    }

    [Fact]
    public void ListenerPorts_ServingBothSchemes_AnswersOnBothPorts()
    {
        // Arrange
        var options = TlsOptions(HealthEndpointTransport.HttpAndHttps);
        options.HttpsPort = 8443;

        // Act
        var listenerPorts = options.ListenerPorts;

        // Assert
        Assert.Equal([8081, 8443], listenerPorts.Order());
        Assert.Empty(options.FindConfigurationErrors(ApplicationPorts));
    }

    [Fact]
    public void FindConfigurationErrors_TlsOnly_OpensNoClearTextListener()
    {
        // Arrange
        var options = TlsOptions(HealthEndpointTransport.HttpsOnly);

        // Act
        var errors = options.FindConfigurationErrors(ApplicationPorts);

        // Assert
        Assert.False(options.ServesClearText);
        Assert.True(options.TerminatesTls);
        Assert.Equal([8081], options.ListenerPorts);
        Assert.Equal(8081, options.TlsListenerPort);
        Assert.Empty(errors);
    }

    /// <summary>
    /// One socket serves one scheme, so which port carries TLS is what the transport decides: the configured port under
    /// the mode that opens no clear-text listener, and the second one under the mode that opens both.
    /// </summary>
    [Fact]
    public void TlsListenerPort_EachTransport_NamesTheSocketTlsIsServedOn()
    {
        // Arrange
        var clearText = new HealthEndpointOptions();
        var tlsOnly = TlsOptions(HealthEndpointTransport.HttpsOnly);
        var bothSchemes = TlsOptions(HealthEndpointTransport.HttpAndHttps);
        bothSchemes.HttpsPort = 8443;

        // Act
        var tlsPorts = new[] { clearText, tlsOnly, bothSchemes }.Select(options => options.TlsListenerPort);

        // Assert
        Assert.Equal([null, 8081, 8443], tlsPorts);
    }

    /// <summary>
    /// A port an operator published and nothing binds is a deployment believing its probes answer somewhere they do
    /// not, which is the same failure a certificate nothing presents produces.
    /// </summary>
    [Fact]
    public void FindConfigurationErrors_ASecondPortUnderAClearTextTransport_IsRefused()
    {
        // Arrange
        var options = new HealthEndpointOptions { HttpsPort = 8443 };

        // Act
        var errors = options.FindConfigurationErrors(ApplicationPorts);

        // Assert
        Assert.Contains(errors, error => error.Contains("nothing binds it", StringComparison.Ordinal));
    }

    [Fact]
    public void FindConfigurationErrors_ASecondPortUnderTlsOnly_IsRefused()
    {
        // Arrange
        var options = TlsOptions(HealthEndpointTransport.HttpsOnly);
        options.HttpsPort = 8443;

        // Act
        var errors = options.FindConfigurationErrors(ApplicationPorts);

        // Assert
        Assert.Contains(errors, error => error.Contains("nothing binds it", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void FindConfigurationErrors_APortOutsideTheRange_IsRefused(int port)
    {
        // Arrange
        var options = new HealthEndpointOptions { Port = port };

        // Act
        var errors = options.FindConfigurationErrors(ApplicationPorts);

        // Assert
        Assert.Contains(errors, error => error.Contains("HealthEndpoints:Port", StringComparison.Ordinal));
    }

    [Fact]
    public void FindConfigurationErrors_APortTheApplicationListenerBinds_IsRefused()
    {
        // Arrange
        var options = new HealthEndpointOptions { Port = 8080 };

        // Act
        var errors = options.FindConfigurationErrors(ApplicationPorts);

        // Assert
        Assert.Contains(errors, error => error.Contains("already the application listener's", StringComparison.Ordinal));
    }

    [Fact]
    public void FindConfigurationErrors_TheTwoProbePortsColliding_IsRefused()
    {
        // Arrange
        var options = TlsOptions(HealthEndpointTransport.HttpAndHttps);
        options.HttpsPort = options.Port;

        // Act
        var errors = options.FindConfigurationErrors(ApplicationPorts);

        // Assert
        Assert.Contains(errors, error => error.Contains("one socket cannot serve both schemes", StringComparison.Ordinal));
    }

    [Fact]
    public void FindConfigurationErrors_BothSchemesWithNoTlsPort_IsRefused()
    {
        // Arrange
        var options = TlsOptions(HealthEndpointTransport.HttpAndHttps);

        // Act
        var errors = options.FindConfigurationErrors(ApplicationPorts);

        // Assert
        Assert.Contains(errors, error => error.Contains("HealthEndpoints:HttpsPort", StringComparison.Ordinal));
    }

    /// <summary>
    /// The binder accepts any number for an enum, so a value naming no transport would otherwise decide the posture by
    /// falling through every check that asks which one it is.
    /// </summary>
    [Fact]
    public void FindConfigurationErrors_ATransportNoMemberDeclares_IsRefused()
    {
        // Arrange
        var options = new HealthEndpointOptions { Transport = (HealthEndpointTransport)7 };

        // Act
        var errors = options.FindConfigurationErrors(ApplicationPorts);

        // Assert
        Assert.Contains(errors, error => error.Contains("names no transport", StringComparison.Ordinal));
    }

    [Fact]
    public void FindConfigurationErrors_ABindAddressThatIsNotAnIpAddress_IsRefused()
    {
        // Arrange
        var options = new HealthEndpointOptions { BindAddress = "probe.example.test" };

        // Act
        var errors = options.FindConfigurationErrors(ApplicationPorts);

        // Assert
        Assert.Contains(errors, error => error.Contains("HealthEndpoints:BindAddress", StringComparison.Ordinal));
    }

    [Fact]
    public void FindConfigurationErrors_ALoopbackBindAddress_IsAccepted()
    {
        // Arrange
        var options = new HealthEndpointOptions { BindAddress = "127.0.0.1" };

        // Act
        var errors = options.FindConfigurationErrors(ApplicationPorts);

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public void FindConfigurationErrors_ATlsTransportWithNoCertificate_IsRefused()
    {
        // Arrange
        var options = new HealthEndpointOptions
        {
            Transport = HealthEndpointTransport.HttpsOnly,
            Domain = "probe.example.test",
        };

        // Act
        var errors = options.FindConfigurationErrors(ApplicationPorts);

        // Assert
        Assert.Contains(errors, error => error.Contains("no development-certificate fallback", StringComparison.Ordinal));
    }

    [Fact]
    public void FindConfigurationErrors_ATlsTransportWithNoDomain_IsRefused()
    {
        // Arrange
        var options = TlsOptions(HealthEndpointTransport.HttpsOnly);
        options.Domain = "   ";

        // Act
        var errors = options.FindConfigurationErrors(ApplicationPorts);

        // Assert
        Assert.Contains(errors, error => error.Contains("HealthEndpoints:Domain", StringComparison.Ordinal));
    }

    /// <summary>
    /// An orchestrator dials this listener by address, so an IP address is the mistake an operator is most likely to
    /// make here — and a certificate is matched against DNS subject alternative names, which never carry one.
    /// </summary>
    [Theory]
    [InlineData("10.0.0.5")]
    [InlineData("*.example.test")]
    public void FindConfigurationErrors_ADomainThatIsNotADnsName_IsRefused(string domain)
    {
        // Arrange
        var options = TlsOptions(HealthEndpointTransport.HttpsOnly);
        options.Domain = domain;

        // Act
        var errors = options.FindConfigurationErrors(ApplicationPorts);

        // Assert
        Assert.Contains(errors, error => error.StartsWith("HealthEndpoints:Domain", StringComparison.Ordinal));
    }

    [Fact]
    public void FindConfigurationErrors_APrivateKeyWithNoCertificate_IsRefused()
    {
        // Arrange
        var options = new HealthEndpointOptions
        {
            Transport = HealthEndpointTransport.HttpsOnly,
            Domain = "probe.example.test",
            ServerCertificate = new TlsServerCertificateOptions
            {
                PrivateKey = new ConfiguredSecret { Name = "probe-key", SecretReference = "file:/run/secrets/probe-key" },
            },
        };

        // Act
        var errors = options.FindConfigurationErrors(ApplicationPorts);

        // Assert
        Assert.Contains(errors, error => error.Contains("CertificateChain", StringComparison.Ordinal));
    }

    /// <summary>
    /// Material nothing presents is a deployment believing it configured TLS, which is worse than one that knows it did
    /// not.
    /// </summary>
    [Fact]
    public void FindConfigurationErrors_ACertificateUnderAClearTextTransport_IsRefused()
    {
        // Arrange
        var options = new HealthEndpointOptions
        {
            ServerCertificate = new TlsServerCertificateOptions
            {
                Bundle = new ConfiguredSecret { Name = "probe-bundle", SecretReference = "file:/run/secrets/probe.pfx" },
            },
        };

        // Act
        var errors = options.FindConfigurationErrors(ApplicationPorts);

        // Assert
        Assert.Contains(errors, error => error.Contains("opens no TLS listener", StringComparison.Ordinal));
    }

    [Fact]
    public void FindConfigurationErrors_BothKindsOfMaterial_IsRefused()
    {
        // Arrange
        var options = TlsOptions(HealthEndpointTransport.HttpsOnly);
        options.ServerCertificate.PrivateKey = new ConfiguredSecret
        {
            Name = "probe-key",
            SecretReference = "file:/run/secrets/probe-key",
        };

        // Act
        var errors = options.FindConfigurationErrors(ApplicationPorts);

        // Assert
        Assert.Contains(errors, error => error.Contains("state one or the other", StringComparison.Ordinal));
    }

    private static HealthEndpointOptions TlsOptions(HealthEndpointTransport transport) =>
        new()
        {
            Transport = transport,
            Domain = "probe.example.test",
            ServerCertificate = new TlsServerCertificateOptions
            {
                Bundle = new ConfiguredSecret { Name = "probe-bundle", SecretReference = "file:/run/secrets/probe.pfx" },
            },
        };
}
