// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Spam;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Spam;

/// <summary>Covers what the classification section refuses before an operator discovers it on their own mail.</summary>
public sealed class SpamClassificationOptionsTests
{
    /// <summary>The section binds with none of its keys set, which is what makes the feature opt-in rather than defaulted off.</summary>
    [Fact]
    public void FindErrors_ASectionSettingNothing_ReportsNoErrorAndClassifiesNothing()
    {
        // Arrange
        var options = new SpamClassificationOptions();

        // Act
        var errors = options.FindErrors().ToArray();

        // Assert
        Assert.Empty(errors);
        Assert.False(options.Enabled);
        Assert.False(options.UseScanner);
        Assert.Null(options.ScannedFolders);
        Assert.Null(options.ScannerThreshold);
    }

    /// <summary>An operator who switched the scanner on and left classification off is told, rather than given the quiet answer.</summary>
    [Fact]
    public void FindErrors_AScannerAskedForWhileClassificationIsOff_IsRefused()
    {
        // Arrange
        var options = new SpamClassificationOptions
        {
            Enabled = false,
            UseScanner = true,
            Scanner = ReachableScanner(),
        };

        // Act
        var error = Assert.Single(options.FindErrors());

        // Assert
        Assert.Equal([nameof(SpamClassificationOptions.UseScanner)], error.MemberNames);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void FindErrors_AScannedFolderHoldingNoAlias_IsRefusedNamingTheEntry(string alias)
    {
        // Arrange
        var options = new SpamClassificationOptions { Enabled = true, ScannedFolders = [alias] };

        // Act
        var error = Assert.Single(options.FindErrors());

        // Assert
        Assert.Equal([nameof(SpamClassificationOptions.ScannedFolders)], error.MemberNames);
        Assert.Contains(alias, error.ErrorMessage, StringComparison.Ordinal);
    }

    /// <summary>An alias this system could never have issued is a typo rather than a folder, so it is named back.</summary>
    [Fact]
    public void FindErrors_AScannedFolderCarryingAControlCharacter_IsRefusedNamingTheAlias()
    {
        // Arrange
        var alias = "junk" + (char)7 + "mail";
        var options = new SpamClassificationOptions { Enabled = true, ScannedFolders = [alias] };

        // Act
        var error = Assert.Single(options.FindErrors());

        // Assert
        Assert.Equal([nameof(SpamClassificationOptions.ScannedFolders)], error.MemberNames);
        Assert.Contains(alias, error.ErrorMessage, StringComparison.Ordinal);
    }

    /// <summary>Writing no folder at all is an operator switching the work off without switching the section off.</summary>
    [Fact]
    public void FindErrors_AnExplicitlyEmptyScannedFolderList_ReportsNoError()
    {
        // Arrange
        var options = new SpamClassificationOptions { Enabled = true, ScannedFolders = [] };

        // Act, Assert
        Assert.Empty(options.FindErrors());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(SpamClassificationOptions.LargestThreshold + 1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void FindErrors_AThresholdOutsideTheUsableRange_IsRefused(double threshold)
    {
        // Arrange
        var options = new SpamClassificationOptions
        {
            Enabled = true,
            UseScanner = true,
            Scanner = ReachableScanner(),
            ScannerThreshold = threshold,
        };

        // Act
        var error = Assert.Single(options.FindErrors());

        // Assert
        Assert.Equal([nameof(SpamClassificationOptions.ScannerThreshold)], error.MemberNames);
    }

    [Theory]
    [InlineData(SpamClassificationOptions.SmallestThreshold)]
    [InlineData(5.0)]
    [InlineData(SpamClassificationOptions.LargestThreshold)]
    public void FindErrors_AThresholdInsideTheUsableRange_ReportsNoError(double threshold)
    {
        // Arrange
        var options = new SpamClassificationOptions
        {
            Enabled = true,
            UseScanner = true,
            Scanner = ReachableScanner(),
            ScannerThreshold = threshold,
        };

        // Act, Assert
        Assert.Empty(options.FindErrors());
    }

    /// <summary>The scanner block below the section is validated with it rather than on its own.</summary>
    /// <remarks>
    /// A scanner switched on with nowhere to ask is the block's own rule, and it reaches an operator through this
    /// section because that is the section a deployment writes.
    /// </remarks>
    [Fact]
    public void FindErrors_AScannerSwitchedOnWithNoAddressBelowIt_ReportsTheBlocksOwnRefusal()
    {
        // Arrange
        var options = new SpamClassificationOptions { Enabled = true, UseScanner = true };

        // Act
        var error = Assert.Single(options.FindErrors());

        // Assert
        Assert.Equal([nameof(SpamScannerOptions.Host)], error.MemberNames);
    }

    /// <summary>Every rule is reported at once, so an operator repairs the section rather than one key per restart.</summary>
    [Fact]
    public void FindErrors_ASectionBreakingSeveralRules_ReportsAllOfThem()
    {
        // Arrange
        var options = new SpamClassificationOptions
        {
            Enabled = false,
            UseScanner = true,
            Scanner = new SpamScannerOptions { Host = "mailfathom-spamassassin", Port = 0 },
            ScannedFolders = ["", "   "],
            ScannerThreshold = 0,
        };

        // Act
        var errors = options.FindErrors().ToArray();

        // Assert
        Assert.Equal(5, errors.Length);
    }

    private static SpamScannerOptions ReachableScanner() => new() { Host = "mailfathom-spamassassin" };
}
