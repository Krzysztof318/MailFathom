// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Spam;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Spam;

/// <summary>Covers what a configured scanner address and its bounds are allowed to be.</summary>
public sealed class SpamAssassinScannerProfileTests
{
    /// <summary>A usable profile keeps what it was given, and composes the address a caller may record.</summary>
    [Fact]
    public void Create_AUsableAddressAndBounds_KeepsThemAndComposesTheEndpoint()
    {
        // Arrange
        var scanTimeout = TimeSpan.FromSeconds(30);

        // Act
        var profile = SpamAssassinScannerProfile.Create(
            "  mailfathom-spamassassin  ",
            SpamAssassinScannerProfile.DefaultPort,
            scanTimeout,
            maximumMessageBytes: 512_000,
            maximumConcurrentScans: 5);

        // Assert
        Assert.Equal("mailfathom-spamassassin", profile.Host);
        Assert.Equal(783, profile.Port);
        Assert.Equal(scanTimeout, profile.ScanTimeout);
        Assert.Equal(512_000, profile.MaximumMessageBytes);
        Assert.Equal(5, profile.MaximumConcurrentScans);
        Assert.Equal("mailfathom-spamassassin:783", profile.Endpoint);
    }

    /// <summary>An address that is not one is refused, and the refusal quotes no part of it.</summary>
    /// <remarks>
    /// The message reaches a startup log, and a host name never does. What it names instead is the shape of a good
    /// value, so an operator with the file already open knows what to write; the assertion is against a host distinct
    /// from the one the message offers as an example, so a literal match would fail rather than pass on that example.
    /// </remarks>
    [Theory]
    [InlineData("http://spam.example.test")]
    [InlineData("spam.example.test:783")]
    [InlineData("spam example test")]
    [InlineData("spam\texample")]
    public void Create_AnAddressThatIsNotAHostName_IsRefusedWithoutQuotingIt(string host)
    {
        // Act
        var refusal = Assert.Throws<ArgumentException>(() => SpamAssassinScannerProfile.Create(
            host,
            783,
            TimeSpan.FromSeconds(30),
            512_000,
            5));

        // Assert
        Assert.DoesNotContain("spam.example.test", refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("spam example test", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("mailfathom-spamassassin", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>A blank address is refused as an absent one rather than reaching the shape check.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ABlankAddress_IsRefused(string host)
    {
        // Act, Assert
        _ = Assert.Throws<ArgumentException>(() => SpamAssassinScannerProfile.Create(
            host,
            783,
            TimeSpan.FromSeconds(30),
            512_000,
            5));
    }

    /// <summary>Every bound has to be a bound, and a port has to be a port.</summary>
    /// <remarks>
    /// The options section refuses each of these first and with a message naming its key, so nothing a deployment
    /// configures reaches here. What this covers is the type's own invariant, which is what a second caller — a test, a
    /// future composition root — would otherwise be free to break.
    /// </remarks>
    [Theory]
    [InlineData(0, 30, 512_000, 5)]
    [InlineData(65_536, 30, 512_000, 5)]
    [InlineData(783, 0, 512_000, 5)]
    [InlineData(783, -1, 512_000, 5)]
    [InlineData(783, 30, 0, 5)]
    [InlineData(783, 30, -1, 5)]
    [InlineData(783, 30, 512_000, 0)]
    [InlineData(783, 30, 512_000, -1)]
    public void Create_ABoundThatIsNotOne_IsRefused(
        int port,
        int scanTimeoutSeconds,
        int maximumMessageBytes,
        int maximumConcurrentScans)
    {
        // Act, Assert
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => SpamAssassinScannerProfile.Create(
            "mailfathom-spamassassin",
            port,
            TimeSpan.FromSeconds(scanTimeoutSeconds),
            maximumMessageBytes,
            maximumConcurrentScans));
    }
}
