// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailFathom.Host.Security;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Host.UnitTests;

/// <summary>Covers when startup reports a certificate as something to renew rather than as something it loaded.</summary>
/// <remarks>
/// The window is one rule for every listener that presents a certificate. Two copies of it would let one listener start
/// warning a month before the other, which an operator would read as one of them being wrong.
/// </remarks>
public sealed class ServerCertificateExpiryTests
{
    private static readonly DateTimeOffset ReadAt = new(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ExpirationOf_ACertificate_ReadsTheInstantItStopsBeingUsable()
    {
        // Arrange
        using var identity = TestCertificates.CreateServerIdentity(
            ["probe.example.test"],
            ReadAt.AddDays(-1),
            ReadAt.AddDays(90));

        // Act
        var expiration = ServerCertificateExpiry.ExpirationOf(identity);

        // Assert
        Assert.Equal(TimeSpan.Zero, expiration.Offset);
        Assert.Equal(identity.NotAfter.ToUniversalTime(), expiration);
    }

    [Theory]
    [InlineData(31, false)]
    [InlineData(30, true)]
    [InlineData(1, true)]
    public void IsExpiringSoon_ACertificateExpiringInSoManyDays_IsReportedAgainstTheNoticeWindow(
        int daysUntilExpiry,
        bool expected)
    {
        // Arrange
        var expiration = ReadAt.AddDays(daysUntilExpiry);

        // Act
        var expiringSoon = ServerCertificateExpiry.IsExpiringSoon(expiration, ReadAt);

        // Assert
        Assert.Equal(expected, expiringSoon);
    }

    /// <summary>
    /// Nothing reaches this with an expired certificate — the loader refuses one before a holder publishes it — but the
    /// window has to answer for it rather than reporting an ordinary load.
    /// </summary>
    [Fact]
    public void IsExpiringSoon_ACertificateThatHasAlreadyExpired_IsReportedAsExpiringSoon()
    {
        // Act
        var expiringSoon = ServerCertificateExpiry.IsExpiringSoon(ReadAt.AddDays(-1), ReadAt);

        // Assert
        Assert.True(expiringSoon);
    }
}
