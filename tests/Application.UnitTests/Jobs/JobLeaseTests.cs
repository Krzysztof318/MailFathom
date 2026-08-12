// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using Xunit;

namespace MailFathom.Application.UnitTests.Jobs;

public sealed class JobLeaseTests
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 12, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A lease that still holds is what lets an attempt go on working; nothing else keeps two workers apart.</summary>
    [Fact]
    public void HasExpiredAt_AnInstantBeforeTheExpiry_ReportsTheLeaseStillHolds()
    {
        // Arrange
        var lease = new JobLease(JobLeaseOwner.Create("attempt-a"), Noon.AddMinutes(5));

        // Act
        var expired = lease.HasExpiredAt(Noon);

        // Assert
        Assert.False(expired);
    }

    /// <summary>
    /// The expiry instant itself counts as expired, matching the claim statement's own comparison. Disagreeing about
    /// that one instant would let a worker believe it held a job the next claim was already free to reclaim.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void HasExpiredAt_TheExpiryOrLater_ReportsTheLeaseIsClaimableAgain(int secondsPastExpiry)
    {
        // Arrange
        var expiresAt = Noon.AddMinutes(5);
        var lease = new JobLease(JobLeaseOwner.Create("attempt-a"), expiresAt);

        // Act
        var expired = lease.HasExpiredAt(expiresAt.AddSeconds(secondsPastExpiry));

        // Assert
        Assert.True(expired);
    }

    /// <summary>
    /// Every write against a leased job is conditional on this comparison, so an attempt that was reclaimed and
    /// finished late must not recognize itself as the holder.
    /// </summary>
    [Fact]
    public void IsHeldBy_TheAttemptThatTookTheJob_ReportsTrueAndNoOtherAttemptDoes()
    {
        // Arrange
        var holder = JobLeaseOwner.Create("attempt-a");
        var lease = new JobLease(holder, Noon.AddMinutes(5));

        // Act & Assert
        Assert.True(lease.IsHeldBy(holder));
        Assert.True(lease.IsHeldBy(JobLeaseOwner.Create("attempt-a")));
        Assert.False(lease.IsHeldBy(JobLeaseOwner.Create("attempt-b")));
    }

    /// <summary>Two replicas cannot see what the other allocated, so a generated owner has to be unique on its own.</summary>
    [Fact]
    public void NewAttempt_CalledTwice_ProducesOwnersThatDoNotMatch()
    {
        // Act
        var owner = JobLeaseOwner.NewAttempt();
        var otherOwner = JobLeaseOwner.NewAttempt();

        // Assert
        Assert.NotEqual(owner, otherOwner);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("attempt\ta")]
    public void Create_AnOwnerThatIsBlankOrCarriesAControlCharacter_IsRefused(string value)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => JobLeaseOwner.Create(value));
    }

    [Fact]
    public void Create_AnOwnerLongerThanTheBound_IsRefused()
    {
        // Arrange
        var overLongOwner = new string('o', JobLeaseOwner.MaximumLength + 1);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => JobLeaseOwner.Create(overLongOwner));
    }
}
