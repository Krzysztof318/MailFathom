// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Domain.Transport;
using MailMcp.Infrastructure.Mail;
using Xunit;

namespace MailMcp.Infrastructure.UnitTests;

public sealed class MailAccountTransportSecurityOptionsTests
{
    [Fact]
    public void EffectivePermittedAuthenticationMechanisms_MechanismsOmitted_AppliesThePostBindingDefault()
    {
        // Arrange
        var options = new MailAccountTransportSecurityOptions();

        // Act
        var mechanisms = options.EffectivePermittedAuthenticationMechanisms;

        // Assert
        Assert.Equal(["PLAIN", "LOGIN"], mechanisms);
    }

    [Fact]
    public void EffectivePermittedAuthenticationMechanisms_MechanismsConfigured_ReplacesTheDefaultInsteadOfExtendingIt()
    {
        // Arrange
        var options = new MailAccountTransportSecurityOptions
        {
            PermittedAuthenticationMechanisms = ["SCRAM-SHA-256"],
        };

        // Act
        var mechanisms = options.EffectivePermittedAuthenticationMechanisms;

        // Assert
        Assert.Equal(["SCRAM-SHA-256"], mechanisms);
    }

    [Fact]
    public void FindConfigurationErrors_DefaultSettings_ReportsNoError()
    {
        // Arrange
        var options = new MailAccountTransportSecurityOptions();

        // Act
        var errors = options.FindConfigurationErrors();

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public void FindConfigurationErrors_UnencryptedConnectionWithoutOptIn_ReportsTheConnectionSecuritySetting()
    {
        // Arrange
        var options = new MailAccountTransportSecurityOptions
        {
            ConnectionSecurity = MailConnectionSecurity.None,
            PermittedAuthenticationMechanisms = ["SCRAM-SHA-256"],
        };

        // Act
        var errors = options.FindConfigurationErrors();

        // Assert
        var error = Assert.Single(errors);
        Assert.Equal(nameof(MailAccountTransportSecurityOptions.ConnectionSecurity), error.PropertyName);
        Assert.Contains("AllowInsecureConnection", error.Description, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(MailConnectionSecurity.Auto)]
    [InlineData(MailConnectionSecurity.StartTlsWhenAvailable)]
    public void FindConfigurationErrors_OpportunisticEncryptionWithoutOptIn_ReportsAnError(MailConnectionSecurity connectionSecurity)
    {
        // Arrange
        var options = new MailAccountTransportSecurityOptions
        {
            ConnectionSecurity = connectionSecurity,
            PermittedAuthenticationMechanisms = ["SCRAM-SHA-256"],
        };

        // Act
        var errors = options.FindConfigurationErrors();

        // Assert
        var error = Assert.Single(errors);
        Assert.Contains("continues unencrypted", error.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void FindConfigurationErrors_DefaultMechanismsOnAcceptedUnencryptedConnection_StillRequiresTheClearTextOptIn()
    {
        // Arrange
        var options = new MailAccountTransportSecurityOptions
        {
            ConnectionSecurity = MailConnectionSecurity.None,
            AllowInsecureConnection = true,
        };

        // Act
        var errors = options.FindConfigurationErrors();

        // Assert
        var error = Assert.Single(errors);
        Assert.Contains("AllowClearTextAuthenticationOverUnencryptedConnection", error.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void FindConfigurationErrors_ClearTextOnUnencryptedConnectionWithBothOptIns_ReportsNoError()
    {
        // Arrange
        var options = new MailAccountTransportSecurityOptions
        {
            ConnectionSecurity = MailConnectionSecurity.None,
            AllowInsecureConnection = true,
            AllowClearTextAuthenticationOverUnencryptedConnection = true,
        };

        // Act
        var errors = options.FindConfigurationErrors();

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public void FindConfigurationErrors_UnsupportedMechanismName_ReportsItAndTheEmptyAllowList()
    {
        // Arrange
        var options = new MailAccountTransportSecurityOptions
        {
            PermittedAuthenticationMechanisms = ["GSSAPI"],
        };

        // Act
        var errors = options.FindConfigurationErrors();

        // Assert
        Assert.Equal(
            [
                "SASL mechanism 'GSSAPI' is not supported.",
                "At least one supported SASL mechanism must be permitted.",
            ],
            errors.Select(error => error.Description));
    }

    [Fact]
    public void FindConfigurationErrors_AdditionalTrustedAuthorityWithoutReference_ReportsTheReferenceSetting()
    {
        // Arrange
        var options = new MailAccountTransportSecurityOptions
        {
            CertificateTrust = MailServerCertificateTrust.AdditionalTrustedAuthority,
        };

        // Act
        var errors = options.FindConfigurationErrors();

        // Assert
        var error = Assert.Single(errors);
        Assert.Equal(nameof(MailAccountTransportSecurityOptions.TrustedCertificateAuthorityReference), error.PropertyName);
    }

    [Fact]
    public void FindConfigurationErrors_SystemTrustStoreWithReference_ReportsTheReferenceSetting()
    {
        // Arrange
        var options = new MailAccountTransportSecurityOptions
        {
            TrustedCertificateAuthorityReference = "mailmcp-imap-ca",
        };

        // Act
        var errors = options.FindConfigurationErrors();

        // Assert
        var error = Assert.Single(errors);
        Assert.Equal(nameof(MailAccountTransportSecurityOptions.TrustedCertificateAuthorityReference), error.PropertyName);
        Assert.DoesNotContain("mailmcp-imap-ca", error.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void FindConfigurationErrors_UndefinedCertificateTrust_ReportsTheCertificateTrustSetting()
    {
        // Arrange
        var options = new MailAccountTransportSecurityOptions
        {
            CertificateTrust = (MailServerCertificateTrust)99,
        };

        // Act
        var errors = options.FindConfigurationErrors();

        // Assert
        var error = Assert.Single(errors);
        Assert.Equal(nameof(MailAccountTransportSecurityOptions.CertificateTrust), error.PropertyName);
    }

    [Fact]
    public void FindConfigurationErrors_UndefinedConnectionSecurity_ReportsTheConnectionSecuritySetting()
    {
        // Arrange
        var options = new MailAccountTransportSecurityOptions
        {
            ConnectionSecurity = (MailConnectionSecurity)99,
        };

        // Act
        var errors = options.FindConfigurationErrors();

        // Assert
        var error = Assert.Single(errors);
        Assert.Equal(nameof(MailAccountTransportSecurityOptions.ConnectionSecurity), error.PropertyName);
    }

    [Fact]
    public void CreatePolicy_SafeSettings_MapsEveryValueOntoTheDomainPolicy()
    {
        // Arrange
        var options = new MailAccountTransportSecurityOptions
        {
            ConnectionSecurity = MailConnectionSecurity.StartTlsRequired,
            PermittedAuthenticationMechanisms = ["scram-sha-256", "SCRAM-SHA-256", "PLAIN"],
            CertificateTrust = MailServerCertificateTrust.AdditionalTrustedAuthority,
            TrustedCertificateAuthorityReference = " mailmcp-imap-ca ",
        };

        // Act
        var policy = options.CreatePolicy();

        // Assert
        Assert.Equal(MailConnectionSecurity.StartTlsRequired, policy.ConnectionSecurity);
        Assert.Equal(
            [MailAuthenticationMechanism.ScramSha256, MailAuthenticationMechanism.Plain],
            policy.Authentication.PermittedMechanisms);
        Assert.Equal("mailmcp-imap-ca", policy.TrustedCertificateAuthorityReference);
    }

    [Fact]
    public void CreatePolicy_UnsafeSettings_ThrowsInsteadOfReturningAConnectablePolicy()
    {
        // Arrange
        var options = new MailAccountTransportSecurityOptions
        {
            ConnectionSecurity = MailConnectionSecurity.None,
        };

        // Act, Assert
        Assert.Throws<MailTransportSecurityPolicyViolationException>(options.CreatePolicy);
    }
}
