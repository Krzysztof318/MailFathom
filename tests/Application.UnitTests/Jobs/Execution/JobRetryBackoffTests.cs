// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs.Execution;
using Xunit;

namespace MailFathom.Application.UnitTests.Jobs.Execution;

public sealed class JobRetryBackoffTests
{
    private static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(30);

    /// <summary>Approaching a struggling dependency less often is the whole point, so the range doubles per attempt.</summary>
    [Theory]
    [InlineData(1, 15, 30)]
    [InlineData(2, 30, 60)]
    [InlineData(3, 60, 120)]
    [InlineData(4, 120, 240)]
    public void DelayBeforeNextAttempt_EachFurtherAttempt_DrawsFromATwiceWiderRange(
        int attemptCount,
        int floorSeconds,
        int ceilingSeconds)
    {
        // Act
        var delay = JobRetryBackoff.DelayBeforeNextAttempt(BaseDelay, MaxDelay, attemptCount);

        // Assert
        Assert.InRange(delay, TimeSpan.FromSeconds(floorSeconds), TimeSpan.FromSeconds(ceilingSeconds));
    }

    /// <summary>
    /// Without a ceiling a job that kept failing would drift towards never being attempted again, which is a dead letter
    /// nobody can see rather than the bounded backoff this is.
    /// </summary>
    [Theory]
    [InlineData(10)]
    [InlineData(40)]
    [InlineData(int.MaxValue)]
    public void DelayBeforeNextAttempt_AnAttemptCountPastTheCeiling_StaysWithinIt(int attemptCount)
    {
        // Act
        var delay = JobRetryBackoff.DelayBeforeNextAttempt(BaseDelay, MaxDelay, attemptCount);

        // Assert
        Assert.InRange(delay, MaxDelay / 2, MaxDelay);
    }

    /// <summary>
    /// Jobs that failed together failed on the same dependency, so an exact delay would return every one of them to it
    /// in the same instant. The draw is what spreads them, and a formula that lost it would still pass every bound above.
    /// </summary>
    [Fact]
    public void DelayBeforeNextAttempt_ManyJobsFailingAtOnce_DoesNotGiveThemAllTheSameDelay()
    {
        // Act
        var delays = Enumerable
            .Range(0, 64)
            .Select(_ => JobRetryBackoff.DelayBeforeNextAttempt(BaseDelay, MaxDelay, attemptCount: 3))
            .ToArray();

        // Assert
        Assert.True(delays.Distinct().Count() > 1, "Every job drew the same retry delay, so the backoff carries no jitter.");
    }

    /// <summary>A delay drawn from a range that starts at zero would be no delay at all for the job that drew the floor.</summary>
    [Fact]
    public void DelayBeforeNextAttempt_AnyAttempt_IsNeverZero()
    {
        // Act
        var delays = Enumerable
            .Range(1, 8)
            .Select(attemptCount => JobRetryBackoff.DelayBeforeNextAttempt(BaseDelay, MaxDelay, attemptCount))
            .ToArray();

        // Assert
        Assert.All(delays, delay => Assert.True(delay > TimeSpan.Zero));
    }

    /// <summary>A bound that is not a bound would be read as one, so each is refused rather than corrected.</summary>
    [Theory]
    [InlineData(0, 60, 1)]
    [InlineData(-1, 60, 1)]
    [InlineData(30, 10, 1)]
    [InlineData(30, 60, 0)]
    [InlineData(30, 60, -1)]
    public void DelayBeforeNextAttempt_AnUnusableBound_IsRefused(
        int baseDelaySeconds,
        int maxDelaySeconds,
        int attemptCount)
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => JobRetryBackoff.DelayBeforeNextAttempt(
            TimeSpan.FromSeconds(baseDelaySeconds),
            TimeSpan.FromSeconds(maxDelaySeconds),
            attemptCount));
    }
}
