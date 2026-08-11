// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Access;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Access;

/// <summary>Covers the ceiling one request runs under, and what an operator may state for it.</summary>
public sealed class TransportRequestTimeoutOptionsTests
{
    [Fact]
    public void Enabled_WithNothingConfigured_BoundsTheRequest()
    {
        // Act
        var settings = new TransportRequestTimeoutOptions();

        // Assert
        Assert.True(settings.Enabled);
    }

    /// <summary>
    /// Pins the product ceiling itself and nothing beyond it. It deliberately asserts no relationship to what an
    /// answering run may spend, because there is none to assert: a run is bounded by
    /// <c>MailAnswering:MaxProviderCallsPerRun</c> at eight AI provider invocations, and a ceiling enclosing that
    /// maximum would sit past three-quarters of an hour. The default is chosen against what a request costs to hold
    /// instead, so a maximal run being abandoned is the trade rather than a defect, and a test claiming to guard a
    /// relationship across the two sections would be describing a guarantee the code does not make.
    /// </summary>
    [Fact]
    public void Duration_WithNothingConfigured_IsTheProductCeiling()
    {
        // Act
        var settings = new TransportRequestTimeoutOptions();

        // Assert
        Assert.Equal(TimeSpan.FromMinutes(10), settings.Duration);
    }

    [Fact]
    public void FindConfigurationErrors_WithNothingConfigured_ReportsNothing()
    {
        // Arrange
        var settings = new TransportRequestTimeoutOptions();

        // Act
        var errors = settings.FindConfigurationErrors();

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public void FindConfigurationErrors_WithTheCeilingTurnedOff_ReportsNothingAboutTheDuration()
    {
        // Arrange
        // A deployment that turned the ceiling off is not held to a duration it is no longer applying, which is what
        // lets an operator leave a narrower number in place while an ingress bounds the request instead.
        var settings = new TransportRequestTimeoutOptions { Enabled = false, Duration = TimeSpan.Zero };

        // Act
        var errors = settings.FindConfigurationErrors();

        // Assert
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void FindConfigurationErrors_WithADurationAtOrBelowZero_ReportsTheSetting(int seconds)
    {
        // Arrange
        var settings = new TransportRequestTimeoutOptions { Duration = TimeSpan.FromSeconds(seconds) };

        // Act
        var error = Assert.Single(settings.FindConfigurationErrors());

        // Assert
        Assert.Contains(nameof(TransportRequestTimeoutOptions.Duration), error, StringComparison.Ordinal);
    }

    [Fact]
    public void FindConfigurationErrors_WithADurationBeyondAnHour_ReportsTheSetting()
    {
        // Arrange
        var settings = new TransportRequestTimeoutOptions { Duration = TimeSpan.FromHours(1) + TimeSpan.FromSeconds(1) };

        // Act
        var error = Assert.Single(settings.FindConfigurationErrors());

        // Assert
        Assert.Contains(nameof(TransportRequestTimeoutOptions.Duration), error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(60)]
    [InlineData(3600)]
    public void FindConfigurationErrors_WithADurationInsideTheRange_ReportsNothing(int seconds)
    {
        // Arrange
        var settings = new TransportRequestTimeoutOptions { Duration = TimeSpan.FromSeconds(seconds) };

        // Act
        var errors = settings.FindConfigurationErrors();

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public void FindConfigurationErrors_WithADurationOutsideTheRange_NamesWhatToWriteToRemoveTheCeiling()
    {
        // Arrange
        // An operator whose request genuinely outlives the longest permitted ceiling needs to be told the setting that
        // turns the bound off, rather than being left to raise a number the section refuses.
        var settings = new TransportRequestTimeoutOptions { Duration = TimeSpan.FromHours(2) };

        // Act
        var error = Assert.Single(settings.FindConfigurationErrors());

        // Assert
        Assert.Contains("concurrency permit", error, StringComparison.Ordinal);
    }
}
