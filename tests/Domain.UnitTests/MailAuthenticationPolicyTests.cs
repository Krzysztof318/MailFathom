// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Transport;
using Xunit;

namespace MailFathom.Domain.UnitTests;

public sealed class MailAuthenticationPolicyTests
{
    public static TheoryData<MailAuthenticationMechanism, string> SupportedMechanismNames =>
        new()
        {
            { MailAuthenticationMechanism.Plain, "PLAIN" },
            { MailAuthenticationMechanism.Login, "LOGIN" },
            { MailAuthenticationMechanism.CramMd5, "CRAM-MD5" },
            { MailAuthenticationMechanism.DigestMd5, "DIGEST-MD5" },
            { MailAuthenticationMechanism.ScramSha1, "SCRAM-SHA-1" },
            { MailAuthenticationMechanism.ScramSha1Plus, "SCRAM-SHA-1-PLUS" },
            { MailAuthenticationMechanism.ScramSha256, "SCRAM-SHA-256" },
            { MailAuthenticationMechanism.ScramSha256Plus, "SCRAM-SHA-256-PLUS" },
            { MailAuthenticationMechanism.ScramSha512, "SCRAM-SHA-512" },
            { MailAuthenticationMechanism.ScramSha512Plus, "SCRAM-SHA-512-PLUS" },
            { MailAuthenticationMechanism.Ntlm, "NTLM" },
        };

    public static TheoryData<string, MailAuthenticationMechanism> ParsableMechanismNames =>
        new()
        {
            { " scram-sha-256 ", MailAuthenticationMechanism.ScramSha256 },
            { "Plain", MailAuthenticationMechanism.Plain },
            { "SCRAM-SHA-1-PLUS", MailAuthenticationMechanism.ScramSha1Plus },
        };

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
    public void PermittedMechanisms_CreatedPolicy_CannotBeCastBackToAMutableCollection()
    {
        // Arrange
        var policy = MailAuthenticationPolicy.Create(
            [MailAuthenticationMechanism.ScramSha256],
            allowInsecureConnection: false,
            allowClearTextAuthenticationOverUnencryptedConnection: false);

        // Act
        var mutableView = policy.PermittedMechanisms as IList<MailAuthenticationMechanism>;

        // Assert
        Assert.Null(policy.PermittedMechanisms as MailAuthenticationMechanism[]);
        Assert.Throws<NotSupportedException>(() => mutableView![0] = MailAuthenticationMechanism.Plain);
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
    [MemberData(nameof(SupportedMechanismNames))]
    public void SaslName_SupportedMechanism_ReturnsTheRegisteredWireName(
        MailAuthenticationMechanism mechanism,
        string expectedSaslName)
    {
        // Arrange, Act
        var saslName = mechanism.SaslName;

        // Assert
        Assert.Equal(expectedSaslName, saslName);
    }

    [Theory]
    [MemberData(nameof(ParsableMechanismNames))]
    public void TryParseSaslName_MixedCaseOrPaddedName_ParsesTheMechanism(
        string configuredName,
        MailAuthenticationMechanism expectedMechanism)
    {
        // Arrange, Act
        var parsed = MailAuthenticationMechanism.TryParseSaslName(configuredName, out var mechanism);

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
        var parsed = MailAuthenticationMechanism.TryParseSaslName(configuredName, out _);

        // Assert
        Assert.False(parsed);
    }

    [Fact]
    public void SaslName_StructDefault_ThrowsInsteadOfReportingAMechanism()
    {
        // Arrange
        MailAuthenticationMechanism unspecifiedMechanism = default;

        // Act, Assert
        Assert.False(unspecifiedMechanism.IsSpecified);
        Assert.Throws<InvalidOperationException>(() => unspecifiedMechanism.SaslName);
    }

    [Fact]
    public void Create_StructDefaultMechanism_Throws()
    {
        // Arrange
        MailAuthenticationMechanism unspecifiedMechanism = default;

        // Act, Assert
        Assert.Throws<ArgumentException>(() => MailAuthenticationPolicy.Create(
            [unspecifiedMechanism],
            allowInsecureConnection: false,
            allowClearTextAuthenticationOverUnencryptedConnection: false));
    }

    [Fact]
    public void All_SupportedMechanisms_ExposesEveryMechanismExactlyOnce()
    {
        // Arrange, Act
        var saslNames = MailAuthenticationMechanism.All.Select(mechanism => mechanism.SaslName).ToArray();

        // Assert
        Assert.Equal(saslNames.Length, saslNames.Distinct(StringComparer.Ordinal).Count());
        Assert.All(MailAuthenticationMechanism.All, mechanism => Assert.True(mechanism.IsSpecified));
    }
}
