// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Host.Configuration;
using Xunit;

namespace MailMcp.Host.UnitTests;

/// <summary>Covers one authorization server profile: what it must state, and where it then looks for that server.</summary>
public sealed class McpAuthorizationServerOptionsTests
{
    [Fact]
    public void FindConfigurationErrors_ANamedProfileWithAnIssuer_IsAccepted()
    {
        // Arrange
        var profile = Profile("workforce", "https://sso.example.test/realms/mailmcp");

        // Act, Assert
        Assert.Empty(profile.FindConfigurationErrors());
    }

    /// <summary>A startup message and a log line identify a profile by its name rather than by its issuer, which names the operator's identity provider.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FindConfigurationErrors_AProfileWithNoName_IsRefused(string? name)
    {
        // Arrange
        var profile = Profile(name, "https://sso.example.test/realms/mailmcp");

        // Act
        var error = Assert.Single(profile.FindConfigurationErrors());

        // Assert
        Assert.StartsWith("Name", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("sso.example.test")]
    [InlineData("http://sso.example.test")]
    [InlineData("https://sso.example.test?realm=mailmcp")]
    public void FindConfigurationErrors_AProfileWhoseIssuerIsNotAnIdentifier_IsRefused(string? issuer)
    {
        // Arrange
        var profile = Profile("workforce", issuer);

        // Act
        var error = Assert.Single(profile.FindConfigurationErrors());

        // Assert
        Assert.StartsWith("Issuer", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// The issuer is compared against what the authorization server emits, so it survives configuration exactly as it was
    /// copied. Servers publishing an issuer whose path is one trailing slash are the reason this is not tidied away.
    /// </summary>
    [Fact]
    public void ValidatedIssuer_AnIssuerEndingInASlash_IsUsedExactlyAsConfigured()
    {
        // Arrange
        var profile = Profile("tenant", "  https://tenant.identity.example.test/  ");

        // Act, Assert
        Assert.Equal("https://tenant.identity.example.test/", profile.ValidatedIssuer());
    }

    [Fact]
    public void MetadataAddresses_NoOverride_LooksWhereTheSpecificationSaysTo()
    {
        // Arrange
        var profile = Profile("workforce", "https://sso.example.test/realms/mailmcp");

        // Act
        var addresses = profile.MetadataAddresses();

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
    public void MetadataAddresses_AnOverride_LooksNowhereElse()
    {
        // Arrange
        var profile = Profile("workforce", "https://sso.example.test/realms/mailmcp");
        profile.MetadataAddress = "https://sso.example.test/metadata.json";

        // Act
        var address = Assert.Single(profile.MetadataAddresses());

        // Assert
        Assert.Equal("https://sso.example.test/metadata.json", address);
    }

    /// <summary>
    /// The metadata address is the one setting naming something the host will fetch, on a schedule nobody watches. Tying
    /// it to the issuer's authority means a mistyped one cannot make the host reach an address the profile never named.
    /// </summary>
    [Theory]
    [InlineData("https://internal.example.test/metadata.json")]
    [InlineData("https://sso.example.test:9443/metadata.json")]
    [InlineData("http://sso.example.test/metadata.json")]
    [InlineData("not-a-url")]
    public void FindConfigurationErrors_AMetadataAddressAwayFromTheIssuersServer_IsRefused(string metadataAddress)
    {
        // Arrange
        var profile = Profile("workforce", "https://sso.example.test/realms/mailmcp");
        profile.MetadataAddress = metadataAddress;

        // Act
        var error = Assert.Single(profile.FindConfigurationErrors());

        // Assert
        Assert.StartsWith("MetadataAddress", error, StringComparison.Ordinal);
    }

    [Fact]
    public void IsConfigured_AnUntouchedProfile_ReportsNothingWasWritten()
    {
        // Arrange, Act
        var profile = new McpAuthorizationServerOptions();

        // Assert
        Assert.False(profile.IsConfigured);
    }

    private static McpAuthorizationServerOptions Profile(string? name, string? issuer) =>
        new() { Name = name, Issuer = issuer };
}
