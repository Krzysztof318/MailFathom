// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs.Execution;
using Xunit;

namespace MailFathom.Application.UnitTests.Jobs.Execution;

public sealed class JobExecutionSettingsTests
{
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
            TimeSpan.FromSeconds(timeoutSeconds)));
    }

    /// <summary>A pass that took nothing or a job held for no time would each be a bound that bounds nothing.</summary>
    [Theory]
    [InlineData(0, 60, 30)]
    [InlineData(5, 0, 30)]
    [InlineData(5, 60, 0)]
    public void Create_ABoundThatIsNotPositive_IsRefused(int batchSize, int leaseSeconds, int timeoutSeconds)
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => JobExecutionSettings.Create(
            batchSize,
            TimeSpan.FromSeconds(leaseSeconds),
            TimeSpan.FromSeconds(timeoutSeconds)));
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
            TimeSpan.FromMinutes(9));

        // Act
        var renewalInterval = settings.LeaseRenewalInterval;

        // Assert
        Assert.Equal(TimeSpan.FromMinutes(5), renewalInterval);
    }
}
