// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MailFathom.Common.ClientAssertions;
using Xunit;

namespace MailFathom.Common.UnitTests.ClientAssertions;

/// <summary>Covers the document a client signs, read as the endpoint will read it.</summary>
/// <remarks>
/// The minter and the verifier are two halves of one contract that ship in the same product, so a mistake here is not a
/// compile error anywhere — it is a deployment where every client is refused. These assertions read the produced
/// credential back segment by segment rather than through a token library, so what is checked is what actually goes over
/// the wire.
/// </remarks>
public sealed class ClientAssertionMinterTests
{
    private static readonly DateTimeOffset MintedAt = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Mint_AnyKey_DeclaresTheTypeTheEndpointRecognizes()
    {
        // Arrange
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        // Act
        var header = SegmentOf(ClientAssertionMinter.Mint(signingKey, ClientAssertion.AdminAudience, MintedAt), 0);

        // Assert
        Assert.Equal(ClientAssertion.DeclaredType, header.GetProperty("typ").GetString());
    }

    /// <summary>The algorithm follows from the key rather than from anything either side configures, so a client cannot present a signature the endpoint refuses.</summary>
    [Theory]
    [InlineData(256, "ES256")]
    [InlineData(384, "ES384")]
    [InlineData(521, "ES512")]
    public void Mint_AnEllipticCurveKey_NamesTheAlgorithmThatCurveIsSizedFor(int curveSize, string algorithmName)
    {
        // Arrange
        using var signingKey = ECDsa.Create(CurveOf(curveSize));

        // Act
        var header = SegmentOf(ClientAssertionMinter.Mint(signingKey, ClientAssertion.McpAudience, MintedAt), 0);

        // Assert
        Assert.Equal(algorithmName, header.GetProperty("alg").GetString());
    }

    [Fact]
    public void Mint_AnRsaKey_NamesTheRsaAlgorithm()
    {
        // Arrange
        using var signingKey = RSA.Create(ClientAssertionKeyMaterial.ShortestRsaModulusInBits);

        // Act
        var header = SegmentOf(ClientAssertionMinter.Mint(signingKey, ClientAssertion.McpAudience, MintedAt), 0);

        // Assert
        Assert.Equal(ClientAssertionSignature.RsaAlgorithmName, header.GetProperty("alg").GetString());
    }

    /// <summary>The audience is what keeps a credential minted to read a mailbox from administering the service, so it is written verbatim rather than derived.</summary>
    [Fact]
    public void Mint_AnAudience_WritesItVerbatim()
    {
        // Arrange
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        // Act
        var payload = SegmentOf(ClientAssertionMinter.Mint(signingKey, ClientAssertion.AdminAudience, MintedAt), 1);

        // Assert
        Assert.Equal(ClientAssertion.AdminAudience, payload.GetProperty(ClientAssertion.AudienceClaimName).GetString());
    }

    /// <summary>A minted assertion has to expire well inside the window the endpoint permits, or the client the command exists to serve would be refused by its own product.</summary>
    [Fact]
    public void Mint_AnyKey_ExpiresWellInsideThePermittedWindow()
    {
        // Arrange
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        // Act
        var payload = SegmentOf(ClientAssertionMinter.Mint(signingKey, ClientAssertion.AdminAudience, MintedAt), 1);
        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(
            payload.GetProperty(ClientAssertion.ExpiresAtClaimName).GetInt64());

        // Assert
        Assert.Equal(MintedAt + ClientAssertion.MintedLifetime, expiresAt);
        Assert.True(expiresAt <= MintedAt + ClientAssertion.MaximumLifetime);
    }

    /// <summary>Two assertions minted at the same instant carry different identifiers, which is the whole of what makes replay refusable.</summary>
    [Fact]
    public void Mint_TwiceAtTheSameInstant_CarriesDifferentIdentifiers()
    {
        // Arrange
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        // Act
        var first = SegmentOf(ClientAssertionMinter.Mint(signingKey, ClientAssertion.AdminAudience, MintedAt), 1);
        var second = SegmentOf(ClientAssertionMinter.Mint(signingKey, ClientAssertion.AdminAudience, MintedAt), 1);

        // Assert
        Assert.NotEqual(
            first.GetProperty(ClientAssertion.IdentifierClaimName).GetString(),
            second.GetProperty(ClientAssertion.IdentifierClaimName).GetString());
    }

    /// <summary>The identifier is the one value a client chooses that the endpoint has to remember, so it stays far inside what the endpoint will accept.</summary>
    [Fact]
    public void Mint_AnyKey_CarriesAnIdentifierInsideTheAcceptedLength()
    {
        // Arrange
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        // Act
        var payload = SegmentOf(ClientAssertionMinter.Mint(signingKey, ClientAssertion.AdminAudience, MintedAt), 1);
        var identifier = payload.GetProperty(ClientAssertion.IdentifierClaimName).GetString();

        // Assert
        Assert.NotNull(identifier);
        Assert.InRange(identifier.Length, 1, ClientAssertion.IdentifierLengthLimit);
    }

    /// <summary>The signature covers the two encoded segments joined by a full stop, which is the one detail a verifier reads and no test above would notice.</summary>
    [Fact]
    public void Mint_AnyKey_SignsTheEncodedHeaderAndPayload()
    {
        // Arrange
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        // Act
        var assertion = ClientAssertionMinter.Mint(signingKey, ClientAssertion.AdminAudience, MintedAt);
        var segments = assertion.Split('.');

        // Assert
        Assert.Equal(3, segments.Length);
        Assert.True(signingKey.VerifyData(
            Encoding.ASCII.GetBytes($"{segments[0]}.{segments[1]}"),
            Base64Url.DecodeFromChars(segments[2]),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    /// <summary>
    /// The algorithm is looked up from the curve rather than from its size, so a key on a curve of a permitted size but
    /// an unpermitted identity has no algorithm at all — and minting refuses rather than emitting an assertion labelled
    /// with one that does not describe its signature.
    /// </summary>
    [Fact]
    public void Mint_AKeyOnAnUnpermittedCurveOfAPermittedSize_IsRefused()
    {
        // Arrange
        using var signingKey = ECDsa.Create(ECCurve.CreateFromFriendlyName("secP256k1"));

        // Act, Assert
        Assert.Throws<NotSupportedException>(() =>
            ClientAssertionMinter.Mint(signingKey, ClientAssertion.AdminAudience, MintedAt));
    }

    private static ECCurve CurveOf(int curveSize) => curveSize switch
    {
        256 => ECCurve.NamedCurves.nistP256,
        384 => ECCurve.NamedCurves.nistP384,
        _ => ECCurve.NamedCurves.nistP521,
    };

    private static JsonElement SegmentOf(string assertion, int index)
    {
        var segment = assertion.Split('.')[index];

        return JsonDocument.Parse(Base64Url.DecodeFromChars(segment)).RootElement;
    }
}
