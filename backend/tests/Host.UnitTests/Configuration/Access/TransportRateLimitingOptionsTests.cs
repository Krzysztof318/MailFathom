// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Access;
using MailFathom.Infrastructure.Security.Transport;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Access;

public sealed class TransportRateLimitingOptionsTests
{
    [Fact]
    public void Enabled_WithNothingConfigured_BoundsTheEndpoint()
    {
        // Act
        var settings = new TransportRateLimitingOptions();

        // Assert
        Assert.True(settings.Enabled);
    }

    [Fact]
    public void Defaults_WithNothingConfigured_AreTheProductLimits()
    {
        // Arrange
        var expected = TransportRateLimits.Default;

        // Act
        var settings = new TransportRateLimitingOptions();

        // Assert
        Assert.Equal(expected.MaxConcurrentRequests, settings.MaxConcurrentRequests);
        Assert.Equal(expected.ConcurrencyQueueLimit, settings.ConcurrencyQueueLimit);
        Assert.Equal(expected.TokenCapacity, settings.TokenCapacity);
        Assert.Equal(expected.TokensPerReplenishmentPeriod, settings.TokensPerReplenishmentPeriod);
        Assert.Equal(expected.ReplenishmentPeriod, settings.ReplenishmentPeriod);
        Assert.Equal(expected.RequestQueueLimit, settings.RequestQueueLimit);
    }

