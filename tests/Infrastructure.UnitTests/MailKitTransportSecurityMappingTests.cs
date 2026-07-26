// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Domain.Transport;
using MailMcp.Infrastructure.Mail.MailKit;
using Xunit;

namespace MailMcp.Infrastructure.UnitTests;

public sealed class MailKitTransportSecurityMappingTests
{
    [Fact]
    public void ToSecureSocketOptions_UndefinedConnectionSecurityMode_Throws()
    {
        // Arrange
        const MailConnectionSecurity undefinedMode = (MailConnectionSecurity)99;

        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => undefinedMode.ToSecureSocketOptions());
    }

    [Fact]
    public void RestrictAdvertisedMechanisms_AdvertisedNameInDifferentCase_KeepsTheMechanism()
    {
        // Arrange
        var advertisedMechanisms = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "scram-sha-256", "plain" };
        var authentication = MailAuthenticationPolicy.Create(
            [MailAuthenticationMechanism.ScramSha256],
            allowInsecureConnection: false,
            allowClearTextAuthenticationOverUnencryptedConnection: false);

        // Act
        MailKitTransportSecurityMapping.RestrictAdvertisedMechanisms(advertisedMechanisms, authentication, "primary");

        // Assert
        Assert.Equal(["scram-sha-256"], advertisedMechanisms);
    }

    [Fact]
    public void RestrictAdvertisedMechanisms_ServerAdvertisesNothing_ReportsThePermittedNamesOnly()
    {
        // Arrange
        var advertisedMechanisms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var authentication = MailAuthenticationPolicy.Create(
            [MailAuthenticationMechanism.ScramSha512, MailAuthenticationMechanism.CramMd5],
            allowInsecureConnection: false,
            allowClearTextAuthenticationOverUnencryptedConnection: false);

        // Act
        var exception = Assert.Throws<MailAuthenticationMechanismUnavailableException>(
            () => MailKitTransportSecurityMapping.RestrictAdvertisedMechanisms(advertisedMechanisms, authentication, "primary"));

        // Assert
        Assert.Equal(["CRAM-MD5", "SCRAM-SHA-512"], exception.PermittedMechanismNames);
        Assert.DoesNotContain("password", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RestrictAdvertisedMechanisms_NoAdvertisedMechanismSet_Throws()
    {
        // Arrange
        var authentication = MailAuthenticationPolicy.Create(
            [MailAuthenticationMechanism.Plain],
            allowInsecureConnection: false,
            allowClearTextAuthenticationOverUnencryptedConnection: false);

        // Act, Assert
        Assert.Throws<ArgumentNullException>(
            () => MailKitTransportSecurityMapping.RestrictAdvertisedMechanisms(null!, authentication, "primary"));
    }
}
