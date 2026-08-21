// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs.Execution;
using Xunit;

namespace MailFathom.Application.UnitTests.Jobs.Execution;

public sealed class JobCapacitySettingsTests
{
    /// <summary>A capacity a deployment could actually run under is accepted whole, so the mapping from options is one step.</summary>
    [Fact]
    public void Create_ACapacityWithinItsRules_KeepsEveryBound()
    {
        // Act
        var capacity = JobCapacitySettings.Create(
            maxConcurrentJobs: 4,
            maxConcurrentJobsPerType: 2,
            maxQueueDepthPerType: 10000);

        // Assert
        Assert.Equal(4, capacity.MaxConcurrentJobs);
        Assert.Equal(2, capacity.MaxConcurrentJobsPerType);
        Assert.Equal(10000, capacity.MaxQueueDepthPerType);
    }

    /// <summary>A bound that admits nothing is not a bound, so a queue nothing may run in or wait in is refused.</summary>
    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(-1, 1, 1)]
    [InlineData(2, 0, 1)]
    [InlineData(2, -1, 1)]
    [InlineData(2, 1, 0)]
    [InlineData(2, 1, -1)]
    public void Create_ABoundThatIsNotPositive_IsRefused(
        int maxConcurrentJobs,
        int maxConcurrentJobsPerType,
        int maxQueueDepthPerType)
    {
        // Act
        var refusal = Record.Exception(() => JobCapacitySettings.Create(
            maxConcurrentJobs,
            maxConcurrentJobsPerType,
            maxQueueDepthPerType));

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(refusal);
    }

    /// <summary>
    /// A per-type ceiling above the process ceiling states a bound nothing can reach, because the process ceiling
    /// already caps it — and a bound that cannot be reached is one an operator believes they have.
    /// </summary>
    [Fact]
    public void Create_APerTypeCeilingAboveTheProcessCeiling_IsRefused()
    {
        // Act
        var refusal = Record.Exception(() => JobCapacitySettings.Create(
            maxConcurrentJobs: 2,
            maxConcurrentJobsPerType: 3,
            maxQueueDepthPerType: 10));

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(refusal);
    }

    /// <summary>The two ceilings are equal at the boundary, which is one type being allowed the whole instance.</summary>
    [Fact]
    public void Create_APerTypeCeilingEqualToTheProcessCeiling_IsAccepted()
    {
        // Act
        var capacity = JobCapacitySettings.Create(
            maxConcurrentJobs: 2,
            maxConcurrentJobsPerType: 2,
            maxQueueDepthPerType: 10);

        // Assert
        Assert.Equal(capacity.MaxConcurrentJobs, capacity.MaxConcurrentJobsPerType);
    }
}
