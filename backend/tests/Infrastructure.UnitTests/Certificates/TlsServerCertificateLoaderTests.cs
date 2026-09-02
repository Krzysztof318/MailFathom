// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography.X509Certificates;
using MailFathom.Infrastructure.Certificates;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.Infrastructure.Secrets.Sources;
using MailFathom.Infrastructure.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Certificates;

/// <summary>Covers what material becomes a server identity, and what is refused before an endpoint could present it.</summary>
/// <remarks>
/// Every refusal here is one an operator would otherwise meet as a failed handshake in a client they do not control, so
/// the identity of each failure is as much a contract as the acceptance is: it is what turns "the connection was reset"
/// into "the certificate you provisioned expired" or "it is for the other domain".
/// </remarks>
public sealed class TlsServerCertificateLoaderTests
{
    private const string Domain = "mail.example.test";

    private const string BundleReference = "file:/etc/mailfathom/tls/bundle.pfx";

    private const string ChainReference = "file:/etc/mailfathom/tls/fullchain.pem";

    private const string PrivateKeyReference = "file:/etc/mailfathom/tls/privkey.pem";

    private const string PasswordReference = "systemd-credential:mailfathom-tls-password";

    private static readonly DateTimeOffset Now = new(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task LoadAsync_Pkcs12BundleCoveringTheDomain_LoadsAnIdentityCarryingItsPrivateKey()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        using var certificate = ServerCertificateFor(Domain);
        material.Provision(BundleReference, TestCertificates.ToBundleOf(bundlePassword: null, certificate));

        // Act
        using var result = await LoaderFor(material).LoadAsync(BundleOf(), Domain, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result.Failure);
        Assert.True(result.Certificate?.Leaf.HasPrivateKey);
        Assert.Empty(result.Certificate!.Intermediates);
    }

    [Fact]
    public async Task LoadAsync_PasswordProtectedBundle_OpensWithTheConfiguredPassword()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        using var certificate = ServerCertificateFor(Domain);
        material.Provision(BundleReference, TestCertificates.ToBundleOf("bundle-password", certificate));
        material.ProvisionText(PasswordReference, "bundle-password");

