// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Certificates;
using MailFathom.Infrastructure.Secrets.Discovery;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Certificates;

/// <summary>Covers the shape a certificate block must have before anything tries to load what it names.</summary>
/// <remarks>
/// Every listener MailFathom terminates TLS on provisions its identity through this block, so the rule lives with the
/// type rather than with either endpoint that binds it. The loader answers the same question against real material and
/// reports it as <see cref="CertificateMaterialFailure" />; what this adds is the configuration path, reported during
/// composition before a secret is fetched.
/// </remarks>
public sealed class TlsServerCertificateOptionsTests
{
    private const string SectionPath = "McpEndpoint:Https:Endpoints:0:ServerCertificate";

    [Fact]
    public void FindConfigurationErrors_ABundle_IsComplete()
    {
        // Arrange
        var material = new TlsServerCertificateOptions { Bundle = Reference("bundle") };

        // Act
        var errors = material.FindConfigurationErrors(SectionPath);

        // Assert
        Assert.Empty(errors);
        Assert.True(material.IsConfigured);
    }

    [Fact]
    public void FindConfigurationErrors_AChainBesideItsPrivateKey_IsComplete()
    {
        // Arrange
        var material = new TlsServerCertificateOptions
        {
            CertificateChain = Reference("chain"),
            PrivateKey = Reference("key"),
        };

        // Act, Assert
        Assert.Empty(material.FindConfigurationErrors(SectionPath));
    }

    /// <summary>
    /// Which of the two states the identity would otherwise be decided by a precedence rule nobody would remember, so
    /// both kinds together is a refusal rather than a resolution.
    /// </summary>
    [Fact]
    public void FindConfigurationErrors_ABundleBesidePemMaterial_IsRefusedAsAmbiguous()
    {
        // Arrange
        var material = new TlsServerCertificateOptions
        {
            Bundle = Reference("bundle"),
            PrivateKey = Reference("key"),
        };

        // Act
        var error = Assert.Single(material.FindConfigurationErrors(SectionPath));

        // Assert
        Assert.StartsWith(SectionPath, error, StringComparison.Ordinal);
        Assert.Contains("state one or the other", error, StringComparison.Ordinal);
    }

    [Fact]
    public void FindConfigurationErrors_NoMaterialAtAll_IsRefusedWithoutOfferingAFallback()
    {
        // Arrange
        var material = new TlsServerCertificateOptions();

        // Act
        var error = Assert.Single(material.FindConfigurationErrors(SectionPath));

        // Assert
        Assert.Contains("no development-certificate fallback", error, StringComparison.Ordinal);
        Assert.False(material.IsConfigured);
    }

    [Fact]
    public void FindConfigurationErrors_APrivateKeyWithNoChain_NamesTheMissingChain()
    {
        // Arrange
        var material = new TlsServerCertificateOptions { PrivateKey = Reference("key") };

        // Act
        var error = Assert.Single(material.FindConfigurationErrors(SectionPath));

        // Assert
        Assert.StartsWith($"{SectionPath}:{nameof(TlsServerCertificateOptions.CertificateChain)}", error, StringComparison.Ordinal);
    }

    [Fact]
    public void FindConfigurationErrors_AChainWithNoPrivateKey_NamesTheMissingKey()
    {
        // Arrange
        var material = new TlsServerCertificateOptions { CertificateChain = Reference("chain") };

        // Act
        var error = Assert.Single(material.FindConfigurationErrors(SectionPath));

        // Assert
        Assert.StartsWith($"{SectionPath}:{nameof(TlsServerCertificateOptions.PrivateKey)}", error, StringComparison.Ordinal);
    }

    /// <summary>A block whose parts are declared without a reference names nothing, which is what an operator who deleted a value leaves behind.</summary>
    [Fact]
    public void IsConfigured_BlocksWithoutAReference_NameNoMaterial()
    {
        // Arrange
        var material = new TlsServerCertificateOptions
        {
            Bundle = new ConfiguredSecret { Name = "bundle", SecretReference = "   " },
        };

        // Act, Assert
        Assert.False(material.IsConfigured);
    }

    private static ConfiguredSecret Reference(string name) =>
        new() { Name = name, SecretReference = $"file:/etc/mailfathom/tls/{name}" };
}
