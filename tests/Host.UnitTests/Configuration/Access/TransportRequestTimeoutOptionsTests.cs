// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Access;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Access;

/// <summary>Covers the ceiling one request runs under, and what an operator may state for it.</summary>
public sealed class TransportRequestTimeoutOptionsTests
{
    /// <summary>The longest budget one outbound AI provider invocation may spend, which the default has to enclose twice.</summary>
    /// <remarks>
    /// Restated here rather than read from the resilience defaults, because the point of the assertion is that the two
    /// numbers are related at all: a change to either one that broke the relationship would otherwise pass silently and
    /// leave an <c>ask_mail</c> call abandoned while it was still inside the budget its own configuration granted it.
    /// </remarks>
    private static readonly TimeSpan AiProviderTotalTimeout = TimeSpan.FromMinutes(5);

    [Fact]
    public void Enabled_WithNothingConfigured_BoundsTheRequest()
    {
        // Act
        var settings = new TransportRequestTimeoutOptions();

        // Assert
        Assert.True(settings.Enabled);
    }

    [Fact]
    public void Duration_WithNothingConfigured_EnclosesTwoSequentialAiProviderInvocations()
    {
        // Act
        var settings = new TransportRequestTimeoutOptions();

        // Assert
        // An ask_mail call embeds the question and then generates an answer, so the ceiling has to clear both budgets
        // or the request is abandoned before the provider's own failure could be classified and reported.
        Assert.True(
            settings.Duration >= AiProviderTotalTimeout + AiProviderTotalTimeout,
            $"The default ceiling of {settings.Duration} is below the {AiProviderTotalTimeout + AiProviderTotalTimeout} two sequential AI provider invocations may spend.");
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
