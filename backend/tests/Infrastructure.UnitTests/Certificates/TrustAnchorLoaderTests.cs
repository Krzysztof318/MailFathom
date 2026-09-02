// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Infrastructure.Certificates;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.Infrastructure.Secrets.Sources;
using MailFathom.Infrastructure.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Certificates;

public sealed class TrustAnchorLoaderTests
{
    [Fact]
    public async Task LoadAsync_PemMaterial_LoadsTheAnchor()
    {
        // Arrange
        using var authority = TestCertificates.CreateCertificateAuthority("MailFathom Test Root");
        var resolver = new ProvisionedMaterialResolver();
        resolver.Provision("file:/run/secrets/private-ca.pem", TestCertificates.ToPem(authority));

        // Act
        using var result = await new TrustAnchorLoader(resolver).LoadAsync(
            Reference("file:/run/secrets/private-ca.pem"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result.Failure);
        Assert.Equal(authority.Thumbprint, result.TrustAnchor!.Thumbprint);
    }

    [Fact]
    public async Task LoadAsync_DerMaterial_LoadsTheAnchor()
    {
        // Arrange
        using var authority = TestCertificates.CreateCertificateAuthority("MailFathom Test Root");
        var resolver = new ProvisionedMaterialResolver();
        resolver.Provision("file:/run/secrets/private-ca.der", TestCertificates.ToDer(authority));

        // Act
        using var result = await new TrustAnchorLoader(resolver).LoadAsync(
            Reference("file:/run/secrets/private-ca.der"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(authority.Thumbprint, result.TrustAnchor!.Thumbprint);
    }

    [Fact]
    public async Task LoadAsync_ProtectedBundle_TakesItsPasswordFromTheNestedSecretBlock()
    {
        // Arrange
        using var authority = TestCertificates.CreateCertificateAuthority("MailFathom Test Root");
        using var publicAnchor = TestCertificates.WithoutPrivateKey(authority);
        var resolver = new ProvisionedMaterialResolver();
        resolver.Provision("file:/run/secrets/private-ca.pfx", TestCertificates.ToBundle(publicAnchor, "bundle-password"));
        resolver.Provision("file:/run/secrets/bundle-password", Encoding.UTF8.GetBytes("bundle-password"));

        var configuredMaterial = Reference("file:/run/secrets/private-ca.pfx");
        configuredMaterial.Password = Reference("file:/run/secrets/bundle-password");

        // Act
        using var result = await new TrustAnchorLoader(resolver).LoadAsync(
            configuredMaterial,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result.Failure);
        Assert.Equal(authority.Thumbprint, result.TrustAnchor!.Thumbprint);
    }

    /// <summary>An unprotected bundle is a file an operator is entitled to use, so a password block must not be mandatory.</summary>
    [Fact]
    public async Task LoadAsync_UnprotectedBundleWithNoPasswordBlock_LoadsTheAnchor()
    {
        // Arrange
        using var authority = TestCertificates.CreateCertificateAuthority("MailFathom Test Root");
        using var publicAnchor = TestCertificates.WithoutPrivateKey(authority);
        var resolver = new ProvisionedMaterialResolver();
        resolver.Provision("file:/run/secrets/private-ca.pfx", TestCertificates.ToBundle(publicAnchor));

        // Act
        using var result = await new TrustAnchorLoader(resolver).LoadAsync(
            Reference("file:/run/secrets/private-ca.pfx"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(authority.Thumbprint, result.TrustAnchor!.Thumbprint);
    }

    [Fact]
    public async Task LoadAsync_ProtectedBundleWithTheWrongPassword_NamesTheConfiguredPasswordWithoutThrowing()
    {
        // Arrange
        using var authority = TestCertificates.CreateCertificateAuthority("MailFathom Test Root");
        using var publicAnchor = TestCertificates.WithoutPrivateKey(authority);
        var resolver = new ProvisionedMaterialResolver();
        resolver.Provision("file:/run/secrets/private-ca.pfx", TestCertificates.ToBundle(publicAnchor, "bundle-password"));
        resolver.Provision("file:/run/secrets/bundle-password", Encoding.UTF8.GetBytes("the-wrong-password"));

        var configuredMaterial = Reference("file:/run/secrets/private-ca.pfx");
        configuredMaterial.Password = Reference("file:/run/secrets/bundle-password");

        // Act
        using var result = await new TrustAnchorLoader(resolver).LoadAsync(
            configuredMaterial,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result.TrustAnchor);
        Assert.Equal(CertificateMaterialFailure.BundlePasswordIncorrect, result.Failure);
    }

    /// <summary>An operator who supplied no password reads that the bundle wanted one, not that it is corrupt.</summary>
    [Fact]
    public async Task LoadAsync_ProtectedBundleWithNoPasswordBlock_ReportsTheMissingBundlePassword()
    {
        // Arrange
        using var authority = TestCertificates.CreateCertificateAuthority("MailFathom Test Root");
        using var publicAnchor = TestCertificates.WithoutPrivateKey(authority);
        var resolver = new ProvisionedMaterialResolver();
        resolver.Provision("file:/run/secrets/private-ca.pfx", TestCertificates.ToBundle(publicAnchor, "bundle-password"));

        // Act
        using var result = await new TrustAnchorLoader(resolver).LoadAsync(
            Reference("file:/run/secrets/private-ca.pfx"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CertificateMaterialFailure.BundlePasswordMissing, result.Failure);
    }

    /// <summary>Binary material has no faithful representation in a configuration value, so the encoding is the reason.</summary>
    [Fact]
    public async Task LoadAsync_InlineDerMaterial_IsRejectedForItsEncodingRatherThanAsAParseFailure()
    {
        // Arrange
        using var authority = TestCertificates.CreateCertificateAuthority("MailFathom Test Root");
        var resolver = new ProvisionedMaterialResolver();
        resolver.Provision("inline:anchor", TestCertificates.ToDer(authority), SecretMaterialSource.InlineValue);

        // Act
        using var result = await new TrustAnchorLoader(resolver).LoadAsync(
            Reference("inline:anchor"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CertificateMaterialFailure.InlineEncodingNotSupported, result.Failure);
    }

    [Fact]
    public async Task LoadAsync_InlineBundleMaterial_IsRejectedForItsEncoding()
    {
        // Arrange
        using var authority = TestCertificates.CreateCertificateAuthority("MailFathom Test Root");
        using var publicAnchor = TestCertificates.WithoutPrivateKey(authority);
        var resolver = new ProvisionedMaterialResolver();
        resolver.Provision("inline:anchor", TestCertificates.ToBundle(publicAnchor), SecretMaterialSource.InlineValue);

        // Act
        using var result = await new TrustAnchorLoader(resolver).LoadAsync(
            Reference("inline:anchor"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CertificateMaterialFailure.InlineEncodingNotSupported, result.Failure);
    }

    /// <summary>PEM is the one encoding an Azure App Configuration deployment can hand over as the bound value itself.</summary>
    [Fact]
    public async Task LoadAsync_InlinePemMaterial_LoadsTheAnchor()
    {
        // Arrange
        using var authority = TestCertificates.CreateCertificateAuthority("MailFathom Test Root");
        var resolver = new ProvisionedMaterialResolver();
        resolver.Provision("inline:anchor", TestCertificates.ToPem(authority), SecretMaterialSource.InlineValue);

        // Act
        using var result = await new TrustAnchorLoader(resolver).LoadAsync(
            Reference("inline:anchor"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(authority.Thumbprint, result.TrustAnchor!.Thumbprint);
    }

    /// <summary>A trust anchor needs no private key, and one MailFathom holds is an authority MailFathom could impersonate.</summary>
    [Fact]
    public async Task LoadAsync_BundleCarryingAPrivateKey_IsRejected()
    {
        // Arrange
        using var authority = TestCertificates.CreateCertificateAuthority("MailFathom Test Root");
        var resolver = new ProvisionedMaterialResolver();
        resolver.Provision("file:/run/secrets/private-ca.pfx", TestCertificates.ToBundle(authority));

        // Act
        using var result = await new TrustAnchorLoader(resolver).LoadAsync(
            Reference("file:/run/secrets/private-ca.pfx"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result.TrustAnchor);
        Assert.Equal(CertificateMaterialFailure.TrustAnchorCarriesPrivateKey, result.Failure);
    }

    [Fact]
    public async Task LoadAsync_MaterialThatIsNotACertificate_ReportsTheEncodingFailureWithoutThrowing()
    {
        // Arrange
        var resolver = new ProvisionedMaterialResolver();
        resolver.Provision("file:/run/secrets/private-ca.pem", Encoding.UTF8.GetBytes("this is a configuration file"));

        // Act
        using var result = await new TrustAnchorLoader(resolver).LoadAsync(
            Reference("file:/run/secrets/private-ca.pem"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CertificateMaterialFailure.EncodingNotRecognized, result.Failure);
    }

    [Fact]
    public async Task LoadAsync_PemDelimitersAroundSomethingElse_ReportsUnreadableMaterial()
    {
        // Arrange
        var resolver = new ProvisionedMaterialResolver();
        resolver.Provision(
            "file:/run/secrets/private-ca.pem",
            Encoding.UTF8.GetBytes("-----BEGIN CERTIFICATE-----\nbm90IGEgY2VydGlmaWNhdGU=\n-----END CERTIFICATE-----\n"));

        // Act
        using var result = await new TrustAnchorLoader(resolver).LoadAsync(
            Reference("file:/run/secrets/private-ca.pem"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CertificateMaterialFailure.MaterialNotReadable, result.Failure);
    }

    [Fact]
    public async Task LoadAsync_UnresolvableReference_ReportsThatNoMaterialWasRetrieved()
    {
        // Act
        using var result = await new TrustAnchorLoader(new ProvisionedMaterialResolver()).LoadAsync(
            Reference("file:/run/secrets/absent"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CertificateMaterialFailure.SecretNotResolvable, result.Failure);
    }

    [Fact]
    public async Task LoadAsync_UnresolvableBundlePassword_ReportsThatNoMaterialWasRetrieved()
    {
        // Arrange
        using var authority = TestCertificates.CreateCertificateAuthority("MailFathom Test Root");
        using var publicAnchor = TestCertificates.WithoutPrivateKey(authority);
        var resolver = new ProvisionedMaterialResolver();
        resolver.Provision("file:/run/secrets/private-ca.pfx", TestCertificates.ToBundle(publicAnchor, "bundle-password"));

        var configuredMaterial = Reference("file:/run/secrets/private-ca.pfx");
        configuredMaterial.Password = Reference("file:/run/secrets/absent");

        // Act
        using var result = await new TrustAnchorLoader(resolver).LoadAsync(
            configuredMaterial,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CertificateMaterialFailure.SecretNotResolvable, result.Failure);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task LoadAsync_NoConfiguredMaterial_ReportsItAsMissingRatherThanAsAParseFailure(string? secretReference)
    {
        // Arrange
        var configuredMaterial = secretReference is null
            ? null
            : new ConfiguredSecret { SecretReference = secretReference };

        // Act
        using var result = await new TrustAnchorLoader(new ProvisionedMaterialResolver()).LoadAsync(
            configuredMaterial,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CertificateMaterialFailure.MaterialMissing, result.Failure);
    }

    /// <summary>Material lives no longer than the load that parsed it, whether or not the anchor came out of it.</summary>
    [Fact]
    public async Task LoadAsync_Always_ErasesTheResolvedMaterialBeforeReturning()
    {
        // Arrange
        using var authority = TestCertificates.CreateCertificateAuthority("MailFathom Test Root");
        var resolver = new ProvisionedMaterialResolver();
        resolver.Provision("file:/run/secrets/private-ca.pem", TestCertificates.ToPem(authority));

        // Act
        using var result = await new TrustAnchorLoader(resolver).LoadAsync(
            Reference("file:/run/secrets/private-ca.pem"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result.TrustAnchor);
        Assert.All(resolver.IssuedMaterial, issued => Assert.Throws<ObjectDisposedException>(() => issued.RevealBytes().Length));
    }

    private static ConfiguredSecret Reference(string secretReference) => new() { SecretReference = secretReference };
}
