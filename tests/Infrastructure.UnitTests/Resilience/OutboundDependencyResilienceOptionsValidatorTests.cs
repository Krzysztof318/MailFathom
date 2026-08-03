// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Resilience;
using MailFathom.Infrastructure.Resilience;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Resilience;

public sealed class OutboundDependencyResilienceOptionsValidatorTests
{
    private readonly OutboundDependencyResilienceOptionsValidator validator = new();

    public static TheoryData<OutboundDependency> EveryDependency => [.. Enum.GetValues<OutboundDependency>()];

    /// <summary>Every shipped budget must survive the rules that reject a configured one.</summary>
    [Theory]
    [MemberData(nameof(EveryDependency))]
    public void Validate_ShippedDefaults_AreAcceptedForEveryDependency(OutboundDependency dependency)
    {
        // Arrange
        var options = new OutboundDependencyResilienceOptions();
        OutboundDependencyResilienceDefaults.ApplyTo(options, dependency);

        // Act
        var result = this.validator.Validate(dependency.ToString(), options);

        // Assert
        Assert.True(result.Succeeded, string.Join(" ", result.Failures ?? []));
    }

    /// <summary>An attempt that outlives the operation it belongs to describes a limit that can never be reached.</summary>
    [Fact]
    public void Validate_AttemptTimeoutLongerThanTotalTimeout_FailsStartup()
    {
        // Arrange
        var options = new OutboundDependencyResilienceOptions
        {
            AttemptTimeout = TimeSpan.FromMinutes(5),
            TotalTimeout = TimeSpan.FromMinutes(1),
        };

        // Act
        var result = this.validator.Validate(nameof(OutboundDependency.MailboxDataRetrieval), options);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, failure => failure.Contains(nameof(OutboundDependencyResilienceOptions.AttemptTimeout), StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_BackoffCeilingLongerThanTotalTimeout_FailsStartup()
    {
        // Arrange
        var options = new OutboundDependencyResilienceOptions
        {
            MaxAttempts = 3,
            MaxDelay = TimeSpan.FromMinutes(10),
            TotalTimeout = TimeSpan.FromMinutes(1),
            AttemptTimeout = TimeSpan.FromSeconds(10),
        };

        // Act
        var result = this.validator.Validate(nameof(OutboundDependency.EmailDelivery), options);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, failure => failure.Contains(nameof(OutboundDependencyResilienceOptions.MaxDelay), StringComparison.Ordinal));
    }

    /// <summary>Without retry there is no backoff, so a ceiling nobody waits for is not a contradiction.</summary>
    [Fact]
    public void Validate_BackoffCeilingLongerThanTotalTimeoutWithoutRetry_IsAccepted()
    {
        // Arrange
        var options = new OutboundDependencyResilienceOptions
        {
            MaxAttempts = 1,
            MaxDelay = TimeSpan.FromMinutes(10),
            TotalTimeout = TimeSpan.FromMinutes(1),
            AttemptTimeout = TimeSpan.FromSeconds(10),
        };

        // Act
        var result = this.validator.Validate(nameof(OutboundDependency.EmailDelivery), options);

        // Assert
        Assert.True(result.Succeeded, string.Join(" ", result.Failures ?? []));
    }

    [Fact]
    public void Validate_BackoffCeilingBelowTheFirstDelay_FailsStartup()
    {
        // Arrange
        var options = new OutboundDependencyResilienceOptions
        {
            BaseDelay = TimeSpan.FromSeconds(10),
            MaxDelay = TimeSpan.FromSeconds(1),
        };

        // Act
        var result = this.validator.Validate(nameof(OutboundDependency.MailboxDataRetrieval), options);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, failure => failure.Contains(nameof(OutboundDependencyResilienceOptions.MaxDelay), StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_UnboundedOperation_FailsStartup(int totalTimeoutSeconds)
    {
        // Arrange
        var options = new OutboundDependencyResilienceOptions
        {
            TotalTimeout = TimeSpan.FromSeconds(totalTimeoutSeconds),
        };

        // Act
        var result = this.validator.Validate(nameof(OutboundDependency.AiProviderInvocation), options);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, failure => failure.Contains(nameof(OutboundDependencyResilienceOptions.TotalTimeout), StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_ImmediateRetry_FailsStartupBecauseItRepeatsWithoutWaiting()
    {
        // Arrange
        var options = new OutboundDependencyResilienceOptions { BaseDelay = TimeSpan.Zero };

        // Act
        var result = this.validator.Validate(nameof(OutboundDependency.DatabaseCommandExecution), options);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, failure => failure.Contains(nameof(OutboundDependencyResilienceOptions.BaseDelay), StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_CircuitWindowsShorterThanTheStrategySupports_FailStartup()
    {
        // Arrange
        var options = new OutboundDependencyResilienceOptions
        {
            CircuitBreakerSamplingDuration = TimeSpan.FromMilliseconds(100),
            CircuitBreakerBreakDuration = TimeSpan.FromMilliseconds(100),
        };

        // Act
        var result = this.validator.Validate(nameof(OutboundDependency.MailboxSessionEstablishment), options);

        // Assert
        Assert.Equal(2, result.Failures!.Count());
    }

    [Fact]
    public void Validate_NoOptions_IsRejected()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => this.validator.Validate(name: null, options: null!));
    }
}
