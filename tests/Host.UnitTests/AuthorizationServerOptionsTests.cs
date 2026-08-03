// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration;
using MailFathom.Infrastructure.Security;
using Xunit;

namespace MailFathom.Host.UnitTests;

/// <summary>Covers one authorization server profile: what it must state, whose tokens it serves, and where it then looks for that server.</summary>
public sealed class AuthorizationServerOptionsTests
{
    private const string OwnerSubject = "9f2c";

    [Fact]
    public void FindConfigurationErrors_ANamedProfileWithAnIssuer_IsAccepted()
    {
        // Arrange
        var profile = Profile("workforce", "https://sso.example.test/realms/mailfathom");

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
        var profile = Profile(name, "https://sso.example.test/realms/mailfathom");

        // Act
        var error = Assert.Single(profile.FindConfigurationErrors());

        // Assert
        Assert.StartsWith("Name", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("sso.example.test")]
    [InlineData("http://sso.example.test")]
    [InlineData("https://sso.example.test?realm=mailfathom")]
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
        var profile = Profile("workforce", "https://sso.example.test/realms/mailfathom");

        // Act
        var addresses = profile.MetadataAddresses();

        // Assert
        Assert.Equal(
            [
                "https://sso.example.test/.well-known/oauth-authorization-server/realms/mailfathom",
                "https://sso.example.test/.well-known/openid-configuration/realms/mailfathom",
                "https://sso.example.test/realms/mailfathom/.well-known/openid-configuration",
            ],
            addresses);
    }

    [Fact]
    public void MetadataAddresses_AnOverride_LooksNowhereElse()
    {
        // Arrange
        var profile = Profile("workforce", "https://sso.example.test/realms/mailfathom");
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
        var profile = Profile("workforce", "https://sso.example.test/realms/mailfathom");
        profile.MetadataAddress = metadataAddress;

        // Act
        var error = Assert.Single(profile.FindConfigurationErrors());

        // Assert
        Assert.StartsWith("MetadataAddress", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// A mistyped issuer is the one setting whose value can carry user information or a query, and the message reporting
    /// it is recorded through the startup failure log. Naming the setting is what an operator needs; copying the value
    /// there is what would export a credential somebody pasted into the wrong place.
    /// </summary>
    [Fact]
    public void FindConfigurationErrors_AMalformedIssuer_IsReportedWithoutQuotingTheValue()
    {
        // Arrange
        var profile = Profile("workforce", "https://operator:s3cret@sso.example.test/realms/mailfathom?token=abc");

        // Act
        var error = Assert.Single(profile.FindConfigurationErrors());

        // Assert
        Assert.StartsWith("Issuer", error, StringComparison.Ordinal);
        Assert.DoesNotContain("s3cret", error, StringComparison.Ordinal);
        Assert.DoesNotContain("sso.example.test", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// A tenant holds whoever the operator's identity platform holds, and every subject able to obtain a token for this
    /// resource would otherwise read the configured owner's mail. The profile therefore states whose tokens it serves.
    /// </summary>
    [Fact]
    public void FindConfigurationErrors_AProfileNamingNoSubject_IsRefused()
    {
        // Arrange
        var profile = new AuthorizationServerOptions
        {
            Name = "workforce",
            Issuer = "https://sso.example.test/realms/mailfathom",
        };

        // Act
        var error = Assert.Single(profile.FindConfigurationErrors());

        // Assert
        Assert.StartsWith("AuthorizedSubjects", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void FindConfigurationErrors_ABlankSubject_IsRefusedByItsPosition(string subject)
    {
        // Arrange
        var profile = Profile("workforce", "https://sso.example.test/realms/mailfathom");
        profile.AuthorizedSubjects.Add(subject);

        // Act
        var error = Assert.Single(profile.FindConfigurationErrors());

        // Assert
        Assert.StartsWith("AuthorizedSubjects:1", error, StringComparison.Ordinal);
    }

    [Fact]
    public void FindConfigurationErrors_ARepeatedSubject_IsRefused()
    {
        // Arrange
        var profile = Profile("workforce", "https://sso.example.test/realms/mailfathom");
        profile.AuthorizedSubjects.Add(OwnerSubject);

        // Act
        var error = Assert.Single(profile.FindConfigurationErrors());

        // Assert
        Assert.StartsWith("AuthorizedSubjects:1", error, StringComparison.Ordinal);
    }

    /// <summary>A subject is unique only within the server that issued it, so what the policy compares is the pair.</summary>
    [Fact]
    public void AuthorizedIdentities_AConfiguredSubject_IsPairedWithTheProfilesIssuer()
    {
        // Arrange
        var profile = Profile("workforce", "https://sso.example.test/realms/mailfathom");
        profile.AuthorizedSubjects.Add("  4b81  ");

        // Act
        var identities = profile.AuthorizedIdentities();

        // Assert
        Assert.Equal(
            [
                OAuthIdentity.IdentityOf("https://sso.example.test/realms/mailfathom", OwnerSubject),
                OAuthIdentity.IdentityOf("https://sso.example.test/realms/mailfathom", "4b81"),
            ],
            identities);
    }

    [Fact]
    public void IsConfigured_AnUntouchedProfile_ReportsNothingWasWritten()
    {
        // Arrange, Act
        var profile = new AuthorizationServerOptions();

        // Assert
        Assert.False(profile.IsConfigured);
    }

    /// <summary>A profile carrying only subjects is a profile an operator started writing, so it is validated rather than skipped.</summary>
    [Fact]
    public void IsConfigured_AProfileCarryingOnlySubjects_ReportsSomethingWasWritten()
    {
        // Arrange
        var profile = new AuthorizationServerOptions();

        // Act
        profile.AuthorizedSubjects.Add(OwnerSubject);

        // Assert
        Assert.True(profile.IsConfigured);
    }

    private static AuthorizationServerOptions Profile(string? name, string? issuer) =>
        new() { Name = name, Issuer = issuer, AuthorizedSubjects = { OwnerSubject } };
}