    [Fact]
    public void FindConfigurationErrors_WithNothingConfigured_ReportsNothing()
    {
        // Arrange
        var settings = new TransportRateLimitingOptions();

        // Act
        var errors = settings.FindConfigurationErrors();

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public void FindConfigurationErrors_WhenLimitingIsOff_LeavesTheRemainingValuesAlone()
    {
        // Arrange
        var settings = new TransportRateLimitingOptions
        {
            Enabled = false,
            MaxConcurrentRequests = 0,
            TokenCapacity = -5,
            ReplenishmentPeriod = TimeSpan.Zero,
        };

        // Act
        var errors = settings.FindConfigurationErrors();

        // Assert
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(nameof(TransportRateLimitingOptions.MaxConcurrentRequests), 0)]
    [InlineData(nameof(TransportRateLimitingOptions.MaxConcurrentRequests), -1)]
    [InlineData(nameof(TransportRateLimitingOptions.MaxConcurrentRequests), 1001)]
    [InlineData(nameof(TransportRateLimitingOptions.ConcurrencyQueueLimit), -1)]
    [InlineData(nameof(TransportRateLimitingOptions.ConcurrencyQueueLimit), 1001)]
    [InlineData(nameof(TransportRateLimitingOptions.TokenCapacity), 0)]
    [InlineData(nameof(TransportRateLimitingOptions.TokenCapacity), 1_000_001)]
    [InlineData(nameof(TransportRateLimitingOptions.TokensPerReplenishmentPeriod), 0)]
    [InlineData(nameof(TransportRateLimitingOptions.RequestQueueLimit), -1)]
    [InlineData(nameof(TransportRateLimitingOptions.RequestQueueLimit), 1001)]
    public void FindConfigurationErrors_WithAnOutOfRangeValue_NamesTheSetting(string settingName, int configuredValue)
    {
        // Arrange
        var settings = new TransportRateLimitingOptions();
        ApplyCountSetting(settings, settingName, configuredValue);

        // Act
        var errors = settings.FindConfigurationErrors();

        // Assert
        var error = Assert.Single(errors);
        Assert.StartsWith(settingName, error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3601)]
    public void FindConfigurationErrors_WithAnUnusableReplenishmentPeriod_NamesTheSetting(int configuredSeconds)
    {
        // Arrange
        var settings = new TransportRateLimitingOptions { ReplenishmentPeriod = TimeSpan.FromSeconds(configuredSeconds) };

        // Act
        var errors = settings.FindConfigurationErrors();

        // Assert
        var error = Assert.Single(errors);
        Assert.StartsWith(nameof(TransportRateLimitingOptions.ReplenishmentPeriod), error, StringComparison.Ordinal);
    }

    [Fact]
    public void FindConfigurationErrors_RestoringMoreThanTheBucketHolds_ReportsTheCombination()
    {
        // Arrange
        var settings = new TransportRateLimitingOptions { TokenCapacity = 10, TokensPerReplenishmentPeriod = 11 };

        // Act
        var errors = settings.FindConfigurationErrors();

        // Assert
        var error = Assert.Single(errors);
        Assert.StartsWith(nameof(TransportRateLimitingOptions.TokensPerReplenishmentPeriod), error, StringComparison.Ordinal);
        Assert.Contains(nameof(TransportRateLimitingOptions.TokenCapacity), error, StringComparison.Ordinal);
    }

    [Fact]
    public void FindConfigurationErrors_WithAClientQueueThatCouldHoldEveryPermit_ReportsTheCombination()
    {
        // Arrange
        // The two limiters are acquired in order, so a request waiting for its client's capacity is already holding a
        // concurrency permit. A queue this size lets one client out of tokens park every permit the process has.
        var settings = new TransportRateLimitingOptions { MaxConcurrentRequests = 4, RequestQueueLimit = 4 };

        // Act
        var errors = settings.FindConfigurationErrors();

        // Assert
        var error = Assert.Single(errors);
        Assert.StartsWith(nameof(TransportRateLimitingOptions.RequestQueueLimit), error, StringComparison.Ordinal);
        Assert.Contains(nameof(TransportRateLimitingOptions.MaxConcurrentRequests), error, StringComparison.Ordinal);
    }

    [Fact]
    public void FindConfigurationErrors_WithAClientQueueBelowThePermitCount_ReportsNothing()
    {
        // Arrange
        var settings = new TransportRateLimitingOptions { MaxConcurrentRequests = 4, RequestQueueLimit = 3 };

        // Act
        var errors = settings.FindConfigurationErrors();

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public void FindConfigurationErrors_WithOneMistypedQueueLimit_ReportsItOnce()
    {
        // Arrange
        // A queue beyond its own range is also beyond the permit count; reporting both would describe one typo twice.
        var settings = new TransportRateLimitingOptions { RequestQueueLimit = 1001 };

        // Act
        var errors = settings.FindConfigurationErrors();

        // Assert
        var error = Assert.Single(errors);
        Assert.StartsWith(nameof(TransportRateLimitingOptions.RequestQueueLimit), error, StringComparison.Ordinal);
    }

    [Fact]
    public void FindConfigurationErrors_WithOneMistypedCapacity_ReportsItOnce()
    {
        // Arrange
        // Zero capacity is out of range and is also below the tokens restored each period; reporting both would describe
        // one typo twice and send an operator looking for a second mistake they did not make.
        var settings = new TransportRateLimitingOptions { TokenCapacity = 0 };

        // Act
        var errors = settings.FindConfigurationErrors();

        // Assert
        var error = Assert.Single(errors);
        Assert.StartsWith(nameof(TransportRateLimitingOptions.TokenCapacity), error, StringComparison.Ordinal);
    }

    [Fact]
    public void FindConfigurationErrors_WithSeveralMistakes_ReportsEveryOne()
    {
        // Arrange
        var settings = new TransportRateLimitingOptions
        {
            MaxConcurrentRequests = 0,
            ConcurrencyQueueLimit = -1,
            RequestQueueLimit = -1,
        };

        // Act
        var errors = settings.FindConfigurationErrors();

        // Assert
        Assert.Equal(3, errors.Count);
    }

    [Fact]
    public void ToRateLimits_WithConfiguredValues_CarriesEveryOne()
    {
        // Arrange
        var settings = new TransportRateLimitingOptions
        {
            MaxConcurrentRequests = 9,
            ConcurrencyQueueLimit = 4,
            TokenCapacity = 30,
            TokensPerReplenishmentPeriod = 5,
            ReplenishmentPeriod = TimeSpan.FromSeconds(20),
            RequestQueueLimit = 1,
        };

        // Act
        var limits = settings.ToRateLimits();

        // Assert
        Assert.Equal(9, limits.MaxConcurrentRequests);
        Assert.Equal(4, limits.ConcurrencyQueueLimit);
        Assert.Equal(30, limits.TokenCapacity);
        Assert.Equal(5, limits.TokensPerReplenishmentPeriod);
        Assert.Equal(TimeSpan.FromSeconds(20), limits.ReplenishmentPeriod);
        Assert.Equal(1, limits.RequestQueueLimit);
    }

    [Fact]
    public void ToRateLimits_BeforeValidation_RefusesToMapAnUnusableValue()
    {
        // Arrange
        var settings = new TransportRateLimitingOptions { MaxConcurrentRequests = 0 };

        // Act, Assert
        Assert.Throws<InvalidOperationException>(settings.ToRateLimits);
    }

    private static void ApplyCountSetting(TransportRateLimitingOptions settings, string settingName, int configuredValue)
    {
        switch (settingName)
        {
            case nameof(TransportRateLimitingOptions.MaxConcurrentRequests):
                settings.MaxConcurrentRequests = configuredValue;
                break;
            case nameof(TransportRateLimitingOptions.ConcurrencyQueueLimit):
                settings.ConcurrencyQueueLimit = configuredValue;
                break;
            case nameof(TransportRateLimitingOptions.TokenCapacity):
                settings.TokenCapacity = configuredValue;
                // Kept at or below the capacity under test, so the assertion reads the range error rather than the
                // combination error a default of sixty tokens per period would also produce.
                settings.TokensPerReplenishmentPeriod = 1;
                break;
            case nameof(TransportRateLimitingOptions.TokensPerReplenishmentPeriod):
                settings.TokensPerReplenishmentPeriod = configuredValue;
                break;
            case nameof(TransportRateLimitingOptions.RequestQueueLimit):
                settings.RequestQueueLimit = configuredValue;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(settingName), settingName, "The test names no such setting.");
        }
    }
}
