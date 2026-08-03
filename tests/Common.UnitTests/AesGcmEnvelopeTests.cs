// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace MailFathom.Common.UnitTests;

public sealed class AesGcmEnvelopeTests
{
    private static readonly byte[] Key = Convert.FromHexString(
        "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f");

    private static readonly byte[] OtherKey = Convert.FromHexString(
        "1f1e1d1c1b1a191817161514131211100f0e0d0c0b0a09080706050403020100");

    [Fact]
    public void CreateKey_ReturnsAKeyOfTheDocumentedLength()
    {
        var key = AesGcmEnvelope.CreateKey();

        Assert.Equal(AesGcmEnvelope.KeySizeInBytes, key.Length);
    }

    [Fact]
    public void CreateKey_ReturnsADifferentKeyEachTime()
    {
        Assert.NotEqual(AesGcmEnvelope.CreateKey(), AesGcmEnvelope.CreateKey());
    }

    [Fact]
    public void SealText_ThenOpenText_ReturnsTheOriginalText()
    {
        var sealedValue = AesGcmEnvelope.SealText(Key, "a-credential", "https://mail.example.test:8443");

        Assert.Equal(
            "a-credential",
            AesGcmEnvelope.OpenText(Key, sealedValue, "https://mail.example.test:8443"));
    }

    [Fact]
    public void SealText_DoesNotContainThePlaintext()
    {
        var sealedValue = AesGcmEnvelope.SealText(Key, "a-credential", "https://mail.example.test:8443");

        Assert.DoesNotContain("a-credential", sealedValue, StringComparison.Ordinal);
    }

    [Fact]
    public void SealText_ProducesADifferentValueEachTimeForTheSameInput()
    {
        // A fresh nonce per operation, which is what keeps two profiles holding the same credential from being
        // recognizable as such by comparing the stored values.
        Assert.NotEqual(
            AesGcmEnvelope.SealText(Key, "a-credential", "https://mail.example.test:8443"),
            AesGcmEnvelope.SealText(Key, "a-credential", "https://mail.example.test:8443"));
    }

    [Fact]
    public void OpenText_RefusesAValueSealedUnderAnotherKey()
    {
        var sealedValue = AesGcmEnvelope.SealText(Key, "a-credential", "https://mail.example.test:8443");

        Assert.Throws<AuthenticationTagMismatchException>(
            () => AesGcmEnvelope.OpenText(OtherKey, sealedValue, "https://mail.example.test:8443"));
    }

    [Fact]
    public void OpenText_RefusesAValueBoundToAnotherEndpoint()
    {
        // The case the associated data exists for: a stored value copied from one profile to another does not open.
        var sealedValue = AesGcmEnvelope.SealText(Key, "a-credential", "https://mail.example.test:8443");

        Assert.Throws<AuthenticationTagMismatchException>(
            () => AesGcmEnvelope.OpenText(Key, sealedValue, "https://other.example.test:8443"));
    }

    [Fact]
    public void OpenText_RefusesAnAlteredValue()
    {
        var sealedValue = Convert.FromBase64String(
            AesGcmEnvelope.SealText(Key, "a-credential", "https://mail.example.test:8443"));

        sealedValue[^1] ^= 0xFF;

        Assert.Throws<AuthenticationTagMismatchException>(() => AesGcmEnvelope.OpenText(
            Key,
            Convert.ToBase64String(sealedValue),
            "https://mail.example.test:8443"));
    }

    [Fact]
    public void OpenText_RefusesAValueShorterThanItsOwnHeader()
    {
        var failure = Assert.Throws<CryptographicException>(
            () => AesGcmEnvelope.OpenText(Key, Convert.ToBase64String(new byte[8]), "https://mail.example.test:8443"));

        Assert.Contains("shorter than its own header", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OpenText_RefusesAValueThatIsNotBase64()
    {
        Assert.Throws<FormatException>(
            () => AesGcmEnvelope.OpenText(Key, "not base64 at all !!", "https://mail.example.test:8443"));
    }

    [Fact]
    public void Seal_RefusesAKeyOfTheWrongLength()
    {
        var failure = Assert.Throws<ArgumentException>(
            () => AesGcmEnvelope.Seal(new byte[16], "value"u8, "context"u8));

        Assert.Contains("AES-256", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Open_RefusesAKeyOfTheWrongLength()
    {
        Assert.Throws<ArgumentException>(() => AesGcmEnvelope.Open(new byte[16], new byte[64], "context"u8));
    }

    [Fact]
    public void Seal_ThenOpen_RoundTripsBytesThatAreNotText()
    {
        var plaintext = new byte[] { 0x00, 0xFF, 0x7F, 0x80, 0x00 };

        var sealedValue = AesGcmEnvelope.Seal(Key, plaintext, "row-42"u8);

        Assert.Equal(plaintext, AesGcmEnvelope.Open(Key, sealedValue, "row-42"u8));
    }

    [Fact]
    public void Seal_ThenOpen_RoundTripsAnEmptyValue()
    {
        var sealedValue = AesGcmEnvelope.Seal(Key, ReadOnlySpan<byte>.Empty, "row-42"u8);

        Assert.Empty(AesGcmEnvelope.Open(Key, sealedValue, "row-42"u8));
    }

    [Fact]
    public void SealText_RoundTripsTextOutsideTheAsciiRange()
    {
        const string Credential = "hasło-zażółć-🔐";

        var sealedValue = AesGcmEnvelope.SealText(Key, Credential, "https://mail.example.test:8443");

        Assert.Equal(Credential, AesGcmEnvelope.OpenText(Key, sealedValue, "https://mail.example.test:8443"));
    }

    [Fact]
    public void Seal_PrefixesTheValueWithANonceAndATag()
    {
        // The layout is a compatibility contract: a value written by an older build has to open here.
        var sealedValue = AesGcmEnvelope.Seal(Key, Encoding.UTF8.GetBytes("abc"), "context"u8);

        Assert.Equal(12 + 16 + 3, sealedValue.Length);
    }
}
