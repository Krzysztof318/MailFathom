// Copyright © 2026 Krzysztof Kasprowicz

using System.Buffers.Text;
using System.Text;
using MailMcp.Infrastructure.Security;
using Xunit;

namespace MailMcp.Infrastructure.UnitTests;

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
        var token = TokenWithPayload("""{"iss":"https://sso.example.test/realms/mailmcp","sub":"1"}""");

        // Act
        var wasRead = UnverifiedJsonWebToken.TryReadClaimedIssuer(token, out var claimedIssuer);

        // Assert
        Assert.True(wasRead);
        Assert.Equal("https://sso.example.test/realms/mailmcp", claimedIssuer);
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

    private static string TokenWithPayload(string payload) => $"header.{Encode(payload)}.signature";

    private static string Encode(string payload) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payload));
}
