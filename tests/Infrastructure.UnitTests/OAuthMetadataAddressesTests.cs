// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Infrastructure.Security;
using Xunit;

namespace MailMcp.Infrastructure.UnitTests;

/// <summary>Covers where an authorization server's discovery document is looked for, and in which order.</summary>
/// <remarks>
/// The order is the whole point. Two specifications place the document differently once an issuer has a path, so a
/// resource server that tried only one of them would work against half the servers in deployment and fail to configure
/// against the other half for no reason an operator could see.
/// </remarks>
public sealed class OAuthMetadataAddressesTests
{
    /// <summary>With no path to insert the well-known segment before, the two specifications place the document identically, so there is no third address to try.</summary>
    [Fact]
    public void ForIssuer_AnIssuerWithNoPath_LooksInTheTwoStandardPlaces()
    {
        // Arrange, Act
        var addresses = OAuthMetadataAddresses.ForIssuer("https://sso.example.test");

        // Assert
        Assert.Equal(
            [
                "https://sso.example.test/.well-known/oauth-authorization-server",
                "https://sso.example.test/.well-known/openid-configuration",
            ],
            addresses);
    }

    /// <summary>A trailing slash is not a path, so it must not produce a third address carrying a doubled separator.</summary>
    [Fact]
    public void ForIssuer_AnIssuerWhosePathIsOneTrailingSlash_IsTreatedAsHavingNoPath()
    {
        // Arrange, Act
        var addresses = OAuthMetadataAddresses.ForIssuer("https://tenant.identity.example.test/");

        // Assert
        Assert.Equal(
            [
                "https://tenant.identity.example.test/.well-known/oauth-authorization-server",
                "https://tenant.identity.example.test/.well-known/openid-configuration",
            ],
            addresses);
    }

    /// <summary>The OAuth form and the OpenID Connect form insert the segment before the path; older OpenID providers append it after.</summary>
    [Fact]
    public void ForIssuer_AnIssuerWithAPath_TriesBothInsertionsBeforeTheAppendedForm()
    {
        // Arrange, Act
        var addresses = OAuthMetadataAddresses.ForIssuer("https://sso.example.test/realms/mailmcp");

        // Assert
        Assert.Equal(
            [
                "https://sso.example.test/.well-known/oauth-authorization-server/realms/mailmcp",
                "https://sso.example.test/.well-known/openid-configuration/realms/mailmcp",
                "https://sso.example.test/realms/mailmcp/.well-known/openid-configuration",
            ],
            addresses);
    }

    [Fact]
    public void ForIssuer_AnIssuerOnANonDefaultPort_KeepsThePortOnEveryAddress()
    {
        // Arrange, Act
        var addresses = OAuthMetadataAddresses.ForIssuer("https://sso.example.test:8443/realms/mailmcp");

        // Assert
        Assert.All(addresses, address => Assert.StartsWith("https://sso.example.test:8443/", address, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://sso.example.test")]
    [InlineData("sso.example.test")]
    public void ForIssuer_AnIssuerThatWasNeverValidated_ThrowsRatherThanComposingAnAddressToFetch(string? issuer)
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentException>(() => OAuthMetadataAddresses.ForIssuer(issuer));
    }
}
