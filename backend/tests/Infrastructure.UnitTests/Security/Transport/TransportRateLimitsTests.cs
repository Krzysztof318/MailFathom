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
        Assert.True(limits.MaxConcurrentRequestsPerUser > 0);
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
    public void Default_LeavesTheProcessPermitsForUsersBeyondOneBusyOne()
    {
        // Act
        var limits = TransportRateLimits.Default;

        // Assert
        Assert.True(limits.MaxConcurrentRequestsPerUser < limits.MaxConcurrentRequests);
    }

    [Fact]
    public void Create_WithUsableValues_CarriesEveryLimit()
    {
        // Act
        var limits = TransportRateLimits.Create(
            maxConcurrentRequests: 7,
            maxConcurrentRequestsPerUser: 4,
            concurrencyQueueLimit: 3,
            tokenCapacity: 40,
            tokensPerReplenishmentPeriod: 10,
            replenishmentPeriod: TimeSpan.FromSeconds(15),
            requestQueueLimit: 2);

        // Assert
        Assert.Equal(7, limits.MaxConcurrentRequests);
        Assert.Equal(4, limits.MaxConcurrentRequestsPerUser);
        Assert.Equal(3, limits.ConcurrencyQueueLimit);
        Assert.Equal(40, limits.TokenCapacity);
        Assert.Equal(10, limits.TokensPerReplenishmentPeriod);
        Assert.Equal(TimeSpan.FromSeconds(15), limits.ReplenishmentPeriod);
        Assert.Equal(2, limits.RequestQueueLimit);
    }

    [Theory]
    [InlineData(0, 1, 0, 10, 10, 1, 0)]
    [InlineData(-1, 1, 0, 10, 10, 1, 0)]
    [InlineData(2, 0, 0, 10, 10, 1, 0)]
    [InlineData(2, 1, -1, 10, 10, 1, 0)]
    [InlineData(2, 1, 0, 0, 10, 1, 0)]
    [InlineData(2, 1, 0, 10, 0, 1, 0)]
    [InlineData(2, 1, 0, 10, 10, 0, 0)]
    [InlineData(2, 1, 0, 10, 10, -1, 0)]
    [InlineData(2, 1, 0, 10, 10, 1, -1)]
    public void Create_WithAnUnusableValue_Throws(
        int maxConcurrentRequests,
        int maxConcurrentRequestsPerUser,
        int concurrencyQueueLimit,
        int tokenCapacity,
        int tokensPerReplenishmentPeriod,
        int replenishmentPeriodSeconds,
        int requestQueueLimit)
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => TransportRateLimits.Create(
            maxConcurrentRequests,
            maxConcurrentRequestsPerUser,
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
            maxConcurrentRequestsPerUser: 2,
            concurrencyQueueLimit: 0,
            tokenCapacity: 10,
            tokensPerReplenishmentPeriod: 11,
            replenishmentPeriod: TimeSpan.FromSeconds(1),
            requestQueueLimit: 0));
    }

    /// <summary>A user allowed every permit the process has is a user whose burst refuses everybody else, which is what the per-user ceiling exists to rule out.</summary>
    [Theory]
    [InlineData(4, 4)]
    [InlineData(4, 5)]
    public void Create_WithAUserCeilingThatCouldHoldEveryPermit_Throws(
        int maxConcurrentRequests,
        int maxConcurrentRequestsPerUser)
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => TransportRateLimits.Create(
            maxConcurrentRequests,
            maxConcurrentRequestsPerUser,
            concurrencyQueueLimit: 0,
            tokenCapacity: 10,
            tokensPerReplenishmentPeriod: 10,
            replenishmentPeriod: TimeSpan.FromSeconds(1),
            requestQueueLimit: 0));
    }

    /// <summary>
    /// A queued request is holding a concurrency permit while it waits, because the concurrency limiters are acquired
    /// first and the user's bucket second. A queue that could hold every permit would let one user out of capacity stop
    /// the whole surface until its next replenishment, which is the isolation the per-user bucket exists for, inverted.
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
            maxConcurrentRequestsPerUser: 1,
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
            maxConcurrentRequestsPerUser: 1,
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
