// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Infrastructure.Security;
using Xunit;

namespace MailMcp.Infrastructure.UnitTests;

/// <summary>Covers what counts as a bearer credential, which both accepted credentials are lifted out of.</summary>
/// <remarks>
/// One parser for both matters more than its size. An API key and an access token that disagreed about a well-formed
/// header would be two definitions of "the request presented nothing", and the same request could then be malformed to
/// one check and a credential to the other.
/// </remarks>
public sealed class BearerCredentialHeaderTests
{
    [Theory]
    [InlineData("Bearer a-credential", "a-credential")]
    [InlineData("bearer a-credential", "a-credential")]
    [InlineData("BEARER a-credential", "a-credential")]
    [InlineData("  Bearer a-credential  ", "a-credential")]
    [InlineData("Bearer   a-credential", "a-credential")]
    public void TryRead_ABearerHeader_ReadsTheCredentialWhateverTheCasingOrSpacing(
        string headerValue,
        string expectedCredential)
    {
        // Arrange, Act
        var wasRead = BearerCredentialHeader.TryRead(headerValue, out var credential);

        // Assert
        Assert.True(wasRead);
        Assert.Equal(expectedCredential, credential);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Basic dXNlcjpwYXNz")]
    [InlineData("Bearer")]
    [InlineData("Bearer ")]
    [InlineData("Bearera-credential")]
    [InlineData("a-credential")]
    public void TryRead_AnythingThatIsNotOneBearerCredential_ReadsNothing(string? headerValue)
    {
        // Arrange, Act
        var wasRead = BearerCredentialHeader.TryRead(headerValue, out var credential);

        // Assert
        Assert.False(wasRead);
        Assert.Empty(credential);
    }

    /// <summary>Two headers reach a handler joined by a comma, which is one malformed value rather than two credentials to try in turn.</summary>
    [Fact]
    public void TryRead_TwoHeadersJoinedAsOneValue_ReadsThemAsOneCredentialRatherThanChoosing()
    {
        // Arrange, Act
        BearerCredentialHeader.TryRead("Bearer first, Bearer second", out var credential);

        // Assert
        Assert.Equal("first, Bearer second", credential);
    }
}
