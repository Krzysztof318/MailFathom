// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Security.OAuth;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Security.OAuth;

/// <summary>Covers the shape OAuth requires of an identifier, and the one place the two identifiers differ.</summary>
/// <remarks>
/// Both an issuer and a canonical resource end up in an exact string comparison against something a token carries, so a
/// value accepted here that does not match what the other side writes is a deployment that starts and then refuses every
/// request. That is why the trailing-slash cases below are the ones worth stating explicitly.
/// </remarks>
public sealed class OAuthIdentifierUriTests
{
    [Theory]
    [InlineData("https://sso.example.test")]
    [InlineData("https://sso.example.test/")]
    [InlineData("https://sso.example.test/realms/mailfathom")]
    [InlineData("https://sso.example.test:8443/realms/mailfathom")]
    public void IsWellFormed_AnHttpsIdentifierWithNoQueryOrFragment_IsAccepted(string identifier)
    {
        // Arrange, Act, Assert
        Assert.True(OAuthIdentifierUri.IsWellFormed(identifier));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("sso.example.test")]
    [InlineData("http://sso.example.test")]
    [InlineData("https://sso.example.test?realm=mailfathom")]
    [InlineData("https://sso.example.test#realm")]
    [InlineData("https://operator:secret@sso.example.test")]
    public void IsWellFormed_AnythingElse_IsRefused(string? identifier)
    {
        // Arrange, Act, Assert
        Assert.False(OAuthIdentifierUri.IsWellFormed(identifier));
    }

    /// <summary>
    /// The reason the issuer is never canonicalized. Authorization servers in wide deployment publish an issuer whose
    /// whole path is one trailing slash, and a resource server that tidied it away would compare against a value the
    /// server never emits — starting cleanly and refusing every token.
    /// </summary>
    [Fact]
    public void IsWellFormed_AnIssuerWhosePathIsOneTrailingSlash_IsAcceptedWithoutBeingRewritten()
    {
        // Arrange
        const string issuer = "https://tenant.identity.example.test/";

        // Act, Assert
        Assert.True(OAuthIdentifierUri.IsWellFormed(issuer));
    }

    [Theory]
    [InlineData("https://mail.example.test/mcp", "https://mail.example.test/mcp")]
    [InlineData("HTTPS://Mail.Example.Test/mcp", "https://mail.example.test/mcp")]
    [InlineData("https://mail.example.test:443/mcp", "https://mail.example.test/mcp")]
    [InlineData("https://mail.example.test:8443/mcp", "https://mail.example.test:8443/mcp")]
    [InlineData("  https://mail.example.test/mcp  ", "https://mail.example.test/mcp")]
    public void TryCanonicalize_ASpellingOfOneResource_ProducesTheOneFormEverythingComparesAgainst(
        string configuredValue,
        string expectedCanonicalForm)
    {
        // Arrange, Act
        var wasCanonicalized = OAuthIdentifierUri.TryCanonicalize(configuredValue, out var canonicalIdentifier);

        // Assert
        Assert.True(wasCanonicalized);
        Assert.Equal(expectedCanonicalForm, canonicalIdentifier);
    }

    /// <summary>The MCP authorization specification asks implementations to settle on the form without the trailing slash, and this is where that happens.</summary>
    [Theory]
    [InlineData("https://mail.example.test")]
    [InlineData("https://mail.example.test/")]
    public void TryCanonicalize_AResourceWithNoPath_DropsTheTrailingSlash(string configuredValue)
    {
        // Arrange, Act
        OAuthIdentifierUri.TryCanonicalize(configuredValue, out var canonicalIdentifier);

        // Assert
        Assert.Equal("https://mail.example.test", canonicalIdentifier);
    }

    /// <summary>A trailing slash on a path that identifies something is part of what it identifies, so it survives.</summary>
    [Fact]
    public void TryCanonicalize_AResourceWhosePathEndsInASlash_KeepsIt()
    {
        // Arrange, Act
        OAuthIdentifierUri.TryCanonicalize("https://mail.example.test/mcp/", out var canonicalIdentifier);

        // Assert
        Assert.Equal("https://mail.example.test/mcp/", canonicalIdentifier);
    }

    [Fact]
    public void TryCanonicalize_AValueThatIsNotAnIdentifier_ReportsFailureAndYieldsNothing()
    {
        // Arrange, Act
        var wasCanonicalized = OAuthIdentifierUri.TryCanonicalize("http://mail.example.test", out var canonicalIdentifier);

        // Assert
        Assert.False(wasCanonicalized);
        Assert.Empty(canonicalIdentifier);
    }

    [Fact]
    public void Canonicalize_AValueThatWasNeverValidated_ThrowsRatherThanReturningSomethingUnusable()
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentException>(() => OAuthIdentifierUri.Canonicalize("not-an-identifier"));
    }
}
