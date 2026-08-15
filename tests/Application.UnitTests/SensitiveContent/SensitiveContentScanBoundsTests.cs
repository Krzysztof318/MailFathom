// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using Xunit;

namespace MailFathom.Application.UnitTests.SensitiveContent;

/// <summary>Covers what one scan may spend, and the values no scan could run under.</summary>
public sealed class SensitiveContentScanBoundsTests
{
    /// <summary>The documented defaults, which an operator who configures nothing receives.</summary>
    [Fact]
    public void Default_StatesTheBoundsADeploymentReceivesWithoutConfiguringAny()
    {
        // Act
        var bounds = SensitiveContentScanBounds.Default;

        // Assert
        Assert.Equal(200_000, bounds.MaximumAnalyzedCharacters);
        Assert.Equal(TimeSpan.FromSeconds(15), bounds.ScanTimeout);
        Assert.Equal(4, bounds.MaximumConcurrentScans);
    }

    [Fact]
    public void Create_ConfiguredValues_AreCarriedThrough()
    {
        // Act
        var bounds = SensitiveContentScanBounds.Create(1_000, TimeSpan.FromSeconds(30), 2);

        // Assert
        Assert.Equal(1_000, bounds.MaximumAnalyzedCharacters);
        Assert.Equal(TimeSpan.FromSeconds(30), bounds.ScanTimeout);
        Assert.Equal(2, bounds.MaximumConcurrentScans);
    }

    [Theory]
    [InlineData(0, 5, 4)]
    [InlineData(-1, 5, 4)]
    [InlineData(1_000, 0, 4)]
    [InlineData(1_000, -1, 4)]
    [InlineData(1_000, 5, 0)]
    [InlineData(1_000, 5, -1)]
    public void Create_ValueNoScanCouldRunUnder_IsRejected(
        int maximumAnalyzedCharacters,
        int scanTimeoutSeconds,
        int maximumConcurrentScans)
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => SensitiveContentScanBounds.Create(
            maximumAnalyzedCharacters,
            TimeSpan.FromSeconds(scanTimeoutSeconds),
            maximumConcurrentScans));
    }
}
