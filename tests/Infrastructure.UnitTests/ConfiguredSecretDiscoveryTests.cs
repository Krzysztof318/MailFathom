// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Domain.Transport;
using MailMcp.Infrastructure.Mail;
using MailMcp.Infrastructure.Secrets;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MailMcp.Infrastructure.UnitTests;

public sealed class ConfiguredSecretDiscoveryTests
{
    [Fact]
    public void FindSecretBearingSettings_BlockAtTheRoot_ReportsItsPath()
    {
        // Arrange
        var options = new AccountOptionsUnderTest
        {
            Secrets = new MailAccountSecretOptions
            {
                Password = new ConfiguredSecret { SecretReference = "systemd-credential:imap" },
            },
        };

        // Act
        var discovered = ConfiguredSecretDiscovery.FindSecretBearingSettings(options, "MailSynchronization");

        // Assert
        Assert.Equal(["MailSynchronization:Secrets:Password"], discovered.Blocks.Select(block => block.ConfigurationPath));
    }

    [Fact]
    public void FindSecretBearingSettings_BlockInsideAList_ReportsItsIndexInThePath()
    {
        // Arrange
        var options = new RootOptionsUnderTest
        {
            Accounts =
            [
                new AccountOptionsUnderTest(),
                new AccountOptionsUnderTest(),
            ],
        };

        // Act
        var discovered = ConfiguredSecretDiscovery.FindSecretBearingSettings(options, "MailSynchronization");

        // Assert
        Assert.Equal(
            [
                "MailSynchronization:Accounts:0:Secrets:Password",
                "MailSynchronization:Accounts:1:Secrets:Password",
            ],
            discovered.Blocks.Select(block => block.ConfigurationPath));
    }

    [Fact]
    public void FindSecretBearingSettings_BlockWithItsOwnPasswordBlock_ReportsBoth()
    {
        // Arrange
        var options = new AccountOptionsUnderTest
        {
            TransportSecurity = new MailAccountTransportSecurityOptions
            {
                CertificateTrust = MailServerCertificateTrust.AdditionalTrustedAuthority,
                TrustedCertificateAuthority = new ConfiguredSecret
                {
                    SecretReference = "file:/run/secrets/client.pfx",
                    Password = new ConfiguredSecret { SecretReference = "systemd-credential:bundle-password" },
                },
            },
        };

        // Act
        var discovered = ConfiguredSecretDiscovery.FindSecretBearingSettings(options, "Account");

        // Assert
        Assert.Contains("Account:TransportSecurity:TrustedCertificateAuthority", discovered.Blocks.Select(block => block.ConfigurationPath));
        Assert.Contains("Account:TransportSecurity:TrustedCertificateAuthority:Password", discovered.Blocks.Select(block => block.ConfigurationPath));
    }

    [Fact]
    public void FindSecretBearingSettings_AbsentBlock_IsSkippedRatherThanReportedAsMissing()
    {
        // Arrange
        var options = new AccountOptionsUnderTest
        {
            TransportSecurity = new MailAccountTransportSecurityOptions(),
        };

        // Act
        var discovered = ConfiguredSecretDiscovery.FindSecretBearingSettings(options, "Account");

        // Assert
        Assert.DoesNotContain(
            "Account:TransportSecurity:TrustedCertificateAuthority",
            discovered.Blocks.Select(block => block.ConfigurationPath));
    }

    [Fact]
    public void FindSecretBearingSettings_StringPropertyNamedForASecret_IsReportedAsARawSecretProperty()
    {
        // Arrange
        var options = new RawSecretOptionsUnderTest { ApiToken = "not-a-block" };

        // Act
        var discovered = ConfiguredSecretDiscovery.FindSecretBearingSettings(options, "Integration");

        // Assert
        Assert.Equal(["Integration:ApiToken"], discovered.RawSecretPropertyPaths);
    }

    [Fact]
    public void FindSecretBearingSettings_StringPropertyNotNamedForASecret_IsNotReported()
    {
        // Arrange
        var options = new PlainOptionsUnderTest { Endpoint = "https://example.test" };

        // Act
        var discovered = ConfiguredSecretDiscovery.FindSecretBearingSettings(options, "Integration");

        // Assert
        Assert.Empty(discovered.RawSecretPropertyPaths);
    }

    [Fact]
    public void FindSecretBearingSettings_SecretReferencePropertyOfABlock_IsNotReportedAsARawSecretProperty()
    {
        // Arrange
        var options = new AccountOptionsUnderTest();

        // Act
        var discovered = ConfiguredSecretDiscovery.FindSecretBearingSettings(options, "Account");

        // Assert
        Assert.Empty(discovered.RawSecretPropertyPaths);
    }

    [Fact]
    public void FindSecretBearingSettings_CyclicGraph_Terminates()
    {
        // Arrange
        var first = new CyclicOptionsUnderTest();
        var second = new CyclicOptionsUnderTest { Next = first };
        first.Next = second;

        // Act
        var discovered = ConfiguredSecretDiscovery.FindSecretBearingSettings(first, "Cyclic");

        // Assert
        Assert.Equal(2, discovered.Blocks.Count);
    }

    [Fact]
    public void FindSecretBearingSettings_OptionsBoundFromFlatColonSeparatedKeys_ReportsTheSamePaths()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MailSynchronization:Accounts:0:AccountId"] = "primary",
                ["MailSynchronization:Accounts:0:Secrets:Password:SecretReference"] = "systemd-credential:imap-primary-password",
                ["MailSynchronization:Accounts:0:TransportSecurity:CertificateTrust"] = "AdditionalTrustedAuthority",
                ["MailSynchronization:Accounts:0:TransportSecurity:TrustedCertificateAuthority:SecretReference"] = "file:/run/secrets/private-ca.pem",
            })
            .Build();
        var options = configuration.GetSection("MailSynchronization").Get<RootOptionsUnderTest>()!;

        // Act
        var discovered = ConfiguredSecretDiscovery.FindSecretBearingSettings(options, "MailSynchronization");

        // Assert
        Assert.Equal(
            [
                "MailSynchronization:Accounts:0:Secrets:Password",
                "MailSynchronization:Accounts:0:TransportSecurity:TrustedCertificateAuthority",
            ],
            discovered.Blocks.Select(block => block.ConfigurationPath).Order(StringComparer.Ordinal));
        Assert.Equal(
            "systemd-credential:imap-primary-password",
            discovered.Blocks.Single(block => block.ConfigurationPath.EndsWith("Secrets:Password", StringComparison.Ordinal)).Secret.SecretReference);
    }

    private sealed class RootOptionsUnderTest
    {
        public List<AccountOptionsUnderTest> Accounts { get; set; } = [];
    }

    private sealed class AccountOptionsUnderTest
    {
        public string AccountId { get; set; } = string.Empty;

        public MailAccountSecretOptions Secrets { get; set; } = new();

        public MailAccountTransportSecurityOptions TransportSecurity { get; set; } = new();
    }

    private sealed class RawSecretOptionsUnderTest
    {
        public string ApiToken { get; set; } = string.Empty;
    }

    private sealed class PlainOptionsUnderTest
    {
        public string Endpoint { get; set; } = string.Empty;
    }

    private sealed class CyclicOptionsUnderTest
    {
        public ConfiguredSecret Password { get; set; } = new();

        public CyclicOptionsUnderTest? Next { get; set; }
    }
}
