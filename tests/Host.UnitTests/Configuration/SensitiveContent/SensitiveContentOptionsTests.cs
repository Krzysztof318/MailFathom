// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using MailFathom.Application.SensitiveContent;
using MailFathom.Host.Configuration.SensitiveContent;
using MailFathom.Host.UnitTests.TestDoubles;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.SensitiveContent;

/// <summary>Covers the section's own defaults and bounds, and the validator the options framework runs at startup.</summary>
public sealed class SensitiveContentOptionsTests
{
    /// <summary>The ordinary deployment scans nothing, so an absent section has to be a working configuration.</summary>
    [Fact]
    public void Defaults_BothScanners_AreOff()
    {
        // Act
        var settings = new SensitiveContentOptions();

        // Assert
        Assert.False(settings.Secrets.Enabled);
        Assert.False(settings.Pii.Enabled);
        Assert.False(settings.IsAnyScannerEnabled);
    }

    [Fact]
    public void Defaults_Bounds_AreTheOnesTheApplicationDocuments()
    {
        // Act
        var settings = new SensitiveContentOptions();

        // Assert
        Assert.Equal(SensitiveContentScanBounds.Default.MaximumAnalyzedCharacters, settings.MaximumAnalyzedCharacters);
        Assert.Equal(SensitiveContentScanBounds.Default.ScanTimeout, settings.ScanTimeout);
        Assert.Equal(SensitiveContentScanBounds.Default.MaximumConcurrentScans, settings.MaximumConcurrentScans);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void IsAnyScannerEnabled_EitherSwitchOn_IsTrue(bool secrets, bool personalData)
    {
        // Arrange
        var settings = new SensitiveContentOptions();
        settings.Secrets.Enabled = secrets;
        settings.Pii.Enabled = personalData;

        // Act, Assert
        Assert.True(settings.IsAnyScannerEnabled);
    }

    [Fact]
    public void For_EachScanner_ReadsItsOwnSwitch()
    {
        // Arrange
        var settings = new SensitiveContentOptions();

        // Act, Assert
        Assert.Same(settings.Secrets, settings.For(SensitiveContentScannerKind.Secrets));
        Assert.Same(settings.Pii, settings.For(SensitiveContentScannerKind.Pii));
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.For((SensitiveContentScannerKind)7));
    }

    /// <summary>A budget below a second refuses ordinary mail on a busy machine, and one measured in minutes is a stall.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(121)]
    public void Validate_ScanTimeoutOutsideItsRange_IsReported(int seconds)
    {
        // Arrange
        var settings = new SensitiveContentOptions { ScanTimeout = TimeSpan.FromSeconds(seconds) };

        // Act
        var results = settings.Validate(new ValidationContext(settings)).ToArray();

        // Assert
        var result = Assert.Single(results);
        Assert.Contains("SensitiveContent:ScanTimeout", result.ErrorMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_ScanTimeoutInsideItsRange_ReportsNothing()
    {
        // Arrange
        var settings = new SensitiveContentOptions { ScanTimeout = TimeSpan.FromSeconds(45) };

        // Act
        var results = settings.Validate(new ValidationContext(settings));

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void CatalogValidator_ConfigurationNamingSomethingNoScannerDetects_FailsStartup()
    {
        // Arrange
        var catalog = new StubSensitiveContentCatalog(
            SensitiveContentScannerKind.Secrets,
            [StubSensitiveContentCatalog.Declare("CloudKey", detectedByDefault: true, "aws-access-token")]);
        var settings = new SensitiveContentOptions();
        settings.Secrets.Enabled = true;
        settings.Secrets.Categories.Add("CloudKeys");
        var validator = new SensitiveContentCatalogValidator([catalog]);

        // Act
        var result = validator.Validate(SensitiveContentOptions.SectionName, settings);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains("'CloudKeys'", StringComparison.Ordinal));
    }

    [Fact]
    public void CatalogValidator_UsableConfiguration_Succeeds()
    {
        // Arrange
        var catalog = new StubSensitiveContentCatalog(
            SensitiveContentScannerKind.Secrets,
            [StubSensitiveContentCatalog.Declare("CloudKey", detectedByDefault: true, "aws-access-token")]);
        var settings = new SensitiveContentOptions();
        settings.Secrets.Enabled = true;
        var validator = new SensitiveContentCatalogValidator([catalog]);

        // Act
        var result = validator.Validate(SensitiveContentOptions.SectionName, settings);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void CatalogValidator_WithoutOptions_IsRejected()
    {
        // Arrange
        var validator = new SensitiveContentCatalogValidator([]);

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => validator.Validate(SensitiveContentOptions.SectionName, null!));
    }
}
