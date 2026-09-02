// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using Xunit;

namespace MailFathom.Application.UnitTests.Jobs;

public sealed class JobClaimRequestTests
{
    /// <summary>
    /// The claim carries the types this process has a handler for, which is what leaves a job an older replica cannot
    /// run where it is instead of failing it. A claim that named none would drain the queue into a process unable to
    /// act on any of it.
    /// </summary>
    [Fact]
    public void Create_ADeclaredTypeAndPositiveBounds_KeepsWhatTheClaimWillSelectOn()
    {
        // Arrange
        var owner = JobLeaseOwner.Create("attempt-a");

        // Act
        var request = JobClaimRequest.Create([JobType.ClassifyEmailSpam], 5, TimeSpan.FromMinutes(2), owner);

        // Assert
        Assert.Equal([JobType.ClassifyEmailSpam], request.HandledTypes);
        Assert.Equal(5, request.BatchSize);
        Assert.Equal(TimeSpan.FromMinutes(2), request.LeaseDuration);
        Assert.Equal(owner, request.Owner);
    }

    /// <summary>A caller keeping its own list must not be able to widen a claim that has already been composed.</summary>
    [Fact]
    public void Create_AListTheCallerGoesOnToChange_DoesNotChangeTheClaim()
    {
        // Arrange
        var handledTypes = new List<JobType> { JobType.ClassifyEmailSpam };
        var request = JobClaimRequest.Create(handledTypes, 1, TimeSpan.FromMinutes(1), JobLeaseOwner.Create("a"));

        // Act
        handledTypes.Clear();

        // Assert
        Assert.Equal([JobType.ClassifyEmailSpam], request.HandledTypes);
    }

    [Fact]
    public void Create_NoHandledType_IsRefused()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(
            () => JobClaimRequest.Create([], 1, TimeSpan.FromMinutes(1), JobLeaseOwner.Create("a")));
    }

    /// <summary>The unspecified default names no type, so a claim carrying one would filter on a name nothing stores.</summary>
    [Fact]
    public void Create_TheUnspecifiedDefault_IsRefused()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(
            () => JobClaimRequest.Create([default], 1, TimeSpan.FromMinutes(1), JobLeaseOwner.Create("a")));
    }

    /// <summary>A repeated type widens no predicate and would absorb a process that registered one handler twice.</summary>
    [Fact]
    public void Create_ARepeatedType_IsRefused()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => JobClaimRequest.Create(
            [JobType.ClassifyEmailSpam, JobType.ClassifyEmailSpam],
            1,
            TimeSpan.FromMinutes(1),
            JobLeaseOwner.Create("a")));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_ABatchSizeThatIsNotPositive_IsRefused(int batchSize)
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => JobClaimRequest.Create(
            [JobType.ClassifyEmailSpam],
            batchSize,
            TimeSpan.FromMinutes(1),
            JobLeaseOwner.Create("a")));
    }

    /// <summary>A lease that expires the moment it is taken would make every claim reclaimable at once.</summary>
    [Fact]
    public void Create_ALeaseDurationThatIsNotPositive_IsRefused()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => JobClaimRequest.Create(
            [JobType.ClassifyEmailSpam],
            1,
            TimeSpan.Zero,
            JobLeaseOwner.Create("a")));
    }
}
