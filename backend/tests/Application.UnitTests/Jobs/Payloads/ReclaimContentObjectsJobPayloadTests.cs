// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Payloads;
using Xunit;

namespace MailFathom.Application.UnitTests.Jobs.Payloads;

/// <summary>Covers how one sweep's segments are chained, and why each of them is enqueueable at all.</summary>
/// <remarks>
/// An idempotency key is unique for the life of the queue table, so two claims matter here at once: two segments of one
/// sweep never compose the same key, and one segment composes the same key however many times it is attempted. A chain
/// that broke the first would have its second segment silently answered as a job already enqueued and the tail of the
/// bucket would never be swept; one that broke the second would fork a sweep into two chains walking one bucket.
/// </remarks>
public sealed class ReclaimContentObjectsJobPayloadTests
{
    private const string Sweep = "sweep-of-the-occasion";

    /// <summary>The first segment begins at the start of the listing and already names the sweep it begins.</summary>
    [Fact]
    public void FromTheStart_TheSegmentAScheduleDispatches_BeginsTheListingAndNamesItsSweep()
    {
        // Act
        var payload = ReclaimContentObjectsJobPayload.FromTheStart(Sweep);

        // Assert
        Assert.Null(payload.ResumeFrom);
        Assert.Equal(Sweep, payload.SweepId);
        Assert.Equal(0, payload.Segment);
        Assert.Equal(JobType.ReclaimContentObjects, payload.JobType);
    }

    /// <summary>A segment belongs to the sweep the occasion named rather than to one the hand-on invented.</summary>
    [Fact]
    public void ContinuingFrom_TheFirstHandOn_StaysInTheSweepTheOccasionNamed()
    {
        // Arrange
        var first = ReclaimContentObjectsJobPayload.FromTheStart(Sweep);

        // Act
        var second = first.ContinuingFrom("half-way", TimeSpan.Zero);

        // Assert
        Assert.Equal(Sweep, second.SweepId);
        Assert.Equal(1, second.Segment);
        Assert.Equal("half-way", second.ResumeFrom);
    }

    /// <summary>Every segment after the first stays in the sweep it was handed on from rather than starting one.</summary>
    [Fact]
    public void ContinuingFrom_ALaterHandOn_StaysInTheSameSweepAndCountsOn()
    {
        // Arrange
        var second = ReclaimContentObjectsJobPayload.FromTheStart(Sweep).ContinuingFrom("half-way", TimeSpan.Zero);

        // Act
        var third = second.ContinuingFrom("further-on", TimeSpan.Zero);

        // Assert
        Assert.Equal(second.SweepId, third.SweepId);
        Assert.Equal(2, third.Segment);
    }

    /// <summary>A key a second segment shared would be answered as a job already enqueued, and the sweep would stop there.</summary>
    [Fact]
    public void ToIdempotencyKey_TwoSegmentsOfOneSweep_ComposeDifferentIdentities()
    {
        // Arrange
        var second = ReclaimContentObjectsJobPayload.FromTheStart(Sweep).ContinuingFrom("half-way", TimeSpan.Zero);
        var third = second.ContinuingFrom("further-on", TimeSpan.Zero);

        // Act
        var secondKey = second.ToIdempotencyKey();
        var thirdKey = third.ToIdempotencyKey();

        // Assert
        Assert.NotEqual(secondKey.Value, thirdKey.Value);
        Assert.StartsWith(JobType.ReclaimContentObjects.Name, secondKey.Value, StringComparison.Ordinal);
    }

    /// <summary>Two occasions are two sweeps, so the segments carrying them are never deduped against each other.</summary>
    [Fact]
    public void ToIdempotencyKey_TheSameSegmentOfTwoSweeps_ComposesDifferentIdentities()
    {
        // Arrange
        var ofOneOccasion = ReclaimContentObjectsJobPayload.FromTheStart(Sweep).ContinuingFrom("half-way", TimeSpan.Zero);
        var ofAnother = ReclaimContentObjectsJobPayload.FromTheStart("sweep-of-the-next-occasion")
            .ContinuingFrom("half-way", TimeSpan.Zero);

        // Act, Assert
        Assert.NotEqual(ofOneOccasion.ToIdempotencyKey().Value, ofAnother.ToIdempotencyKey().Value);
    }

    /// <summary>
    /// Two hand-ons from one segment compose one key, which is what keeps a repeated attempt from forking the sweep.
    /// </summary>
    /// <remarks>
    /// The executor can run one leased row's handler twice against the same payload — the work succeeds and the
    /// compare-and-set recording it does not — and the positions the two attempts stop at need not agree. What has to
    /// agree is the key, so that the second hand-on is answered with the segment the first one enqueued.
    /// </remarks>
    [Fact]
    public void ToIdempotencyKey_TwoHandOnsFromOneSegment_ComposeOneIdentity()
    {
        // Arrange
        var segment = ReclaimContentObjectsJobPayload.FromTheStart(Sweep);

        // Act
        var ofOneAttempt = segment.ContinuingFrom("half-way", TimeSpan.Zero).ToIdempotencyKey();
        var ofTheRepeat = segment.ContinuingFrom("further-on", TimeSpan.FromDays(9)).ToIdempotencyKey();

        // Assert
        Assert.Equal(ofOneAttempt.Value, ofTheRepeat.Value);
    }

    /// <summary>A key names a position in a listing nowhere, because a listing position is what a key must not be composed of.</summary>
    [Fact]
    public void ToIdempotencyKey_ASegmentResumingFromAPosition_CarriesNoPartOfThatPosition()
    {
        // Arrange
        var segment = ReclaimContentObjectsJobPayload.FromTheStart(Sweep)
            .ContinuingFrom("mailfathom-incoming-recognizable", TimeSpan.Zero);

        // Act
        var key = segment.ToIdempotencyKey();

        // Assert
        Assert.DoesNotContain("recognizable", key.Value, StringComparison.Ordinal);
    }

    /// <summary>A segment belonging to no sweep could compose a key another sweep's segment shares.</summary>
    [Fact]
    public void FromTheStart_NoSweep_IsRefused() =>

        // Act, Assert
        Assert.Throws<ArgumentException>(() => ReclaimContentObjectsJobPayload.FromTheStart("  "));

    /// <summary>A segment that resumes nowhere is the first one, which nothing hands on to.</summary>
    [Fact]
    public void ContinuingFrom_NoPosition_IsRefused() =>

        // Act, Assert
        Assert.Throws<ArgumentException>(
            () => ReclaimContentObjectsJobPayload.FromTheStart(Sweep).ContinuingFrom("  ", TimeSpan.Zero));

    /// <summary>What the earlier segments met travels with the position, or the gauge would describe the last one alone.</summary>
    [Fact]
    public void ContinuingFrom_AHandOn_CarriesTheOldestOrphanTheSweepHasMet()
    {
        // Arrange
        var first = ReclaimContentObjectsJobPayload.FromTheStart(Sweep);

        // Act
        var second = first.ContinuingFrom("half-way", TimeSpan.FromDays(9));

        // Assert
        Assert.Equal(TimeSpan.FromDays(9), second.OldestOrphanAge);
    }
}
