// Copyright © 2026 Krzysztof Kasprowicz

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using MailMcp.TestSupport;
using Xunit;

namespace MailMcp.SharedSources.UnitTests;

/// <summary>
/// Proves the shared certificate builder, because every trust decision the mail and MCP suites assert is asserted
/// against what this issues. A certificate that quietly carried the wrong usage, the wrong name, or the wrong validity
/// would let a validator pass its tests while accepting what it must refuse, and the failure would show up nowhere.
/// </summary>
public sealed class TestCertificatesTests
{
    private const string ClientAuthenticationOid = "1.3.6.1.5.5.7.3.2";

    private const string ServerAuthenticationOid = "1.3.6.1.5.5.7.3.1";

    private const string DnsName = "client.example.test";

    [Fact]
    public void CreateCertificateAuthority_AnAuthority_CanIssueAndIsMarkedAsOne()
    {
        // Arrange, Act
        using var authority = TestCertificates.CreateCertificateAuthority("Test Root");

        // Assert
        Assert.True(authority.HasPrivateKey);
        var basicConstraints = Assert.Single(authority.Extensions.OfType<X509BasicConstraintsExtension>());
        Assert.True(basicConstraints.CertificateAuthority);
    }

    [Fact]
    public void IssueClientAuthenticationCertificate_AnIssuedCertificate_ChainsToItsAuthorityAndNamesItsClient()
    {
        // Arrange
        using var authority = TestCertificates.CreateCertificateAuthority("Test Root");

        // Act
        using var certificate = TestCertificates.IssueClientAuthenticationCertificate(authority, DnsName);

        // Assert
        Assert.Equal(authority.Subject, certificate.Issuer);
        Assert.False(certificate.HasPrivateKey);
        Assert.Equal([ClientAuthenticationOid], ExtendedKeyUsagesOf(certificate));
        Assert.Equal([DnsName], DnsNamesOf(certificate));
    }

    /// <summary>The same authority issues both kinds, which is the whole reason a validator has to tell them apart.</summary>
    [Fact]
    public void IssueServerAuthenticationCertificate_AnIssuedCertificate_IsLimitedToServerAuthentication()
    {
        // Arrange
        using var authority = TestCertificates.CreateCertificateAuthority("Test Root");

        // Act
        using var certificate = TestCertificates.IssueServerAuthenticationCertificate(authority, DnsName);

        // Assert
        Assert.Equal([ServerAuthenticationOid], ExtendedKeyUsagesOf(certificate));
    }

    /// <summary>Absence of the extension means every usage in X.509, so a test for that case needs a certificate that genuinely carries none.</summary>
    [Fact]
    public void IssueServerCertificate_AnIssuedCertificate_CarriesNoExtendedKeyUsageAtAll()
    {
        // Arrange
        using var authority = TestCertificates.CreateCertificateAuthority("Test Root");

        // Act
        using var certificate = TestCertificates.IssueServerCertificate(authority, DnsName);

        // Assert
        Assert.Empty(ExtendedKeyUsagesOf(certificate));
        Assert.Equal([DnsName], DnsNamesOf(certificate));
    }

    /// <summary>Chain building compares against the system clock, so the validity is a fixed instant well in the past rather than an offset from now.</summary>
    [Fact]
    public void IssueExpiredClientAuthenticationCertificate_AnIssuedCertificate_IsAlreadyOutOfItsValidityPeriod()
    {
        // Arrange
        using var authority = TestCertificates.CreateCertificateAuthority("Test Root");

        // Act
        using var certificate = TestCertificates.IssueExpiredClientAuthenticationCertificate(authority, DnsName);

        // Assert
        Assert.Equal(
            new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            certificate.NotAfter.ToUniversalTime());
        Assert.Equal([ClientAuthenticationOid], ExtendedKeyUsagesOf(certificate));
    }

