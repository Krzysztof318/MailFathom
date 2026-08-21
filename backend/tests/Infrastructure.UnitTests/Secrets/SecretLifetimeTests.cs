// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Secrets;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Secrets;

/// <summary>Covers how long a configured secret stays usable, and what a badly stated lifetime does.</summary>
public sealed class SecretLifetimeTests
{
    private static readonly DateTimeOffset Expiration = new(2027, 1, 31, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The type's default and the setting's default have to agree, or a field nobody assigned would mean something configuration cannot express.</summary>
    [Fact]
    public void Default_UnassignedValue_IsTheSameAsNoLimit()
    {
        // Arrange, Act
        var lifetime = default(SecretLifetime);

        // Assert
        Assert.Equal(SecretLifetime.NoLimit, lifetime);
        Assert.False(lifetime.IsBounded);
    }

    [Fact]
    public void TryParse_TheNoLimitSpelling_ReadsAsAnUnboundedLifetime()
    {
        // Arrange, Act
        var parsed = SecretLifetime.TryParse(SecretLifetime.NoLimitValue, out var lifetime);

        // Assert
        Assert.True(parsed);
        Assert.False(lifetime.IsBounded);
    }

    [Theory]
    [InlineData("nolimit")]
    [InlineData("NOLIMIT")]
    [InlineData("  NoLimit  ")]
    public void TryParse_TheNoLimitSpellingCasedOrPadded_StillReadsAsUnbounded(string configuredValue)
    {
        // Arrange, Act
        var parsed = SecretLifetime.TryParse(configuredValue, out var lifetime);

        // Assert
        Assert.True(parsed);
        Assert.False(lifetime.IsBounded);
    }

    [Theory]
    [InlineData("2027-01-31T00:00:00Z")]
    [InlineData("2027-01-31T00:00:00.000Z")]
    [InlineData("2027-01-31T01:00:00+01:00")]
    public void TryParse_AnInstantCarryingAnExplicitOffset_ReadsAsTheSameUtcExpiration(string configuredValue)
    {
        // Arrange, Act
        var parsed = SecretLifetime.TryParse(configuredValue, out var lifetime);

        // Assert
        Assert.True(parsed);
        Assert.True(lifetime.IsBounded);
        Assert.Equal(Expiration, lifetime.Expiration);
    }

    /// <summary>Without an offset the same configuration expires at a different moment on every machine that runs it.</summary>
    [Theory]
    [InlineData("2027-01-31T00:00:00")]
    [InlineData("2027-01-31")]
    public void TryParse_AnInstantWithoutAnOffset_IsRefusedRatherThanReadInLocalTime(string configuredValue)
    {
        // Arrange, Act
        var parsed = SecretLifetime.TryParse(configuredValue, out _);

        // Assert
        Assert.False(parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Never")]
    [InlineData("30d")]
    [InlineData("00:30:00")]
    public void TryParse_AnythingElse_IsRefusedRatherThanTakenAsUnbounded(string? configuredValue)
    {
        // Arrange, Act
        var parsed = SecretLifetime.TryParse(configuredValue, out var lifetime);

        // Assert
        Assert.False(parsed);
        Assert.Equal(SecretLifetime.NoLimit, lifetime);
    }

    [Fact]
    public void HasExpiredAt_AnUnboundedLifetime_IsNeverExpired()
    {
        // Arrange
        var lifetime = SecretLifetime.NoLimit;

        // Act, Assert
        Assert.False(lifetime.HasExpiredAt(DateTimeOffset.MaxValue));
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public void HasExpiredAt_ABoundedLifetime_EndsAtTheInstantItNamesRatherThanAfterIt(
        int minutesFromExpiration,
        bool expectedToHaveExpired)
    {
        // Arrange
        var lifetime = SecretLifetime.ExpiringAt(Expiration);

        // Act
        var expired = lifetime.HasExpiredAt(Expiration.AddMinutes(minutesFromExpiration));

        // Assert
        Assert.Equal(expectedToHaveExpired, expired);
    }

    /// <summary>
    /// An absolute instant is what makes a lifetime survive a restart. Had it been a duration, a credential retired for
    /// a week would come back with the next deployment, which is the failure this modelling exists to rule out.
    /// </summary>
    [Fact]
    public void HasExpiredAt_TheSameConfiguredValueReadAgain_ReportsTheSameExpiration()
    {
        // Arrange
        Assert.True(SecretLifetime.TryParse("2027-01-31T00:00:00Z", out var beforeRestart));
        Assert.True(SecretLifetime.TryParse("2027-01-31T00:00:00Z", out var afterRestart));
        var wellPastIt = Expiration.AddYears(1);

        // Act, Assert
        Assert.Equal(beforeRestart, afterRestart);
        Assert.True(afterRestart.HasExpiredAt(wellPastIt));
    }

    [Fact]
    public void Expiration_AnUnboundedLifetime_ThrowsBecauseItNamesNoInstant()
    {
        // Arrange
        var lifetime = SecretLifetime.NoLimit;

        // Act, Assert
        Assert.Throws<InvalidOperationException>(() => lifetime.Expiration);
    }

    [Fact]
    public void ToString_AnyLifetime_ReturnsTheSpellingConfigurationUses()
    {
        // Arrange, Act, Assert
        Assert.Equal(SecretLifetime.NoLimitValue, SecretLifetime.NoLimit.ToString());
        Assert.Equal("2027-01-31T00:00:00Z", SecretLifetime.ExpiringAt(Expiration).ToString());
    }
}
