// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Host.Configuration;
using MailMcp.Infrastructure.Secrets;
using MailMcp.Infrastructure.Security;
using Xunit;

namespace MailMcp.Host.UnitTests;

/// <summary>Covers what a client certificate profile must state before a deployment can start with it.</summary>
public sealed class McpClientCertificateProfileOptionsTests
{
    [Fact]
    public void FindConfigurationErrors_AProfileNamingItsClientItsAuthorityAndItsRequirement_ReportsNothing()
    {
        // Arrange
        var profile = ConnectorProfile();

        // Act, Assert
        Assert.Empty(profile.FindConfigurationErrors());
    }

    /// <summary>Both candidate defaults are postures: one would lock out every other client, the other would report a control nobody configured.</summary>
    [Fact]
    public void FindConfigurationErrors_AProfileNamingNoRequirement_IsRefused()
    {
        // Arrange
        var profile = ConnectorProfile();
        profile.Requirement = null;

        // Act
        var error = Assert.Single(profile.FindConfigurationErrors());

        // Assert
        Assert.StartsWith(nameof(McpClientCertificateProfileOptions.Requirement), error, StringComparison.Ordinal);
    }

    /// <summary>The binder accepts any number for an enum, and a value no member declares would be judged by a rule nobody wrote.</summary>
    [Fact]
    public void FindConfigurationErrors_ARequirementThatIsNeitherOfTheTwo_IsRefused()
    {
        // Arrange
        var profile = ConnectorProfile();
        profile.Requirement = (McpClientCertificateRequirement)7;

        // Act
        var error = Assert.Single(profile.FindConfigurationErrors());

        // Assert
        Assert.Contains("names no requirement", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("chatgpt connector")]
    public void FindConfigurationErrors_AProfileWithNoUsableName_IsRefused(string configuredName)
    {
        // Arrange
        var profile = ConnectorProfile();
        profile.Name = configuredName;

        // Act
        var error = Assert.Single(profile.FindConfigurationErrors());

        // Assert
        Assert.StartsWith(nameof(McpClientCertificateProfileOptions.Name), error, StringComparison.Ordinal);
    }

    [Fact]
    public void FindConfigurationErrors_AProfileTrustingNoAuthority_IsRefused()
    {
        // Arrange
        var profile = ConnectorProfile();
        profile.TrustAnchors.Clear();

        // Act
        var error = Assert.Single(profile.FindConfigurationErrors());

        // Assert
        Assert.StartsWith(nameof(McpClientCertificateProfileOptions.TrustAnchors), error, StringComparison.Ordinal);
    }

    /// <summary>An authority alone accepts every certificate it has ever issued, which for a public one is most of the internet.</summary>
    [Fact]
    public void FindConfigurationErrors_AProfileNamingNoClient_IsRefused()
    {
        // Arrange
        var profile = ConnectorProfile();
        profile.SubjectAlternativeNames.Clear();

        // Act
        var error = Assert.Single(profile.FindConfigurationErrors());

        // Assert
        Assert.StartsWith(nameof(McpClientCertificateProfileOptions.SubjectAlternativeNames), error, StringComparison.Ordinal);
    }

    [Fact]
    public void FindConfigurationErrors_SomethingThatIsNotADnsName_IsReportedAgainstItsPositionInTheList()
    {
        // Arrange
        var profile = ConnectorProfile();
        profile.SubjectAlternativeNames.Add("https://mtls.prod.connectors.openai.com");

        // Act
        var error = Assert.Single(profile.FindConfigurationErrors());

        // Assert
        Assert.StartsWith($"{nameof(McpClientCertificateProfileOptions.SubjectAlternativeNames)}:1", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ToTrustProfile_AValidatedProfile_CarriesWhatWasConfigured()
    {
        // Arrange
        var profile = ConnectorProfile();

        // Act
        var trustProfile = profile.ToTrustProfile();

        // Assert
        Assert.Equal("chatgpt-connector", trustProfile.Name);
        Assert.Equal(McpClientCertificateRequirement.Required, trustProfile.Requirement);
        Assert.Equal(["mtls.prod.connectors.openai.com"], trustProfile.ExpectedDnsNames);
        Assert.Equal(
            ["file:/run/secrets/openai-connectors-ca.pem"],
            trustProfile.TrustAnchors.Select(anchor => anchor.SecretReference));
    }

    [Fact]
    public void ToTrustProfile_SettingsThatWereNeverValidated_ThrowsRatherThanTrustingLessThanWasConfigured()
    {
        // Arrange
        var profile = ConnectorProfile();
        profile.SubjectAlternativeNames.Clear();

        // Act, Assert
        Assert.Throws<InvalidOperationException>(profile.ToTrustProfile);
    }

    private static McpClientCertificateProfileOptions ConnectorProfile()
    {
        var profile = new McpClientCertificateProfileOptions
        {
            Name = "chatgpt-connector",
            Requirement = McpClientCertificateRequirement.Required,
        };

        profile.TrustAnchors.Add(new ConfiguredSecret
        {
            Name = "openai-connectors-ca",
            SecretReference = "file:/run/secrets/openai-connectors-ca.pem",
        });
        profile.SubjectAlternativeNames.Add("mtls.prod.connectors.openai.com");

        return profile;
    }
}