    /// <summary>A trust anchor a deployment provisions carries no private key, and a loader that refuses one needs material that has none.</summary>
    [Fact]
    public void WithoutPrivateKey_AnAuthority_KeepsItsIdentityAndDropsItsKey()
    {
        // Arrange
        using var authority = TestCertificates.CreateCertificateAuthority("Test Root");

        // Act
        using var publicAnchor = TestCertificates.WithoutPrivateKey(authority);

        // Assert
        Assert.False(publicAnchor.HasPrivateKey);
        Assert.Equal(authority.Thumbprint, publicAnchor.Thumbprint);
    }

    [Fact]
    public void ToPemAndToDer_AnAuthority_ProduceMaterialThatLoadsBackAsTheSameCertificate()
    {
        // Arrange
        using var authority = TestCertificates.CreateCertificateAuthority("Test Root");
        using var publicAnchor = TestCertificates.WithoutPrivateKey(authority);

        // Act
        using var fromPem = X509CertificateLoader.LoadCertificate(TestCertificates.ToPem(publicAnchor));
        using var fromDer = X509CertificateLoader.LoadCertificate(TestCertificates.ToDer(publicAnchor));

        // Assert
        Assert.Equal(authority.Thumbprint, fromPem.Thumbprint);
        Assert.Equal(authority.Thumbprint, fromDer.Thumbprint);
    }

    [Fact]
    public void ToBundle_AProtectedBundle_OpensWithThePasswordItWasBuiltWith()
    {
        // Arrange
        using var authority = TestCertificates.CreateCertificateAuthority("Test Root");
        using var publicAnchor = TestCertificates.WithoutPrivateKey(authority);

        // Act
        var bundle = TestCertificates.ToBundle(publicAnchor, "bundle-password");

        // Assert
        using var opened = X509CertificateLoader.LoadPkcs12(
            bundle,
            "bundle-password",
            X509KeyStorageFlags.EphemeralKeySet);
        Assert.Equal(authority.Thumbprint, opened.Thumbprint);
    }

    [Fact]
    public void IssueIntermediateAuthority_AnIntermediate_IsAnAuthorityUnderItsIssuerAndCanIssueInTurn()
    {
        // Arrange
        using var rootAuthority = TestCertificates.CreateCertificateAuthority("Test Root");

        // Act
        using var intermediate = TestCertificates.IssueIntermediateAuthority(rootAuthority, "Test Intermediate");
        using var certificate = TestCertificates.IssueClientAuthenticationCertificate(intermediate, DnsName);

        // Assert
        Assert.True(intermediate.HasPrivateKey);
        Assert.Equal(rootAuthority.Subject, intermediate.Issuer);
        Assert.Equal(intermediate.Subject, certificate.Issuer);
    }

    /// <summary>Serial numbers must be unique per issuer, or a chain built from two of them is not the chain the test described.</summary>
    [Fact]
    public void IssueClientAuthenticationCertificate_TwoCertificatesFromOneAuthority_CarryDifferentSerialNumbers()
    {
        // Arrange
        using var authority = TestCertificates.CreateCertificateAuthority("Test Root");

        // Act
        using var first = TestCertificates.IssueClientAuthenticationCertificate(authority, DnsName);
        using var second = TestCertificates.IssueClientAuthenticationCertificate(authority, DnsName);

        // Assert
        Assert.NotEqual(first.SerialNumber, second.SerialNumber);
    }

    private static IReadOnlyList<string> ExtendedKeyUsagesOf(X509Certificate2 certificate) =>
    [
        .. certificate.Extensions
            .OfType<X509EnhancedKeyUsageExtension>()
            .SelectMany(extension => extension.EnhancedKeyUsages.OfType<Oid>())
            .Select(usage => usage.Value)
            .OfType<string>(),
    ];

    private static IReadOnlyList<string> DnsNamesOf(X509Certificate2 certificate) =>
    [
        .. certificate.Extensions
            .Where(extension => extension.Oid?.Value == "2.5.29.17")
            .SelectMany(extension => new X509SubjectAlternativeNameExtension(
                extension.RawData,
                extension.Critical).EnumerateDnsNames()),
    ];
}
