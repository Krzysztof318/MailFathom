// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using MailFathom.Common.ClientAssertions;
using Xunit;

namespace MailFathom.Common.UnitTests.ClientAssertions;

/// <summary>Covers which key material each half of the pair accepts, and what it says about the material it refuses.</summary>
/// <remarks>
/// The case that matters most is the one nothing else would ever report. A private key written where the public half
/// belongs imports cleanly and verifies signatures correctly, so a deployment configured with one would start and run
/// while holding exactly the credential key-pair authentication exists to keep off the host. Everything below exists so
/// that case is refused by its own name rather than by whichever generic parse failure happened to fire.
/// </remarks>
public sealed class ClientAssertionKeyMaterialTests
{
    [Fact]
    public void ReadPublicKey_APublicKeyPem_ReadsTheKey()
    {
        // Arrange
        using var pair = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        // Act
        using var publicKey = ClientAssertionKeyMaterial.ReadPublicKey(pair.ExportSubjectPublicKeyInfoPem(), out _);

        // Assert
        Assert.IsAssignableFrom<ECDsa>(publicKey);
    }

    [Fact]
    public void ReadPublicKey_AnRsaPublicKeyPem_ReadsTheKey()
    {
        // Arrange
        using var pair = RSA.Create(ClientAssertionKeyMaterial.ShortestRsaModulusInBits);

        // Act
        using var publicKey = ClientAssertionKeyMaterial.ReadPublicKey(pair.ExportSubjectPublicKeyInfoPem(), out _);

        // Assert
        Assert.IsAssignableFrom<RSA>(publicKey);
    }

    /// <summary>The one refusal a running deployment would otherwise never produce, because a private key verifies signatures perfectly well.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReadPublicKey_APrivateKeyPem_ReportsTheWrongHalf(bool ellipticCurve)
    {
        // Arrange
        using var pair = PrivateKeyOf(ellipticCurve);

        // Act
        using var publicKey = ClientAssertionKeyMaterial.ReadPublicKey(pair.ExportPkcs8PrivateKeyPem(), out var fault);

        // Assert
        Assert.Null(publicKey);
        Assert.Equal(ClientAssertionKeyFault.WrongHalf, fault);
    }

