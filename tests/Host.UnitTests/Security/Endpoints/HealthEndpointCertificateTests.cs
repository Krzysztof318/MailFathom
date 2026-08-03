// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography.X509Certificates;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Security.Endpoints;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Certificates;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Host.UnitTests.Security.Endpoints;

/// <summary>Covers the identity the probe listener presents, and what happens when the deployment provisioned none.</summary>
/// <remarks>
/// A TLS transport never downgrades. Material that is missing, unresolvable, expired, or unusable fails startup with
/// nothing listening, because a probe answering on a port an operator believed was TLS is worse than one that does not
/// answer at all.
/// </remarks>
public sealed class HealthEndpointCertificateTests
{
    private const string Domain = "probe.example.test";

    private static readonly DateTimeOffset Now = new(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task LoadAsync_MaterialCoveringTheDomain_PresentsAnIdentityCarryingItsPrivateKey()
    {
        // Arrange
        using var identity = TestCertificates.CreateServerIdentity(
            [Domain],
            Now.AddDays(-1),
            Now.AddDays(200));

        using var certificate = CertificateFor(TlsOptions(identity));

        // Act
        await certificate.LoadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(certificate.Leaf.HasPrivateKey);
        Assert.Empty(certificate.Chain);
    }

    [Fact]
    public async Task LoadAsync_MaterialThatIsNotUsable_FailsStartupRatherThanServingClearText()
    {
        // Arrange
        var settings = new HealthEndpointOptions
        {
            Transport = HealthEndpointTransport.HttpsOnly,
            Domain = Domain,
            ServerCertificate = new TlsServerCertificateOptions
            {
                Bundle = new ConfiguredSecret { Name = "probe-bundle", SecretReference = "file:/run/secrets/probe.pfx" },
            },
        };

        using var certificate = CertificateFor(settings);

        // Act
        var loading = async () => await certificate.LoadAsync(TestContext.Current.CancellationToken);

        // Assert
        var failure = await Assert.ThrowsAsync<OptionsValidationException>(loading);

        Assert.Contains(failure.Failures, message => message.Contains("HealthEndpoints:ServerCertificate", StringComparison.Ordinal));
    }

    /// <summary>
    /// A certificate outside its validity period is refused by the loader rather than presented, which is what makes an
    /// expired one a startup failure instead of a handshake every orchestrator meets afterwards.
    /// </summary>
    [Fact]
    public async Task LoadAsync_ExpiredMaterial_FailsStartup()
    {
        // Arrange
        using var identity = TestCertificates.CreateServerIdentity(
            [Domain],
            Now.AddDays(-400),
            Now.AddDays(-1));

        using var certificate = CertificateFor(TlsOptions(identity));

        // Act
        var loading = async () => await certificate.LoadAsync(TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<OptionsValidationException>(loading);
    }

    [Fact]
    public async Task LoadAsync_AClearTextTransport_LoadsNothing()
    {
        // Arrange
        using var certificate = CertificateFor(new HealthEndpointOptions());

        // Act
        await certificate.LoadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Throws<InvalidOperationException>(() => certificate.Leaf);
    }

    [Fact]
    public async Task LoadAsync_DisabledProbes_LoadNothingEvenUnderATlsTransport()
    {
        // Arrange
        using var identity = TestCertificates.CreateServerIdentity([Domain], Now.AddDays(-1), Now.AddDays(200));
        var settings = TlsOptions(identity);
        settings.Enabled = false;

        using var certificate = CertificateFor(settings);

        // Act
        await certificate.LoadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Throws<InvalidOperationException>(() => certificate.Leaf);
    }

    private static HealthEndpointOptions TlsOptions(X509Certificate2 identity) =>
        new()
        {
            Transport = HealthEndpointTransport.HttpsOnly,
            Domain = Domain,
            ServerCertificate = new TlsServerCertificateOptions
            {
                CertificateChain = new ConfiguredSecret
                {
                    Name = "probe-chain",
                    SecretReference = $"plaintext:{TestCertificates.ToCertificateChainPem(identity)}",
                },
                PrivateKey = new ConfiguredSecret
                {
                    Name = "probe-key",
                    SecretReference = $"plaintext:{TestCertificates.ToPrivateKeyPem(identity)}",
                },
            },
        };

    private static HealthEndpointCertificate CertificateFor(HealthEndpointOptions settings) =>
        new(
            Options.Create(settings),
            new TlsServerCertificateLoader(new PlaintextOnlySecretReferenceResolver(), new FakeTimeProvider(Now)),
            new FakeTimeProvider(Now),
            NullLogger<HealthEndpointCertificate>.Instance);
}
