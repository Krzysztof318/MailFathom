// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Payloads;
using Xunit;

namespace MailFathom.Application.UnitTests.Jobs.Payloads;

/// <summary>Covers how one sweep's segments are chained, and why each of them is enqueueable at all.</summary>
/// <remarks>
/// An idempotency key is unique for the life of the queue table, so the claim that matters here is that two segments of
/// one sweep never compose the same one. A chain that did would have its second segment silently answered as a job
/// already enqueued, and the tail of the bucket would never be swept.
/// </remarks>
public sealed class ReclaimContentObjectsJobPayloadTests
{
    /// <summary>The first segment begins at the start of the listing and belongs to no chain yet.</summary>
    [Fact]
    public void FromTheStart_TheSegmentASchedulDispatches_BeginsTheListingAndNamesNoSweep()
    {
        // Act
        var payload = ReclaimContentObjectsJobPayload.FromTheStart();

        // Assert
        Assert.Null(payload.ResumeFrom);
        Assert.Null(payload.SweepId);
        Assert.Equal(0, payload.Segment);
        Assert.Equal(JobType.ReclaimContentObjects, payload.JobType);
    }

    /// <summary>A segment always belongs to a named sweep, however the chain it is part of began.</summary>
    [Fact]
    public void ContinuingFrom_TheFirstHandOn_MintsTheSweepTheChainIsNamedBy()
    {
        // Arrange
        var first = ReclaimContentObjectsJobPayload.FromTheStart();

        // Act
        var second = first.ContinuingFrom("half-way");

        // Assert
        Assert.NotNull(second.SweepId);
        Assert.Equal(1, second.Segment);
        Assert.Equal("half-way", second.ResumeFrom);
    }

    /// <summary>Every segment after the first stays in the sweep it was handed on from rather than starting one.</summary>
    [Fact]
    public void ContinuingFrom_ALaterHandOn_StaysInTheSameSweepAndCountsOn()
    {
        // Arrange
        var second = ReclaimContentObjectsJobPayload.FromTheStart().ContinuingFrom("half-way");

        // Act
        var third = second.ContinuingFrom("further-on");

        // Assert
        Assert.Equal(second.SweepId, third.SweepId);
        Assert.Equal(2, third.Segment);
    }

    /// <summary>A key a second segment shared would be answered as a job already enqueued, and the sweep would stop there.</summary>
    [Fact]
    public void ToIdempotencyKey_TwoSegmentsOfOneSweep_ComposeDifferentIdentities()
    {
        // Arrange
        var second = ReclaimContentObjectsJobPayload.FromTheStart().ContinuingFrom("half-way");
        var third = second.ContinuingFrom("further-on");

        // Act
        var secondKey = second.ToIdempotencyKey();
        var thirdKey = third.ToIdempotencyKey();

        // Assert
        Assert.NotEqual(secondKey.Value, thirdKey.Value);
        Assert.StartsWith(JobType.ReclaimContentObjects.Name, secondKey.Value, StringComparison.Ordinal);
    }

    /// <summary>A key names a position in a listing nowhere, because a listing position is what a key must not be composed of.</summary>
    [Fact]
    public void ToIdempotencyKey_ASegmentResumingFromAPosition_CarriesNoPartOfThatPosition()
    {
        // Arrange
        var segment = ReclaimContentObjectsJobPayload.FromTheStart().ContinuingFrom("mailfathom-incoming-recognizable");

        // Act
        var key = segment.ToIdempotencyKey();

        // Assert
        Assert.DoesNotContain("recognizable", key.Value, StringComparison.Ordinal);
    }

    /// <summary>The first segment is enqueued by the schedule under the occasion's own key, so it composes none.</summary>
    [Fact]
    public void ToIdempotencyKey_TheSegmentASchedulDispatches_IsRefused() => Assert.Throws<InvalidOperationException>(
        () => ReclaimContentObjectsJobPayload.FromTheStart().ToIdempotencyKey());

    /// <summary>A segment that resumes nowhere is the first one, which nothing hands on to.</summary>
    [Fact]
    public void ContinuingFrom_NoPosition_IsRefused() => Assert.Throws<ArgumentException>(
        () => ReclaimContentObjectsJobPayload.FromTheStart().ContinuingFrom("  "));
}
