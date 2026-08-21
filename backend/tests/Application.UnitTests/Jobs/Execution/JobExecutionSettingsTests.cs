// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs.Execution;
using Xunit;

namespace MailFathom.Application.UnitTests.Jobs.Execution;

public sealed class JobExecutionSettingsTests
{
    private static readonly TimeSpan RetryBaseDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RetryMaxDelay = TimeSpan.FromMinutes(30);

    /// <summary>
    /// An attempt has to be cancelled before its lease can expire underneath it, because a lease that ran out while its
    /// holder was still working is a second worker taking the same job.
    /// </summary>
    [Theory]
    [InlineData(60, 60)]
    [InlineData(90, 60)]
    public void Create_ATimeoutThatIsNotShorterThanTheLease_IsRefused(int timeoutSeconds, int leaseSeconds)
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => JobExecutionSettings.Create(
            batchSize: 5,
            TimeSpan.FromSeconds(leaseSeconds),
            TimeSpan.FromSeconds(timeoutSeconds),
            maxAttempts: 5,
            RetryBaseDelay,
            RetryMaxDelay));
    }

    /// <summary>A pass that took nothing, a job held for no time, or no attempt at all would each be a bound that bounds nothing.</summary>
    [Theory]
    [InlineData(0, 60, 30, 5)]
    [InlineData(5, 0, 30, 5)]
    [InlineData(5, 60, 0, 5)]
    [InlineData(5, 60, 30, 0)]
    public void Create_ABoundThatIsNotPositive_IsRefused(
        int batchSize,
        int leaseSeconds,
        int timeoutSeconds,
        int maxAttempts)
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => JobExecutionSettings.Create(
            batchSize,
            TimeSpan.FromSeconds(leaseSeconds),
            TimeSpan.FromSeconds(timeoutSeconds),
            maxAttempts,
            RetryBaseDelay,
            RetryMaxDelay));
    }

    /// <summary>A ceiling below the delay the growth starts from caps every retry at the ceiling and leaves nothing to grow.</summary>
    [Fact]
    public void Create_ARetryCeilingBelowTheDelayItGrowsFrom_IsRefused()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => JobExecutionSettings.Create(
            batchSize: 5,
            TimeSpan.FromMinutes(10),
            TimeSpan.FromMinutes(9),
            maxAttempts: 5,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(1)));
    }

    /// <summary>A retry delay of zero would return a failing job to the queue as fast as the queue can hand it out.</summary>
    [Fact]
    public void Create_ARetryDelayThatIsNotPositive_IsRefused()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => JobExecutionSettings.Create(
            batchSize: 5,
            TimeSpan.FromMinutes(10),
            TimeSpan.FromMinutes(9),
            maxAttempts: 5,
            TimeSpan.Zero,
            RetryMaxDelay));
    }

    /// <summary>
    /// Renewal at half the lease is the margin the ordering needs: one renewal that fails still leaves a whole half
    /// lease before anything can reclaim the job.
    /// </summary>
    [Fact]
    public void LeaseRenewalInterval_AnyLease_IsHalfOfIt()
    {
        // Arrange
        var settings = JobExecutionSettings.Create(
            batchSize: 5,
            TimeSpan.FromMinutes(10),
            TimeSpan.FromMinutes(9),
            maxAttempts: 5,
            RetryBaseDelay,
            RetryMaxDelay);

        // Act
        var renewalInterval = settings.LeaseRenewalInterval;

        // Assert
        Assert.Equal(TimeSpan.FromMinutes(5), renewalInterval);
    }

    /// <summary>The retry budget is the queue's own and is read straight back, because the executor bounds attempts against it.</summary>
    [Fact]
    public void Create_ARetryBudget_KeepsTheAttemptBoundAndBothDelays()
    {
        // Act
        var settings = JobExecutionSettings.Create(
            batchSize: 5,
            TimeSpan.FromMinutes(10),
            TimeSpan.FromMinutes(9),
            maxAttempts: 7,
            RetryBaseDelay,
            RetryMaxDelay);

        // Assert
        Assert.Equal(7, settings.MaxAttempts);
        Assert.Equal(RetryBaseDelay, settings.RetryBaseDelay);
        Assert.Equal(RetryMaxDelay, settings.RetryMaxDelay);
    }
}
