// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Domain.Transport;
using MailMcp.Infrastructure.Mail;
using MailMcp.Infrastructure.Secrets;
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
        Assert.Equal(MailTransportSecurityViolation.UnencryptedConnectionRequiresExplicitOptIn, error.Violation);
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
    public void FindConfigurationErrors_UnsupportedMechanismName_CarriesNoViolationBecauseItIsAParseFailure()
    {
        // Arrange
        var options = new MailAccountTransportSecurityOptions
        {
            PermittedAuthenticationMechanisms = ["GSSAPI", "SCRAM-SHA-256"],
        };

        // Act
        var errors = options.FindConfigurationErrors();

        // Assert
        var error = Assert.Single(errors);
        Assert.Equal(nameof(MailAccountTransportSecurityOptions.PermittedAuthenticationMechanisms), error.PropertyName);
        Assert.Null(error.Violation);
    }

    [Fact]
    public void FindConfigurationErrors_SeveralViolatedRules_PreservesEveryDomainViolationIdentity()
    {
        // Arrange
        var options = new MailAccountTransportSecurityOptions
        {
            ConnectionSecurity = MailConnectionSecurity.None,
            PermittedAuthenticationMechanisms = ["PLAIN"],
            TrustedCertificateAuthority = new ConfiguredSecret { SecretReference = "systemd-credential:mailmcp-imap-ca" },
        };

        // Act
        var errors = options.FindConfigurationErrors();

        // Assert
        Assert.Equal(
            [
                MailTransportSecurityViolation.UnencryptedConnectionRequiresExplicitOptIn,
                MailTransportSecurityViolation.ClearTextAuthenticationRequiresEncryptedConnection,
                MailTransportSecurityViolation.TrustedCertificateAuthorityReferenceNotApplicable,
            ],
            errors.Select(error => error.Violation));
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
        Assert.Equal(nameof(MailAccountTransportSecurityOptions.TrustedCertificateAuthority), error.PropertyName);
    }

    [Fact]
    public void FindConfigurationErrors_AdditionalTrustedAuthorityWithABlock_ReportsNoError()
    {
        // Arrange
        var options = new MailAccountTransportSecurityOptions
        {
            CertificateTrust = MailServerCertificateTrust.AdditionalTrustedAuthority,
            TrustedCertificateAuthority = new ConfiguredSecret { SecretReference = "file:/run/secrets/private-ca.pem" },
        };

        // Act
        var errors = options.FindConfigurationErrors();

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public void FindConfigurationErrors_AdditionalTrustedAuthorityWithAnEmptyBlock_ReportsTheAnchorAsMissing()
    {
        // Arrange
        var options = new MailAccountTransportSecurityOptions
        {
            CertificateTrust = MailServerCertificateTrust.AdditionalTrustedAuthority,
            TrustedCertificateAuthority = new ConfiguredSecret(),
        };

        // Act
        var errors = options.FindConfigurationErrors();

        // Assert
        var error = Assert.Single(errors);
        Assert.Equal(nameof(MailAccountTransportSecurityOptions.TrustedCertificateAuthority), error.PropertyName);
        Assert.Equal(MailTransportSecurityViolation.TrustedCertificateAuthorityReferenceRequired, error.Violation);
    }

    [Fact]
    public void FindConfigurationErrors_SystemTrustStoreWithReference_ReportsTheReferenceSetting()
    {
        // Arrange
        var options = new MailAccountTransportSecurityOptions
        {
            TrustedCertificateAuthority = new ConfiguredSecret { SecretReference = "systemd-credential:mailmcp-imap-ca" },
        };

        // Act
        var errors = options.FindConfigurationErrors();

        // Assert
        var error = Assert.Single(errors);
        Assert.Equal(nameof(MailAccountTransportSecurityOptions.TrustedCertificateAuthority), error.PropertyName);
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
            TrustedCertificateAuthority = new ConfiguredSecret { SecretReference = "systemd-credential:mailmcp-imap-ca" },
        };

        // Act
        var policy = options.CreatePolicy();

        // Assert
        Assert.Equal(MailConnectionSecurity.StartTlsRequired, policy.ConnectionSecurity);
        Assert.Equal(
            [MailAuthenticationMechanism.ScramSha256, MailAuthenticationMechanism.Plain],
            policy.Authentication.PermittedMechanisms);
        Assert.Equal("systemd-credential:***", policy.TrustedCertificateAuthorityReference);
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
