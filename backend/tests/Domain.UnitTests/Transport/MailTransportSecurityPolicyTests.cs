// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Transport;
using Xunit;

namespace MailFathom.Domain.UnitTests.Transport;

public sealed class MailTransportSecurityPolicyTests
{
    public static TheoryData<MailAuthenticationMechanism> ClearTextMechanisms =>
        [MailAuthenticationMechanism.Plain, MailAuthenticationMechanism.Login];

    public static TheoryData<MailAuthenticationMechanism> ChallengeResponseMechanisms =>
        [
            MailAuthenticationMechanism.CramMd5,
            MailAuthenticationMechanism.DigestMd5,
            MailAuthenticationMechanism.ScramSha1,
            MailAuthenticationMechanism.ScramSha256,
            MailAuthenticationMechanism.ScramSha512,
            MailAuthenticationMechanism.Ntlm,
        ];

    [Theory]
    [InlineData(MailConnectionSecurity.TlsOnConnect, true)]
    [InlineData(MailConnectionSecurity.StartTlsRequired, true)]
    [InlineData(MailConnectionSecurity.Auto, false)]
    [InlineData(MailConnectionSecurity.StartTlsWhenAvailable, false)]
    [InlineData(MailConnectionSecurity.None, false)]
    public void GuaranteesEncryptedChannel_ConnectionSecurityMode_ReportsWhetherEncryptionIsMandatory(
        MailConnectionSecurity connectionSecurity,
        bool expectedGuarantee)
    {
        // Arrange, Act
        var guaranteesEncryptedChannel = MailTransportSecurityPolicy.GuaranteesEncryptedChannel(connectionSecurity);

        // Assert
        Assert.Equal(expectedGuarantee, guaranteesEncryptedChannel);
    }

    [Fact]
    public void FindViolations_UnencryptedConnectionWithoutOptIn_RequiresExplicitOptIn()
    {
        // Arrange, Act
        var violations = FindViolations(
            MailConnectionSecurity.None,
            [MailAuthenticationMechanism.ScramSha256],
            allowInsecureConnection: false,
            allowClearTextAuthenticationOverUnencryptedConnection: false);

        // Assert
        Assert.Equal([MailTransportSecurityViolation.UnencryptedConnectionRequiresExplicitOptIn], violations);
    }

    [Theory]
    [InlineData(MailConnectionSecurity.Auto)]
    [InlineData(MailConnectionSecurity.StartTlsWhenAvailable)]
    public void FindViolations_OpportunisticEncryptionWithoutOptIn_RequiresExplicitOptIn(MailConnectionSecurity connectionSecurity)
    {
        // Arrange, Act
        var violations = FindViolations(
            connectionSecurity,
            [MailAuthenticationMechanism.ScramSha256],
            allowInsecureConnection: false,
            allowClearTextAuthenticationOverUnencryptedConnection: false);

        // Assert
        Assert.Equal([MailTransportSecurityViolation.OpportunisticEncryptionRequiresExplicitOptIn], violations);
    }

    [Theory]
    [MemberData(nameof(ClearTextMechanisms))]
    public void FindViolations_ClearTextMechanismOnUnencryptedChannelWithOnlyInsecureOptIn_RequiresClearTextOptIn(
        MailAuthenticationMechanism clearTextMechanism)
    {
        // Arrange, Act
        var violations = FindViolations(
            MailConnectionSecurity.None,
            [clearTextMechanism],
            allowInsecureConnection: true,
            allowClearTextAuthenticationOverUnencryptedConnection: false);

        // Assert
        Assert.Equal([MailTransportSecurityViolation.ClearTextAuthenticationRequiresEncryptedConnection], violations);
    }

    [Fact]
    public void FindViolations_ClearTextMechanismOnUnencryptedChannelWithBothOptIns_ReportsNoViolation()
    {
        // Arrange, Act
        var violations = FindViolations(
            MailConnectionSecurity.None,
            [MailAuthenticationMechanism.Plain],
            allowInsecureConnection: true,
            allowClearTextAuthenticationOverUnencryptedConnection: true);

        // Assert
        Assert.Empty(violations);
    }

    [Fact]
    public void FindViolations_ClearTextMechanismWithOnlyClearTextOptIn_StillRequiresInsecureConnectionOptIn()
    {
        // Arrange, Act
        var violations = FindViolations(
            MailConnectionSecurity.None,
            [MailAuthenticationMechanism.Plain],
            allowInsecureConnection: false,
            allowClearTextAuthenticationOverUnencryptedConnection: true);

        // Assert
        Assert.Equal(
            [
                MailTransportSecurityViolation.UnencryptedConnectionRequiresExplicitOptIn,
                MailTransportSecurityViolation.ClearTextAuthenticationRequiresEncryptedConnection,
            ],
            violations);
    }

