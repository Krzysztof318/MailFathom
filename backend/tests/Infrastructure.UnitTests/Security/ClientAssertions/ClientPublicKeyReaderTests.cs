// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using MailFathom.Common.ClientAssertions;
using MailFathom.Infrastructure.Security.ClientAssertions;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Security.ClientAssertions;

/// <summary>Covers what an operator may hand the deployment as a client's public key, and what it is stored as.</summary>
/// <remarks>
/// The value stored is canonical rather than whatever spelling the file carried, and the value it is resolved by is the
/// fingerprint a client's assertions have to name. Both are asserted here rather than at the administrative boundary,
/// because both are properties of the key and not of the command that carried it.
/// </remarks>
public sealed class ClientPublicKeyReaderTests
{
    [Fact]
    public void TryRead_AnEllipticCurvePublicKey_IsReadAsCanonicalMaterialUnderAFingerprint()
    {
        // Arrange
        var reader = new ClientPublicKeyReader();
        using var clientKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        // Act
        var read = reader.TryRead(clientKey.ExportSubjectPublicKeyInfoPem(), out var publicKey);

        // Assert
        Assert.True(read);
        Assert.NotNull(publicKey);
        Assert.Equal(clientKey.ExportSubjectPublicKeyInfoPem(), publicKey.Material);
        Assert.True(publicKey.Lookup.IsSpecified);
    }

    /// <summary>An RSA client is read on the same terms, so the method is not quietly elliptic-curve only.</summary>
    [Fact]
    public void TryRead_AnRsaPublicKeyOfTheShortestAcceptedModulus_IsRead()
    {
        // Arrange
        var reader = new ClientPublicKeyReader();
        using var clientKey = RSA.Create(ClientAssertionKeyMaterial.ShortestRsaModulusInBits);

        // Act
        var read = reader.TryRead(clientKey.ExportSubjectPublicKeyInfoPem(), out var publicKey);

        // Assert
        Assert.True(read);
        Assert.NotNull(publicKey);
    }

    /// <summary>The fingerprint is what a client writes into its assertions, so reading one key twice must answer alike.</summary>
    [Fact]
    public void TryRead_OneKeyReadTwice_ResolvesToTheSameFingerprintBothTimes()
    {
        // Arrange
        var reader = new ClientPublicKeyReader();
        using var clientKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var written = clientKey.ExportSubjectPublicKeyInfoPem();

        // Act
        Assert.True(reader.TryRead(written, out var first));
        Assert.True(reader.TryRead(written, out var second));

        // Assert
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first.Lookup, second.Lookup);
    }

    /// <summary>Two clients are two credentials, so two keys must not resolve to one row.</summary>
    [Fact]
    public void TryRead_TwoDifferentKeys_ResolveToDifferentFingerprints()
    {
        // Arrange
        var reader = new ClientPublicKeyReader();
        using var firstKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var secondKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        // Act
        Assert.True(reader.TryRead(firstKey.ExportSubjectPublicKeyInfoPem(), out var first));
        Assert.True(reader.TryRead(secondKey.ExportSubjectPublicKeyInfoPem(), out var second));

        // Assert
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first.Lookup, second.Lookup);
    }

    /// <summary>The deployment holds the half it verifies with, so a private key is a mistake worth refusing loudly.</summary>
    [Fact]
    public void TryRead_APrivateKey_IsRefused()
    {
        // Arrange
        var reader = new ClientPublicKeyReader();
        using var clientKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        // Act
        var read = reader.TryRead(clientKey.ExportPkcs8PrivateKeyPem(), out var publicKey);

        // Assert
        Assert.False(read);
        Assert.Null(publicKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a key at all")]
    [InlineData("-----BEGIN PUBLIC KEY-----\nnot base64\n-----END PUBLIC KEY-----")]
    public void TryRead_SomethingThatIsNoKey_IsRefused(string? written)
    {
        // Arrange
        var reader = new ClientPublicKeyReader();

        // Act
        var read = reader.TryRead(written, out var publicKey);

        // Assert
        Assert.False(read);
        Assert.Null(publicKey);
    }

    /// <summary>The refusal an operator reads is the deployment's own, so it has to say what would be accepted.</summary>
    [Fact]
    public void DescribeAcceptedForm_TheAnswer_NamesThePemBlockAndTheShortestAcceptedModulus()
    {
        // Arrange
        var reader = new ClientPublicKeyReader();

        // Act
        var described = reader.DescribeAcceptedForm();

        // Assert
        Assert.Contains("PUBLIC KEY", described, StringComparison.Ordinal);
        Assert.Contains(
            ClientAssertionKeyMaterial.ShortestRsaModulusInBits.ToString(System.Globalization.CultureInfo.InvariantCulture),
            described,
            StringComparison.Ordinal);
    }
}
