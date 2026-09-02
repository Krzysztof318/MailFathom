// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers.Text;
using System.Text;
using MailFathom.Infrastructure.Security.OAuth;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Security.OAuth;

/// <summary>Covers reading an issuer out of a token nothing has verified.</summary>
/// <remarks>
/// Everything reaching this comes from an unauthenticated request, so the cases worth stating are the malformed ones. A
/// value that is refused here selects no validator and the request is answered exactly as one presenting nothing at all,
/// which is why none of the refusals below is distinguished from another.
/// </remarks>
public sealed class UnverifiedJsonWebTokenTests
{
    [Fact]
    public void TryReadClaimedIssuer_ACompactTokenNamingAnIssuer_ReadsIt()
    {
        // Arrange
        var token = TokenWithPayload("""{"iss":"https://sso.example.test/realms/mailfathom","sub":"1"}""");

        // Act
        var wasRead = UnverifiedJsonWebToken.TryReadClaimedIssuer(token, out var claimedIssuer);

        // Assert
        Assert.True(wasRead);
        Assert.Equal("https://sso.example.test/realms/mailfathom", claimedIssuer);
    }

    /// <summary>An API key is an opaque string, and telling it apart from a token by shape is what routes each credential to the handler that understands it.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("an-opaque-api-key")]
    [InlineData("only.two")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("header..signature")]
    public void TryReadClaimedIssuer_ACredentialThatIsNotACompactToken_ReadsNothing(string? credential)
    {
        // Arrange, Act
        var wasRead = UnverifiedJsonWebToken.TryReadClaimedIssuer(credential, out var claimedIssuer);

        // Assert
        Assert.False(wasRead);
        Assert.Null(claimedIssuer);
    }

    /// <summary>A fourth segment makes this an encrypted token, whose second segment is a key rather than a set of claims.</summary>
    [Fact]
    public void TryReadClaimedIssuer_AFiveSegmentEncryptedToken_ReadsNothing()
    {
        // Arrange
        var token = $"header.{Encode("""{"iss":"https://sso.example.test"}""")}.key.vector.tag";

        // Act, Assert
        Assert.False(UnverifiedJsonWebToken.TryReadClaimedIssuer(token, out _));
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[]")]
    [InlineData("""{"sub":"1"}""")]
    [InlineData("""{"iss":42}""")]
    [InlineData("""{"iss":""}""")]
    [InlineData("""{"iss":null}""")]
    public void TryReadClaimedIssuer_APayloadThatNamesNoIssuer_ReadsNothing(string payload)
    {
        // Arrange
        var token = TokenWithPayload(payload);

        // Act, Assert
        Assert.False(UnverifiedJsonWebToken.TryReadClaimedIssuer(token, out _));
    }

    /// <summary>Nothing unverified is decoded without a bound, so a caller cannot make the host parse an arbitrarily large document before anything has been checked.</summary>
    [Fact]
    public void TryReadClaimedIssuer_APayloadBeyondTheSizeLimit_ReadsNothingRatherThanDecodingIt()
    {
        // Arrange
        var oversizedIssuer = new string('a', 16 * 1024);
        var token = TokenWithPayload($$"""{"iss":"https://{{oversizedIssuer}}.example.test"}""");

        // Act, Assert
        Assert.False(UnverifiedJsonWebToken.TryReadClaimedIssuer(token, out _));
    }

    /// <summary>Base64url without padding is what a compact serialization uses, and a payload that is not valid base64url is refused rather than partially decoded.</summary>
    [Fact]
    public void TryReadClaimedIssuer_APayloadThatIsNotBase64Url_ReadsNothing()
    {
        // Arrange
        const string token = "header.not+valid/base64url=.signature";

        // Act, Assert
        Assert.False(UnverifiedJsonWebToken.TryReadClaimedIssuer(token, out _));
    }

    /// <summary>The declared type is what tells a credential a client minted for itself from one an authorization server issued, before either has been verified.</summary>
    [Fact]
    public void TryReadDeclaredType_ACompactTokenDeclaringAType_ReadsIt()
    {
        // Arrange
        var token = TokenWithHeader("""{"alg":"ES256","typ":"mailfathom-client-assertion+jwt"}""");

        // Act
        var wasRead = UnverifiedJsonWebToken.TryReadDeclaredType(token, out var declaredType);

        // Assert
        Assert.True(wasRead);
        Assert.Equal("mailfathom-client-assertion+jwt", declaredType);
    }

    /// <summary>A token declaring nothing selects no assertion handler, so the absence has to be reported rather than defaulted.</summary>
    [Theory]
    [InlineData("""{"alg":"RS256"}""")]
    [InlineData("""{"alg":"RS256","typ":1}""")]
    [InlineData("""["not","an","object"]""")]
    [InlineData("not json at all")]
    public void TryReadDeclaredType_AHeaderNamingNoType_ReadsNothing(string header)
    {
        // Arrange
        var token = TokenWithHeader(header);

        // Act
        var wasRead = UnverifiedJsonWebToken.TryReadDeclaredType(token, out var declaredType);

        // Assert
        Assert.False(wasRead);
        Assert.Null(declaredType);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("an-opaque-api-key")]
    [InlineData("only.two")]
    public void TryReadDeclaredType_ACredentialThatIsNotACompactToken_ReadsNothing(string? credential)
    {
        // Arrange, Act
        var wasRead = UnverifiedJsonWebToken.TryReadDeclaredType(credential, out var declaredType);

        // Assert
        Assert.False(wasRead);
        Assert.Null(declaredType);
    }

    private static string TokenWithPayload(string payload) => $"header.{Encode(payload)}.signature";

    private static string TokenWithHeader(string header) => $"{Encode(header)}.payload.signature";

    private static string Encode(string payload) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payload));
}