    [Theory]
    [MemberData(nameof(ChallengeResponseMechanisms))]
    public void FindViolations_ChallengeResponseMechanismOnAcceptedUnencryptedChannel_ReportsNoViolation(
        MailAuthenticationMechanism challengeResponseMechanism)
    {
        // Arrange, Act
        var violations = FindViolations(
            MailConnectionSecurity.None,
            [challengeResponseMechanism],
            allowInsecureConnection: true,
            allowClearTextAuthenticationOverUnencryptedConnection: false);

        // Assert
        Assert.Empty(violations);
    }

    [Theory]
    [InlineData(MailConnectionSecurity.TlsOnConnect)]
    [InlineData(MailConnectionSecurity.StartTlsRequired)]
    public void FindViolations_ClearTextMechanismOnGuaranteedEncryptedChannel_ReportsNoViolation(MailConnectionSecurity connectionSecurity)
    {
        // Arrange, Act
        var violations = FindViolations(
            connectionSecurity,
            [MailAuthenticationMechanism.Plain, MailAuthenticationMechanism.Login],
            allowInsecureConnection: false,
            allowClearTextAuthenticationOverUnencryptedConnection: false);

        // Assert
        Assert.Empty(violations);
    }

    [Fact]
    public void FindViolations_NoPermittedMechanism_RequiresPermittedMechanism()
    {
        // Arrange, Act
        var violations = FindViolations(
            MailConnectionSecurity.TlsOnConnect,
            [],
            allowInsecureConnection: false,
            allowClearTextAuthenticationOverUnencryptedConnection: false);

        // Assert
        Assert.Equal([MailTransportSecurityViolation.PermittedAuthenticationMechanismRequired], violations);
    }

    [Fact]
    public void FindViolations_AdditionalTrustedAuthorityWithoutReference_RequiresReference()
    {
        // Arrange, Act
        var violations = MailTransportSecurityPolicy.FindViolations(
            MailConnectionSecurity.TlsOnConnect,
            [MailAuthenticationMechanism.Plain],
            allowInsecureConnection: false,
            allowClearTextAuthenticationOverUnencryptedConnection: false,
            MailServerCertificateTrust.AdditionalTrustedAuthority,
            trustedCertificateAuthorityReference: "   ");

        // Assert
        Assert.Equal([MailTransportSecurityViolation.TrustedCertificateAuthorityReferenceRequired], violations);
    }

    [Fact]
    public void FindViolations_SystemTrustStoreWithReference_ReportsReferenceNotApplicable()
    {
        // Arrange, Act
        var violations = MailTransportSecurityPolicy.FindViolations(
            MailConnectionSecurity.TlsOnConnect,
            [MailAuthenticationMechanism.Plain],
            allowInsecureConnection: false,
            allowClearTextAuthenticationOverUnencryptedConnection: false,
            MailServerCertificateTrust.SystemTrustStore,
            trustedCertificateAuthorityReference: "mailfathom-imap-ca");

        // Assert
        Assert.Equal([MailTransportSecurityViolation.TrustedCertificateAuthorityReferenceNotApplicable], violations);
    }

    [Fact]
    public void Create_UnsafePolicy_ThrowsWithTheViolatedRules()
    {
        // Arrange
        var authentication = MailAuthenticationPolicy.Create(
            [MailAuthenticationMechanism.Plain],
            allowInsecureConnection: false,
            allowClearTextAuthenticationOverUnencryptedConnection: false);

        // Act
        var exception = Assert.Throws<MailTransportSecurityPolicyViolationException>(() => MailTransportSecurityPolicy.Create(
            MailConnectionSecurity.None,
            authentication,
            MailServerCertificateTrust.SystemTrustStore,
            trustedCertificateAuthorityReference: null));

        // Assert
        Assert.Equal(
            [
                MailTransportSecurityViolation.UnencryptedConnectionRequiresExplicitOptIn,
                MailTransportSecurityViolation.ClearTextAuthenticationRequiresEncryptedConnection,
            ],
            exception.Violations);
    }

