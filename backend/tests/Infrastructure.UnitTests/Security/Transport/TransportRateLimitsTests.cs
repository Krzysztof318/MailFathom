// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Security.Transport;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Security.Transport;

public sealed class TransportRateLimitsTests
{
    [Fact]
    public void Default_BoundsBothResourcesWithoutQueueing()
    {
        // Act
        var limits = TransportRateLimits.Default;

        // Assert
        Assert.True(limits.MaxConcurrentRequests > 0);
        Assert.True(limits.TokenCapacity > 0);
        Assert.True(limits.TokensPerReplenishmentPeriod > 0);
        Assert.True(limits.ReplenishmentPeriod > TimeSpan.Zero);
        Assert.Equal(0, limits.ConcurrencyQueueLimit);
        Assert.Equal(0, limits.RequestQueueLimit);
    }

    [Fact]
    public void Default_RestoresEveryTokenItHandsOut()
    {
        // Act
        var limits = TransportRateLimits.Default;

        // Assert
        Assert.True(limits.TokensPerReplenishmentPeriod <= limits.TokenCapacity);
    }

    [Fact]
    public void Create_WithUsableValues_CarriesEveryLimit()
    {
        // Act
        var limits = TransportRateLimits.Create(
            maxConcurrentRequests: 7,
            concurrencyQueueLimit: 3,
            tokenCapacity: 40,
            tokensPerReplenishmentPeriod: 10,
            replenishmentPeriod: TimeSpan.FromSeconds(15),
            requestQueueLimit: 2);

        // Assert
        Assert.Equal(7, limits.MaxConcurrentRequests);
        Assert.Equal(3, limits.ConcurrencyQueueLimit);
        Assert.Equal(40, limits.TokenCapacity);
        Assert.Equal(10, limits.TokensPerReplenishmentPeriod);
        Assert.Equal(TimeSpan.FromSeconds(15), limits.ReplenishmentPeriod);
        Assert.Equal(2, limits.RequestQueueLimit);
    }

    [Theory]
    [InlineData(0, 0, 10, 10, 1, 0)]
    [InlineData(-1, 0, 10, 10, 1, 0)]
    [InlineData(1, -1, 10, 10, 1, 0)]
    [InlineData(1, 0, 0, 10, 1, 0)]
    [InlineData(1, 0, 10, 0, 1, 0)]
    [InlineData(1, 0, 10, 10, 0, 0)]
    [InlineData(1, 0, 10, 10, -1, 0)]
    [InlineData(1, 0, 10, 10, 1, -1)]
    public void Create_WithAnUnusableValue_Throws(
        int maxConcurrentRequests,
        int concurrencyQueueLimit,
        int tokenCapacity,
        int tokensPerReplenishmentPeriod,
        int replenishmentPeriodSeconds,
        int requestQueueLimit)
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => TransportRateLimits.Create(
            maxConcurrentRequests,
            concurrencyQueueLimit,
            tokenCapacity,
            tokensPerReplenishmentPeriod,
            TimeSpan.FromSeconds(replenishmentPeriodSeconds),
            requestQueueLimit));
    }

    [Fact]
    public void Create_RestoringMoreThanTheBucketHolds_Throws()
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => TransportRateLimits.Create(
            maxConcurrentRequests: 4,
            concurrencyQueueLimit: 0,
            tokenCapacity: 10,
            tokensPerReplenishmentPeriod: 11,
            replenishmentPeriod: TimeSpan.FromSeconds(1),
            requestQueueLimit: 0));
    }

    /// <summary>
    /// A queued request is holding a concurrency permit while it waits, because the process-wide limiter is acquired
    /// first and the caller's bucket second. A queue that could hold every permit would let one caller out of capacity
    /// stop the whole surface until its next replenishment, which is the isolation the per-caller bucket exists for,
    /// inverted.
    /// </summary>
    [Theory]
    [InlineData(4, 4)]
    [InlineData(4, 5)]
    public void Create_WithACallerQueueThatCouldHoldEveryPermit_Throws(
        int maxConcurrentRequests,
        int requestQueueLimit)
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => TransportRateLimits.Create(
            maxConcurrentRequests,
            concurrencyQueueLimit: 0,
            tokenCapacity: 10,
            tokensPerReplenishmentPeriod: 10,
            replenishmentPeriod: TimeSpan.FromSeconds(1),
            requestQueueLimit));
    }

    [Fact]
    public void Create_WithACallerQueueBelowThePermitCount_LeavesAPermitForEveryoneElse()
    {
        // Act
        var limits = TransportRateLimits.Create(
            maxConcurrentRequests: 4,
            concurrencyQueueLimit: 0,
            tokenCapacity: 10,
            tokensPerReplenishmentPeriod: 10,
            replenishmentPeriod: TimeSpan.FromSeconds(1),
            requestQueueLimit: 3);

        // Assert
        Assert.Equal(3, limits.RequestQueueLimit);
        Assert.True(limits.RequestQueueLimit < limits.MaxConcurrentRequests);
    }
}
