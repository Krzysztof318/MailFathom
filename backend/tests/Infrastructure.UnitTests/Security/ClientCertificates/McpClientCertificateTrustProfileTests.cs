// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.Infrastructure.Security.ClientCertificates;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Security.ClientCertificates;

/// <summary>Covers what a profile has to carry before it can identify a client, and what it answers about a name.</summary>
public sealed class McpClientCertificateTrustProfileTests
{
    [Theory]
    [InlineData("chatgpt-connector")]
    [InlineData("reporting.service")]
    [InlineData("client_2")]
    public void IsAcceptedName_ANameSafeToRecord_IsAccepted(string configuredValue)
    {
        // Arrange, Act, Assert
        Assert.True(McpClientCertificateTrustProfile.IsAcceptedName(configuredValue));
    }

    /// <summary>A name reaches a log line and an audit record, so escaping or truncation must never be what decides its meaning.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("-leading-dash")]
    [InlineData("carries a space")]
    [InlineData("carries\na newline")]
    public void IsAcceptedName_AnythingElse_IsRefused(string? configuredValue)
    {
        // Arrange, Act, Assert
        Assert.False(McpClientCertificateTrustProfile.IsAcceptedName(configuredValue));
    }

    [Fact]
    public void IsAcceptedName_ANameLongerThanTheMaximum_IsRefused()
    {
        // Arrange
        var configuredValue = new string('a', McpClientCertificateTrustProfile.MaximumNameLength + 1);

        // Act, Assert
        Assert.False(McpClientCertificateTrustProfile.IsAcceptedName(configuredValue));
    }

    /// <summary>Only DNS names are compared, so anything else would be a profile written against a name nothing reads.</summary>
    [Theory]
    [InlineData("mtls.prod.connectors.openai.com", true)]
    [InlineData("client", true)]
    [InlineData("192.0.2.10", false)]
    [InlineData("https://client.example.test", false)]
    [InlineData("", false)]
    public void IsAcceptedDnsName_AConfiguredValue_IsAcceptedOnlyWhenItIsAHostName(string configuredValue, bool accepted)
    {
        // Arrange, Act, Assert
        Assert.Equal(accepted, McpClientCertificateTrustProfile.IsAcceptedDnsName(configuredValue));
    }

    [Fact]
    public void Create_AProfileNamingItsClientAndItsAuthority_CarriesBoth()
    {
        // Arrange, Act
        var profile = McpClientCertificateTrustProfile.Create(
            "chatgpt-connector",
            McpClientCertificateRequirement.Required,
            [Anchor()],
            ["mtls.prod.connectors.openai.com", "MTLS.PROD.CONNECTORS.OPENAI.COM"]);

        // Assert
        Assert.Equal("chatgpt-connector", profile.Name);
        Assert.Equal(McpClientCertificateRequirement.Required, profile.Requirement);
        Assert.Single(profile.TrustAnchors);
        Assert.Single(profile.ExpectedDnsNames);
        Assert.True(profile.NamesClient(["mtls.prod.connectors.openai.com"]));
    }

    /// <summary>A host name is case-insensitive, and a certificate carrying any of the expected names is the client.</summary>
    [Fact]
    public void NamesClient_ACertificateCarryingOneOfSeveralExpectedNames_IsTheClient()
    {
        // Arrange
        var profile = McpClientCertificateTrustProfile.Create(
            "reporting-service",
            McpClientCertificateRequirement.Optional,
            [Anchor()],
            ["reporting.example.test", "reporting-standby.example.test"]);

        // Act, Assert
        Assert.True(profile.NamesClient(["other.example.test", "REPORTING-STANDBY.example.test"]));
        Assert.False(profile.NamesClient(["someone-else.example.test"]));
        Assert.False(profile.NamesClient([]));
    }

    /// <summary>Every one of these means the profile was mapped before it was validated, which the options rules refuse first.</summary>
    [Theory]
    [InlineData("", true, true)]
    [InlineData("chatgpt connector", true, true)]
    [InlineData("chatgpt-connector", false, true)]
    [InlineData("chatgpt-connector", true, false)]
    public void Create_SettingsThatCouldNotIdentifyAClient_Throws(
        string name,
        bool carriesAnAnchor,
        bool carriesADnsName)
    {
        // Arrange
        ConfiguredSecret[] anchors = carriesAnAnchor ? [Anchor()] : [];
        string[] dnsNames = carriesADnsName ? ["client.example.test"] : [];

        // Act, Assert
        Assert.Throws<ArgumentException>(() => McpClientCertificateTrustProfile.Create(
            name,
            McpClientCertificateRequirement.Optional,
            anchors,
            dnsNames));
    }

    private static ConfiguredSecret Anchor() => new()
    {
        Name = "connector-ca",
        SecretReference = "file:/run/secrets/connector-ca.pem",
    };
}
