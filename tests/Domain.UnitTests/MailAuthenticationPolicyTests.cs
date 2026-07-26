// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Domain.Transport;
using Xunit;

namespace MailMcp.Domain.UnitTests;

public sealed class MailAuthenticationPolicyTests
{
    [Fact]
    public void Create_DuplicateMechanisms_KeepsFirstOccurrenceOrder()
    {
        // Arrange, Act
        var policy = MailAuthenticationPolicy.Create(
            [
                MailAuthenticationMechanism.ScramSha256,
                MailAuthenticationMechanism.Plain,
                MailAuthenticationMechanism.ScramSha256,
            ],
            allowInsecureConnection: false,
            allowClearTextAuthenticationOverUnencryptedConnection: false);

        // Assert
        Assert.Equal([MailAuthenticationMechanism.ScramSha256, MailAuthenticationMechanism.Plain], policy.PermittedMechanisms);
    }

    [Fact]
    public void Create_NoMechanisms_Throws()
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentException>(() => MailAuthenticationPolicy.Create(
            [],
            allowInsecureConnection: false,
            allowClearTextAuthenticationOverUnencryptedConnection: false));
    }

    [Fact]
    public void PermitsClearTextCredentials_ChallengeResponseMechanismsOnly_ReportsFalse()
    {
        // Arrange
        var policy = MailAuthenticationPolicy.Create(
            [MailAuthenticationMechanism.ScramSha512, MailAuthenticationMechanism.CramMd5],
            allowInsecureConnection: false,
            allowClearTextAuthenticationOverUnencryptedConnection: false);

        // Act, Assert
        Assert.False(policy.PermitsClearTextCredentials);
    }

    [Fact]
    public void PermitsClearTextCredentials_ListContainsClearTextMechanism_ReportsTrue()
    {
        // Arrange
        var policy = MailAuthenticationPolicy.Create(
            [MailAuthenticationMechanism.ScramSha512, MailAuthenticationMechanism.Login],
            allowInsecureConnection: false,
            allowClearTextAuthenticationOverUnencryptedConnection: false);

        // Act, Assert
        Assert.True(policy.PermitsClearTextCredentials);
    }

    [Theory]
    [InlineData(MailAuthenticationMechanism.Plain, "PLAIN")]
    [InlineData(MailAuthenticationMechanism.Login, "LOGIN")]
    [InlineData(MailAuthenticationMechanism.CramMd5, "CRAM-MD5")]
    [InlineData(MailAuthenticationMechanism.DigestMd5, "DIGEST-MD5")]
    [InlineData(MailAuthenticationMechanism.ScramSha1, "SCRAM-SHA-1")]
    [InlineData(MailAuthenticationMechanism.ScramSha1Plus, "SCRAM-SHA-1-PLUS")]
    [InlineData(MailAuthenticationMechanism.ScramSha256, "SCRAM-SHA-256")]
    [InlineData(MailAuthenticationMechanism.ScramSha256Plus, "SCRAM-SHA-256-PLUS")]
    [InlineData(MailAuthenticationMechanism.ScramSha512, "SCRAM-SHA-512")]
    [InlineData(MailAuthenticationMechanism.ScramSha512Plus, "SCRAM-SHA-512-PLUS")]
    [InlineData(MailAuthenticationMechanism.Ntlm, "NTLM")]
    public void ToSaslName_SupportedMechanism_ReturnsTheRegisteredWireName(
        MailAuthenticationMechanism mechanism,
        string expectedSaslName)
    {
        // Arrange, Act
        var saslName = mechanism.ToSaslName();

        // Assert
        Assert.Equal(expectedSaslName, saslName);
    }

    [Theory]
    [InlineData(" scram-sha-256 ", MailAuthenticationMechanism.ScramSha256)]
    [InlineData("Plain", MailAuthenticationMechanism.Plain)]
    [InlineData("SCRAM-SHA-1-PLUS", MailAuthenticationMechanism.ScramSha1Plus)]
    public void TryParseSaslName_MixedCaseOrPaddedName_ParsesTheMechanism(
        string configuredName,
        MailAuthenticationMechanism expectedMechanism)
    {
        // Arrange, Act
        var parsed = MailAuthenticationMechanisms.TryParseSaslName(configuredName, out var mechanism);

        // Assert
        Assert.True(parsed);
        Assert.Equal(expectedMechanism, mechanism);
    }

    [Theory]
    [InlineData("GSSAPI")]
    [InlineData("XOAUTH2")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParseSaslName_UnsupportedName_ReturnsFalse(string? configuredName)
    {
        // Arrange, Act
        var parsed = MailAuthenticationMechanisms.TryParseSaslName(configuredName, out _);

        // Assert
        Assert.False(parsed);
    }

    [Fact]
    public void ToSaslName_UndefinedMechanism_Throws()
    {
        // Arrange
        const MailAuthenticationMechanism undefinedMechanism = (MailAuthenticationMechanism)99;

        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => undefinedMechanism.ToSaslName());
    }

    [Fact]
    public void TransmitsCredentialsInClearText_UndefinedMechanism_Throws()
    {
        // Arrange
        const MailAuthenticationMechanism undefinedMechanism = (MailAuthenticationMechanism)99;

        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => undefinedMechanism.TransmitsCredentialsInClearText());
    }
}