        // Act
        using var result = await LoaderFor(material).LoadAsync(
            BundleOf(passwordReference: PasswordReference),
            Domain,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result.Failure);
        Assert.True(result.Certificate?.Leaf.HasPrivateKey);
    }

    /// <summary>An unprotected bundle is a legitimate file, so a missing password is reported only once opening without one has failed.</summary>
    [Fact]
    public async Task LoadAsync_ProtectedBundleWithNoPasswordConfigured_ReportsThePasswordAsMissing()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        using var certificate = ServerCertificateFor(Domain);
        material.Provision(BundleReference, TestCertificates.ToBundleOf("bundle-password", certificate));

        // Act
        using var result = await LoaderFor(material).LoadAsync(BundleOf(), Domain, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CertificateMaterialFailure.BundlePasswordMissing, result.Failure);
    }

    [Fact]
    public async Task LoadAsync_ProtectedBundleWithTheWrongPassword_ReportsThePasswordAsIncorrect()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        using var certificate = ServerCertificateFor(Domain);
        material.Provision(BundleReference, TestCertificates.ToBundleOf("bundle-password", certificate));
        material.ProvisionText(PasswordReference, "the-other-password");

        // Act
        using var result = await LoaderFor(material).LoadAsync(
            BundleOf(passwordReference: PasswordReference),
            Domain,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CertificateMaterialFailure.BundlePasswordIncorrect, result.Failure);
    }

    /// <summary>A client that cannot build a path to a root rejects the handshake, so an issuing authority in the bundle has to travel with the leaf.</summary>
    [Fact]
    public async Task LoadAsync_BundleCarryingTheIssuingAuthority_PresentsItAfterTheLeaf()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        using var authority = TestCertificates.CreateCertificateAuthority("MailFathom Test Authority");
        using var certificate = ServerCertificateFor(Domain, issuer: authority);
        using var publicAuthority = TestCertificates.WithoutPrivateKey(authority);
        material.Provision(BundleReference, TestCertificates.ToBundleOf(bundlePassword: null, certificate, publicAuthority));

        // Act
        using var result = await LoaderFor(material).LoadAsync(BundleOf(), Domain, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result.Failure);
        var intermediate = Assert.Single(result.Certificate!.Intermediates);
        Assert.Equal(authority.Subject, intermediate.Subject);
    }

    /// <summary>A bundle is binary, so material that arrived as the configured value itself cannot be one however it parses.</summary>
    [Fact]
    public async Task LoadAsync_BundleSuppliedInline_IsRefusedBeforeItIsParsed()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        using var certificate = ServerCertificateFor(Domain);
        material.Provision(
            BundleReference,
            TestCertificates.ToBundleOf(bundlePassword: null, certificate),
            SecretMaterialSource.InlineValue);

        // Act
        using var result = await LoaderFor(material).LoadAsync(BundleOf(), Domain, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CertificateMaterialFailure.InlineEncodingNotSupported, result.Failure);
    }

    /// <summary>A PEM chain is a legitimate file in the other setting, so the encoding is reported as wrong for the role rather than as unreadable.</summary>
    [Fact]
    public async Task LoadAsync_PemMaterialConfiguredAsABundle_IsRefusedByRole()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        using var certificate = ServerCertificateFor(Domain);
        material.ProvisionText(BundleReference, TestCertificates.ToCertificateChainPem(certificate));

        // Act
        using var result = await LoaderFor(material).LoadAsync(BundleOf(), Domain, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CertificateMaterialFailure.EncodingNotSupportedForRole, result.Failure);
    }

    /// <summary>Which identity is presented would otherwise depend on parse order rather than on what an operator provisioned.</summary>
    [Fact]
    public async Task LoadAsync_BundleCarryingSeveralPrivateKeys_IsRefusedRatherThanPickingOne()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        using var first = ServerCertificateFor(Domain);
        using var second = ServerCertificateFor("other.example.test");
        material.Provision(BundleReference, TestCertificates.ToBundleOf(bundlePassword: null, first, second));

        // Act
        using var result = await LoaderFor(material).LoadAsync(BundleOf(), Domain, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CertificateMaterialFailure.ChainCarriesSeveralLeaves, result.Failure);
    }

    [Fact]
    public async Task LoadAsync_BundleCarryingNoPrivateKey_IsRefused()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        using var certificate = ServerCertificateFor(Domain);
        using var publicOnly = TestCertificates.WithoutPrivateKey(certificate);
        material.Provision(BundleReference, TestCertificates.ToBundleOf(bundlePassword: null, publicOnly));

        // Act
        using var result = await LoaderFor(material).LoadAsync(BundleOf(), Domain, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CertificateMaterialFailure.PrivateKeyMissing, result.Failure);
    }

    [Fact]
    public async Task LoadAsync_PemChainAndItsPrivateKey_LoadsAnIdentityCarryingThatKey()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        using var certificate = ServerCertificateFor(Domain);
        ProvidePem(material, certificate);

        // Act
        using var result = await LoaderFor(material).LoadAsync(PemOf(), Domain, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result.Failure);
        Assert.True(result.Certificate?.Leaf.HasPrivateKey);
    }

    [Fact]
    public async Task LoadAsync_PemChainCarryingTheIssuingAuthority_PresentsItAfterTheLeaf()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        using var authority = TestCertificates.CreateCertificateAuthority("MailFathom Test Authority");
        using var certificate = ServerCertificateFor(Domain, issuer: authority);
        material.ProvisionText(ChainReference, TestCertificates.ToCertificateChainPem(certificate, authority));
        material.ProvisionText(PrivateKeyReference, TestCertificates.ToPrivateKeyPem(certificate));

        // Act
        using var result = await LoaderFor(material).LoadAsync(PemOf(), Domain, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result.Failure);
        var intermediate = Assert.Single(result.Certificate!.Intermediates);
        Assert.Equal(authority.Subject, intermediate.Subject);
    }

    [Fact]
    public async Task LoadAsync_EncryptedPemPrivateKey_OpensWithTheConfiguredPassword()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        using var certificate = ServerCertificateFor(Domain);
        material.ProvisionText(ChainReference, TestCertificates.ToCertificateChainPem(certificate));
        material.ProvisionText(PrivateKeyReference, TestCertificates.ToEncryptedPrivateKeyPem(certificate, "key-password"));
        material.ProvisionText(PasswordReference, "key-password");

        // Act
        using var result = await LoaderFor(material).LoadAsync(
            PemOf(privateKeyPasswordReference: PasswordReference),
            Domain,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result.Failure);
        Assert.True(result.Certificate?.Leaf.HasPrivateKey);
    }

    [Fact]
    public async Task LoadAsync_EncryptedPemPrivateKeyWithTheWrongPassword_ReportsTheKeyAsUnreadable()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        using var certificate = ServerCertificateFor(Domain);
        material.ProvisionText(ChainReference, TestCertificates.ToCertificateChainPem(certificate));
        material.ProvisionText(PrivateKeyReference, TestCertificates.ToEncryptedPrivateKeyPem(certificate, "key-password"));
        material.ProvisionText(PasswordReference, "the-other-password");

        // Act
        using var result = await LoaderFor(material).LoadAsync(
            PemOf(privateKeyPasswordReference: PasswordReference),
            Domain,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CertificateMaterialFailure.PrivateKeyNotReadable, result.Failure);
    }

    /// <summary>The two failures are fixed differently — one file is damaged, the other is the wrong file — so they are reported apart.</summary>
    [Fact]
    public async Task LoadAsync_PrivateKeyOfAnotherCertificate_ReportsAMismatchRatherThanAnUnreadableKey()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        using var certificate = ServerCertificateFor(Domain);
        using var unrelated = ServerCertificateFor(Domain);
        material.ProvisionText(ChainReference, TestCertificates.ToCertificateChainPem(certificate));
        material.ProvisionText(PrivateKeyReference, TestCertificates.ToPrivateKeyPem(unrelated));

        // Act
        using var result = await LoaderFor(material).LoadAsync(PemOf(), Domain, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CertificateMaterialFailure.PrivateKeyDoesNotMatchCertificate, result.Failure);
    }

    [Fact]
    public async Task LoadAsync_MalformedPrivateKey_ReportsItAsUnreadable()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        using var certificate = ServerCertificateFor(Domain);
        material.ProvisionText(ChainReference, TestCertificates.ToCertificateChainPem(certificate));
        material.ProvisionText(PrivateKeyReference, "-----BEGIN PRIVATE KEY-----\nnot-a-key\n-----END PRIVATE KEY-----");

        // Act
        using var result = await LoaderFor(material).LoadAsync(PemOf(), Domain, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CertificateMaterialFailure.PrivateKeyNotReadable, result.Failure);
    }

    [Fact]
    public async Task LoadAsync_CertificateChainWithNoPrivateKeyConfigured_IsRefused()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        using var certificate = ServerCertificateFor(Domain);
        material.ProvisionText(ChainReference, TestCertificates.ToCertificateChainPem(certificate));
        var configured = new TlsServerCertificateOptions { CertificateChain = Block(ChainReference) };

        // Act
        using var result = await LoaderFor(material).LoadAsync(configured, Domain, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CertificateMaterialFailure.PrivateKeyMissing, result.Failure);
    }

    [Fact]
    public async Task LoadAsync_PrivateKeyWithNoCertificateChainConfigured_IsRefused()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        using var certificate = ServerCertificateFor(Domain);
        material.ProvisionText(PrivateKeyReference, TestCertificates.ToPrivateKeyPem(certificate));
        var configured = new TlsServerCertificateOptions { PrivateKey = Block(PrivateKeyReference) };

        // Act
        using var result = await LoaderFor(material).LoadAsync(configured, Domain, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CertificateMaterialFailure.MaterialMissing, result.Failure);
    }

    /// <summary>A chain states one identity followed by the authorities that issued it; a repeated leaf makes the identity depend on parse order.</summary>
    [Fact]
    public async Task LoadAsync_ChainRepeatingTheLeaf_IsRefused()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        using var certificate = ServerCertificateFor(Domain);
        material.ProvisionText(ChainReference, TestCertificates.ToCertificateChainPem(certificate, certificate));
        material.ProvisionText(PrivateKeyReference, TestCertificates.ToPrivateKeyPem(certificate));

        // Act
        using var result = await LoaderFor(material).LoadAsync(PemOf(), Domain, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CertificateMaterialFailure.ChainCarriesSeveralLeaves, result.Failure);
    }

    [Fact]
    public async Task LoadAsync_BundleAndPemMaterialTogether_IsRefusedAsAmbiguous()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        var configured = new TlsServerCertificateOptions
        {
            Bundle = Block(BundleReference),
            CertificateChain = Block(ChainReference),
            PrivateKey = Block(PrivateKeyReference),
        };

        // Act
        using var result = await LoaderFor(material).LoadAsync(configured, Domain, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CertificateMaterialFailure.MaterialKindAmbiguous, result.Failure);
    }

    [Fact]
    public async Task LoadAsync_NoMaterialConfigured_IsRefused()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();

        // Act
        using var result = await LoaderFor(material).LoadAsync(
            new TlsServerCertificateOptions(),
            Domain,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CertificateMaterialFailure.MaterialMissing, result.Failure);
    }

    [Fact]
    public async Task LoadAsync_ReferenceThatResolvesToNothing_IsRefused()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();

        // Act
        using var result = await LoaderFor(material).LoadAsync(BundleOf(), Domain, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CertificateMaterialFailure.SecretNotResolvable, result.Failure);
    }

    [Fact]
    public async Task LoadAsync_MaterialInNoSupportedEncoding_IsRefused()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        material.ProvisionText(BundleReference, "this is not a certificate");

        // Act
        using var result = await LoaderFor(material).LoadAsync(BundleOf(), Domain, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CertificateMaterialFailure.EncodingNotRecognized, result.Failure);
    }

    [Fact]
    public async Task LoadAsync_CertificateWhoseValidityHasNotStarted_IsRefused()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        using var certificate = TestCertificates.CreateServerIdentity(
            [Domain],
            Now.AddDays(1),
            Now.AddDays(90));
        ProvidePem(material, certificate);

        // Act
        using var result = await LoaderFor(material).LoadAsync(PemOf(), Domain, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CertificateMaterialFailure.CertificateNotYetValid, result.Failure);
    }

    [Fact]
    public async Task LoadAsync_ExpiredCertificate_IsRefused()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        using var certificate = TestCertificates.CreateServerIdentity(
            [Domain],
            Now.AddDays(-90),
            Now.AddDays(-1));
        ProvidePem(material, certificate);

        // Act
        using var result = await LoaderFor(material).LoadAsync(PemOf(), Domain, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CertificateMaterialFailure.CertificateExpired, result.Failure);
    }

    [Fact]
    public async Task LoadAsync_CertificateForAnotherDomain_IsRefused()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        using var certificate = ServerCertificateFor("other.example.test");
        ProvidePem(material, certificate);

        // Act
        using var result = await LoaderFor(material).LoadAsync(PemOf(), Domain, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CertificateMaterialFailure.DomainNotCoveredBySubjectAlternativeName, result.Failure);
    }

    /// <summary>Wildcard certificates are ordinary purchases, and clients admit exactly one label under one — no more and no fewer.</summary>
    [Theory]
    [InlineData("mail.example.test", true)]
    [InlineData("example.test", false)]
    [InlineData("a.b.example.test", false)]
    public async Task LoadAsync_WildcardCertificate_CoversOneLabelAndNoOther(string domain, bool covered)
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        using var certificate = ServerCertificateFor("*.example.test");
        ProvidePem(material, certificate);

        // Act
        using var result = await LoaderFor(material).LoadAsync(PemOf(), domain, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            covered ? null : CertificateMaterialFailure.DomainNotCoveredBySubjectAlternativeName,
            result.Failure);
    }

    [Fact]
    public async Task LoadAsync_CertificateThatPermitsOnlyClientAuthentication_IsRefused()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        using var certificate = TestCertificates.CreateServerIdentity(
            [Domain],
            Now.AddDays(-1),
            Now.AddDays(90),
            serverAuthentication: false);
        ProvidePem(material, certificate);

        // Act
        using var result = await LoaderFor(material).LoadAsync(PemOf(), Domain, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CertificateMaterialFailure.ServerAuthenticationNotPermitted, result.Failure);
    }

    /// <summary>Every current client ignores the common name, so honoring it here would accept material nothing will connect to.</summary>
    [Fact]
    public async Task LoadAsync_CertificateNamingTheDomainOnlyInItsCommonName_IsRefused()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        using var certificate = TestCertificates.CreateIdentityWithoutSubjectAlternativeName(
            Domain,
            Now.AddDays(-1),
            Now.AddDays(90));
        ProvidePem(material, certificate);

        // Act
        using var result = await LoaderFor(material).LoadAsync(PemOf(), Domain, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CertificateMaterialFailure.DomainNotCoveredBySubjectAlternativeName, result.Failure);
    }

    [Fact]
    public async Task LoadAsync_NoDomain_IsARefusalToCallRatherThanAConfigurationFailure()
    {
        // Arrange
        var loader = LoaderFor(new ProvisionedMaterialResolver());

        // Act, Assert
        await Assert.ThrowsAsync<ArgumentException>(() => loader.LoadAsync(
            BundleOf(),
            "   ",
            TestContext.Current.CancellationToken));
    }

    /// <summary>The identity has to outlive the bytes it was parsed from, or the key and its password stay readable in a process dump for as long as the endpoint serves.</summary>
    [Fact]
    public async Task LoadAsync_MaterialItParsed_IsErasedBeforeItReturns()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        using var certificate = ServerCertificateFor(Domain);
        material.ProvisionText(ChainReference, TestCertificates.ToCertificateChainPem(certificate));
        material.ProvisionText(PrivateKeyReference, TestCertificates.ToEncryptedPrivateKeyPem(certificate, "key-password"));
        material.ProvisionText(PasswordReference, "key-password");

        // Act
        using var result = await LoaderFor(material).LoadAsync(
            PemOf(privateKeyPasswordReference: PasswordReference),
            Domain,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result.Failure);
        Assert.Equal(3, material.IssuedMaterial.Count);
        Assert.All(
            material.IssuedMaterial,
            issued => Assert.Throws<ObjectDisposedException>(() => issued.RevealBytes().Length));
    }

    /// <summary>TLS 1.3 authenticates a server by having it sign the transcript, so a key barred from signing completes no handshake this endpoint offers.</summary>
    [Fact]
    public async Task LoadAsync_CertificateWhoseKeyUsageExcludesDigitalSignature_IsRefusedBeforeTheListenerOpens()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        using var certificate = TestCertificates.CreateServerIdentity(
            [Domain],
            Now.AddDays(-1),
            Now.AddDays(90),
            keyUsage: X509KeyUsageFlags.KeyEncipherment);
        ProvidePem(material, certificate);

        // Act
        using var result = await LoaderFor(material).LoadAsync(PemOf(), Domain, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CertificateMaterialFailure.DigitalSignatureNotPermitted, result.Failure);
    }

    /// <summary>A declared key usage is binding, so the one a certificate authority actually issues has to keep loading.</summary>
    [Fact]
    public async Task LoadAsync_CertificateWhoseKeyUsagePermitsDigitalSignature_LoadsAnIdentity()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        using var certificate = TestCertificates.CreateServerIdentity(
            [Domain],
            Now.AddDays(-1),
            Now.AddDays(90),
            keyUsage: X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment);
        ProvidePem(material, certificate);

        // Act
        using var result = await LoaderFor(material).LoadAsync(PemOf(), Domain, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result.Failure);
    }

    /// <summary>A second end-entity certificate pasted into a chain file issues nothing, so it would be presented to every client for no reason a path could use.</summary>
    [Fact]
    public async Task LoadAsync_ChainCarryingACertificateThatIsNoAuthority_IsRefused()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        using var certificate = ServerCertificateFor(Domain);
        using var otherIdentity = ServerCertificateFor("other.example.test");
        using var publicOtherIdentity = TestCertificates.WithoutPrivateKey(otherIdentity);
        material.ProvisionText(
            ChainReference,
            TestCertificates.ToCertificateChainPem(certificate, publicOtherIdentity));
        material.ProvisionText(PrivateKeyReference, TestCertificates.ToPrivateKeyPem(certificate));

        // Act
        using var result = await LoaderFor(material).LoadAsync(PemOf(), Domain, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CertificateMaterialFailure.ChainCarriesNonAuthorityCertificate, result.Failure);
    }

    /// <summary>An authority outside its own validity period breaks the path whatever the leaf beneath it says, and it is renewed from a different place than the leaf is.</summary>
    [Fact]
    public async Task LoadAsync_ChainCarryingAnExpiredAuthority_IsRefusedAgainstTheChainRatherThanTheLeaf()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        using var expiredAuthority = TestCertificates.CreateExpiredCertificateAuthority("MailFathom Retired Authority");
        using var certificate = ServerCertificateFor(Domain);
        using var publicAuthority = TestCertificates.WithoutPrivateKey(expiredAuthority);
        material.ProvisionText(
            ChainReference,
            TestCertificates.ToCertificateChainPem(certificate, publicAuthority));
        material.ProvisionText(PrivateKeyReference, TestCertificates.ToPrivateKeyPem(certificate));

        // Act
        using var result = await LoaderFor(material).LoadAsync(PemOf(), Domain, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CertificateMaterialFailure.ChainCertificateNotCurrentlyValid, result.Failure);
    }

    /// <summary>An authority that issued nothing in the chain takes no part in the path a client builds, so its presence means the wrong material was provisioned.</summary>
    [Fact]
    public async Task LoadAsync_ChainCarryingAnAuthorityThatIssuedNothingInIt_IsRefused()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        using var unrelatedAuthority = TestCertificates.CreateCertificateAuthority("MailFathom Unrelated Authority");
        using var certificate = ServerCertificateFor(Domain);
        using var publicAuthority = TestCertificates.WithoutPrivateKey(unrelatedAuthority);
        material.ProvisionText(
            ChainReference,
            TestCertificates.ToCertificateChainPem(certificate, publicAuthority));
        material.ProvisionText(PrivateKeyReference, TestCertificates.ToPrivateKeyPem(certificate));

        // Act
        using var result = await LoaderFor(material).LoadAsync(PemOf(), Domain, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CertificateMaterialFailure.ChainCarriesUnrelatedCertificate, result.Failure);
    }

    /// <summary>A bundle states no order at all, so the sequence a client follows has to be rebuilt from the issuer each certificate names.</summary>
    [Fact]
    public async Task LoadAsync_BundleCarryingItsAuthoritiesOutOfOrder_PresentsThemLeadingTowardsTheRoot()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        using var root = TestCertificates.CreateCertificateAuthority("MailFathom Test Root");
        using var intermediate = TestCertificates.IssueIntermediateAuthority(root, "MailFathom Test Intermediate");
        using var certificate = ServerCertificateFor(Domain, issuer: intermediate);
        using var publicRoot = TestCertificates.WithoutPrivateKey(root);
        using var publicIntermediate = TestCertificates.WithoutPrivateKey(intermediate);
        material.Provision(
            BundleReference,
            TestCertificates.ToBundleOf(bundlePassword: null, certificate, publicRoot, publicIntermediate));

        // Act
        using var result = await LoaderFor(material).LoadAsync(BundleOf(), Domain, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result.Failure);
        Assert.Equal(
            [intermediate.Subject, root.Subject],
            result.Certificate!.Intermediates.Select(static presented => presented.Subject));
    }

    private static TlsServerCertificateLoader LoaderFor(ProvisionedMaterialResolver material) =>
        new(material, new FakeTimeProvider(Now));

    private static X509Certificate2 ServerCertificateFor(string dnsName, X509Certificate2? issuer = null) =>
        TestCertificates.CreateServerIdentity([dnsName], Now.AddDays(-1), Now.AddDays(90), issuer);

    private static void ProvidePem(ProvisionedMaterialResolver material, X509Certificate2 certificate)
    {
        material.ProvisionText(ChainReference, TestCertificates.ToCertificateChainPem(certificate));
        material.ProvisionText(PrivateKeyReference, TestCertificates.ToPrivateKeyPem(certificate));
    }

    private static TlsServerCertificateOptions BundleOf(string? passwordReference = null) => new()
    {
        Bundle = Block(BundleReference, passwordReference),
    };

    private static TlsServerCertificateOptions PemOf(string? privateKeyPasswordReference = null) => new()
    {
        CertificateChain = Block(ChainReference),
        PrivateKey = Block(PrivateKeyReference, privateKeyPasswordReference),
    };

    private static ConfiguredSecret Block(string reference, string? passwordReference = null) => new()
    {
        Name = reference,
        SecretReference = reference,
        Password = passwordReference is null
            ? null
            : new ConfiguredSecret { Name = passwordReference, SecretReference = passwordReference },
    };
}