    /// <summary>A key file written in the older algorithm-specific form is still a private key, and is refused as one rather than as unparseable material.</summary>
    [Fact]
    public void ReadPublicKey_AnAlgorithmSpecificPrivateKeyPem_ReportsTheWrongHalf()
    {
        // Arrange
        using var pair = RSA.Create(ClientAssertionKeyMaterial.ShortestRsaModulusInBits);

        // Act
        using var publicKey = ClientAssertionKeyMaterial.ReadPublicKey(pair.ExportRSAPrivateKeyPem(), out var fault);

        // Assert
        Assert.Null(publicKey);
        Assert.Equal(ClientAssertionKeyFault.WrongHalf, fault);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a key at all")]
    [InlineData("-----BEGIN CERTIFICATE-----\nQUJD\n-----END CERTIFICATE-----")]
    public void ReadPublicKey_MaterialThatIsNoPublicKeyPem_ReportsThatItIsNotOne(string material)
    {
        // Act
        using var publicKey = ClientAssertionKeyMaterial.ReadPublicKey(material, out var fault);

        // Assert
        Assert.Null(publicKey);
        Assert.Equal(ClientAssertionKeyFault.NotPem, fault);
    }

    /// <summary>An RSA key below the accepted modulus is refused where the material is read, so no signature it could produce is ever verified.</summary>
    [Fact]
    public void ReadPublicKey_AnRsaKeyBelowTheShortestModulus_ReportsThatItIsTooShort()
    {
        // Arrange
        using var pair = RSA.Create(1024);

        // Act
        using var publicKey = ClientAssertionKeyMaterial.ReadPublicKey(pair.ExportSubjectPublicKeyInfoPem(), out var fault);

        // Assert
        Assert.Null(publicKey);
        Assert.Equal(ClientAssertionKeyFault.ModulusTooShort, fault);
    }

    /// <summary>
    /// The allow-list is over three named curves, and a curve is recognized by its identifier rather than by its size.
    /// <c>secp256k1</c> and the Brainpool curves have exactly the sizes the three NIST curves do, so a key admitted on
    /// its length would be trusted over a curve RFC 7518 defines no algorithm for, and its signatures would be labelled
    /// <c>ES256</c> or <c>ES384</c> anyway — an algorithm name that does not describe them.
    /// </summary>
    [Theory]
    [InlineData("secP256k1")]
    [InlineData("brainpoolP256r1")]
    [InlineData("brainpoolP384r1")]
    public void ReadPublicKey_ACurveOfAPermittedSizeButNotAPermittedIdentity_ReportsAnUnsupportedAlgorithm(
        string curveName)
    {
        // Arrange
        using var pair = ECDsa.Create(ECCurve.CreateFromFriendlyName(curveName));

        // Act
        using var publicKey = ClientAssertionKeyMaterial.ReadPublicKey(pair.ExportSubjectPublicKeyInfoPem(), out var fault);

        // Assert
        Assert.Null(publicKey);
        Assert.Equal(ClientAssertionKeyFault.UnsupportedAlgorithm, fault);
    }

    /// <summary>The same rule reaches the private half, so the command refuses to mint under a curve the deployment would refuse.</summary>
    [Fact]
    public void ReadPrivateKey_ACurveOfAPermittedSizeButNotAPermittedIdentity_ReportsAnUnsupportedAlgorithm()
    {
        // Arrange
        using var pair = ECDsa.Create(ECCurve.CreateFromFriendlyName("secP256k1"));

        // Act
        using var privateKey = ClientAssertionKeyMaterial.ReadPrivateKey(pair.ExportPkcs8PrivateKeyPem(), out var fault);

        // Assert
        Assert.Null(privateKey);
        Assert.Equal(ClientAssertionKeyFault.UnsupportedAlgorithm, fault);
    }

    /// <summary>A key of a kind no permitted algorithm is defined over is refused rather than accepted with no algorithm to verify it by.</summary>
    [Fact]
    public void ReadPublicKey_AKeyNoPermittedAlgorithmCovers_ReportsAnUnsupportedAlgorithm()
    {
        // Arrange
        using var pair = DSA.Create(2048);

        // Act
        using var publicKey = ClientAssertionKeyMaterial.ReadPublicKey(pair.ExportSubjectPublicKeyInfoPem(), out var fault);

        // Assert
        Assert.Null(publicKey);
        Assert.Equal(ClientAssertionKeyFault.UnsupportedAlgorithm, fault);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReadPrivateKey_APrivateKeyPem_ReadsTheKey(bool ellipticCurve)
    {
        // Arrange
        using var pair = PrivateKeyOf(ellipticCurve);

        // Act
        using var privateKey = ClientAssertionKeyMaterial.ReadPrivateKey(pair.ExportPkcs8PrivateKeyPem(), out _);

        // Assert
        Assert.NotNull(privateKey);
        Assert.NotNull(ClientAssertionSignature.AlgorithmFor(privateKey));
    }

    /// <summary>The mirror of the refusal above: an operator who hands the command the half they registered is told which file to pass instead.</summary>
    [Fact]
    public void ReadPrivateKey_APublicKeyPem_ReportsTheWrongHalf()
    {
        // Arrange
        using var pair = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        // Act
        using var privateKey = ClientAssertionKeyMaterial.ReadPrivateKey(pair.ExportSubjectPublicKeyInfoPem(), out var fault);

        // Assert
        Assert.Null(privateKey);
        Assert.Equal(ClientAssertionKeyFault.WrongHalf, fault);
    }

    /// <summary>A password-protected key is a key the operator has, so it is reported as needing a password rather than as unreadable material.</summary>
    [Fact]
    public void ReadPrivateKey_AnEncryptedPrivateKeyPem_ReportsThatItIsEncrypted()
    {
        // Arrange
        using var pair = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var material = pair.ExportEncryptedPkcs8PrivateKeyPem(
            "a-passphrase",
            new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 1000));

        // Act
        using var privateKey = ClientAssertionKeyMaterial.ReadPrivateKey(material, out var fault);

        // Assert
        Assert.Null(privateKey);
        Assert.Equal(ClientAssertionKeyFault.EncryptedPrivateKey, fault);
    }

    private static AsymmetricAlgorithm PrivateKeyOf(bool ellipticCurve) => ellipticCurve
        ? ECDsa.Create(ECCurve.NamedCurves.nistP256)
        : RSA.Create(ClientAssertionKeyMaterial.ShortestRsaModulusInBits);
}