    [Fact]
    public void Create_SafePolicy_TrimsTheTrustedCertificateAuthorityReference()
    {
        // Arrange
        var authentication = MailAuthenticationPolicy.Create(
            [MailAuthenticationMechanism.ScramSha256],
            allowInsecureConnection: false,
            allowClearTextAuthenticationOverUnencryptedConnection: false);

        // Act
        var policy = MailTransportSecurityPolicy.Create(
            MailConnectionSecurity.StartTlsRequired,
            authentication,
            MailServerCertificateTrust.AdditionalTrustedAuthority,
            trustedCertificateAuthorityReference: "  mailfathom-imap-ca  ");

        // Assert
        Assert.Equal("mailfathom-imap-ca", policy.TrustedCertificateAuthorityReference);
    }

    [Fact]
    public void Create_SystemTrustStoreWithBlankReference_KeepsTheReferenceAbsent()
    {
        // Arrange
        var authentication = MailAuthenticationPolicy.Create(
            [MailAuthenticationMechanism.ScramSha256],
            allowInsecureConnection: false,
            allowClearTextAuthenticationOverUnencryptedConnection: false);

        // Act
        var policy = MailTransportSecurityPolicy.Create(
            MailConnectionSecurity.TlsOnConnect,
            authentication,
            MailServerCertificateTrust.SystemTrustStore,
            trustedCertificateAuthorityReference: "   ");

        // Assert
        Assert.Null(policy.TrustedCertificateAuthorityReference);
    }

    [Fact]
    public void FindViolations_UndefinedConnectionSecurityMode_ReportsItInsteadOfEvaluatingTheRemainingRules()
    {
        // Arrange
        const MailConnectionSecurity undefinedMode = (MailConnectionSecurity)99;

        // Act
        var violations = FindViolations(
            undefinedMode,
            [MailAuthenticationMechanism.Plain],
            allowInsecureConnection: false,
            allowClearTextAuthenticationOverUnencryptedConnection: false);

        // Assert
        Assert.Equal([MailTransportSecurityViolation.ConnectionSecurityNotSupported], violations);
    }

    [Fact]
    public void FindViolations_UndefinedCertificateTrust_ReportsItInsteadOfAcceptingThePolicy()
    {
        // Arrange
        const MailServerCertificateTrust undefinedTrust = (MailServerCertificateTrust)99;

        // Act
        var violations = MailTransportSecurityPolicy.FindViolations(
            MailConnectionSecurity.TlsOnConnect,
            [MailAuthenticationMechanism.Plain],
            allowInsecureConnection: false,
            allowClearTextAuthenticationOverUnencryptedConnection: false,
            undefinedTrust,
            trustedCertificateAuthorityReference: null);

        // Assert
        Assert.Equal([MailTransportSecurityViolation.CertificateTrustNotSupported], violations);
    }

    [Fact]
    public void Create_UndefinedCertificateTrust_ThrowsInsteadOfReturningAValidatedPolicy()
    {
        // Arrange
        var authentication = MailAuthenticationPolicy.Create(
            [MailAuthenticationMechanism.ScramSha256],
            allowInsecureConnection: false,
            allowClearTextAuthenticationOverUnencryptedConnection: false);

        // Act
        var exception = Assert.Throws<MailTransportSecurityPolicyViolationException>(() => MailTransportSecurityPolicy.Create(
            MailConnectionSecurity.TlsOnConnect,
            authentication,
            (MailServerCertificateTrust)99,
            trustedCertificateAuthorityReference: null));

        // Assert
        Assert.Equal([MailTransportSecurityViolation.CertificateTrustNotSupported], exception.Violations);
    }

    [Fact]
    public void GuaranteesEncryptedChannel_UndefinedConnectionSecurityMode_Throws()
    {
        // Arrange
        const MailConnectionSecurity undefinedMode = (MailConnectionSecurity)99;

        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => MailTransportSecurityPolicy.GuaranteesEncryptedChannel(undefinedMode));
    }

    private static IReadOnlyList<MailTransportSecurityViolation> FindViolations(
        MailConnectionSecurity connectionSecurity,
        IReadOnlyList<MailAuthenticationMechanism> permittedMechanisms,
        bool allowInsecureConnection,
        bool allowClearTextAuthenticationOverUnencryptedConnection) => MailTransportSecurityPolicy.FindViolations(
            connectionSecurity,
            permittedMechanisms,
            allowInsecureConnection,
            allowClearTextAuthenticationOverUnencryptedConnection,
            MailServerCertificateTrust.SystemTrustStore,
            trustedCertificateAuthorityReference: null);
}
