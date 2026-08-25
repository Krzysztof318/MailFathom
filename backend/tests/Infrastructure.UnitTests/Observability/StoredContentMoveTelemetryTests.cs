// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using System.Diagnostics;
using MailFathom.Application.EmailContent.Move;
using MailFathom.Common.Observability;
using MailFathom.Infrastructure.Observability;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Observability;

/// <summary>Covers what one bounded pass of the content move publishes: the counters, the reason, and the span.</summary>
/// <remarks>
/// The meter and the activity source are the process's, and this move publishes under no account or folder — a payload
/// kind is deliberately not a dimension either. Nothing else in the assembly writes to these four instruments or opens
/// this span, so reading them by name is what keeps another class's traffic out of these assertions.
/// </remarks>
public sealed class StoredContentMoveTelemetryTests : IDisposable
{
    private const string MovedInstrumentName = "mailfathom.mail.content.move.moved";

    private const string MovedBytesInstrumentName = "mailfathom.mail.content.move.moved.bytes";

    private const string RefusedInstrumentName = "mailfathom.mail.content.move.refused";

    private const string PassDurationInstrumentName = "mailfathom.mail.content.move.pass.duration";

    private readonly FakeTimeProvider timeProvider = new(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));
    private readonly ConcurrentBag<Activity> published = [];
    private readonly ActivityListener listener;

    public StoredContentMoveTelemetryTests()
    {
        this.listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == Telemetry.Name,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.OperationName == StoredContentMoveTelemetry.PassSpanName)
                {
                    this.published.Add(activity);
                }
            },
        };

        ActivitySource.AddActivityListener(this.listener);
    }

    public void Dispose() => this.listener.Dispose();

    /// <summary>
    /// The two counters an operator watches a move by: how much of the mailbox is now in the bucket, and what that came
    /// to in bytes. They are published per payload rather than per pass, because a pass a restart stopped has still
    /// moved everything it repointed.
    /// </summary>
    [Fact]
    public void Copied_APayloadCarriedIntoTheBucket_PublishesOnePayloadAndItsBytes()
    {
        // Arrange
        var telemetry = new StoredContentMoveTelemetry(this.timeProvider);
        using var measurements = new RecordedMailFathomMeasurements(
            MovedInstrumentName,
            MovedBytesInstrumentName);

        // Act
        using (var pass = telemetry.BeginPass())
        {
            pass.Copied(4_096);
            pass.Copied(2_048);
        }

        // Assert
        Assert.Equal([1d, 1d], measurements.ValuesOf(MovedInstrumentName));
        Assert.Equal([4_096d, 2_048d], measurements.ValuesOf(MovedBytesInstrumentName));
    }

    /// <summary>
    /// A refusal carries why, because the acts differ: stored bytes that disagree with their own row are a mailbox to
    /// re-synchronize, an object that came back wrong is an endpoint to look at, and a payload too large to hold is a
    /// bound to raise.
    /// </summary>
    [Theory]
    [InlineData(StoredContentMoveFailure.SourceMismatch, "source_mismatch")]
    [InlineData(StoredContentMoveFailure.ObjectMismatch, "object_mismatch")]
    [InlineData(StoredContentMoveFailure.ObjectAbsent, "object_absent")]
    [InlineData(StoredContentMoveFailure.Oversized, "oversized")]
    public void Failed_APayloadLeftInTheDatabase_PublishesItUnderTheReasonItWasLeftFor(
        StoredContentMoveFailure failure,
        string expectedReason)
    {
        // Arrange
        var telemetry = new StoredContentMoveTelemetry(this.timeProvider);
        using var measurements = new RecordedMailFathomMeasurements(RefusedInstrumentName);

        // Act
        using (var pass = telemetry.BeginPass())
        {
            pass.Failed(failure);
        }

        // Assert
        Assert.Equal(1d, Assert.Single(measurements.ValuesOf(RefusedInstrumentName)));
        Assert.Equal(
            expectedReason,
            Assert.Single(measurements.DimensionOf(RefusedInstrumentName, StoredContentMoveTelemetry.FailureTagName)));
    }

    /// <summary>A pass that refused nothing publishes no refusal, so a quiet move is not a stream of zeroes.</summary>
    [Fact]
    public void Dispose_APassThatRefusedNothing_PublishesNoRefusal()
    {
        // Arrange
        var telemetry = new StoredContentMoveTelemetry(this.timeProvider);
        using var measurements = new RecordedMailFathomMeasurements(RefusedInstrumentName);

        // Act
        using (var pass = telemetry.BeginPass())
        {
            pass.Copied(1);
        }

        // Assert
        Assert.Empty(measurements.ValuesOf(RefusedInstrumentName));
    }

    /// <summary>How long a pass took is what an operator reads the move's cost from, and it is measured once it ends.</summary>
    [Fact]
    public void Dispose_APassThatEnded_PublishesHowLongItTook()
    {
        // Arrange
        var telemetry = new StoredContentMoveTelemetry(this.timeProvider);
        using var measurements = new RecordedMailFathomMeasurements(PassDurationInstrumentName);

        // Act
        using (var pass = telemetry.BeginPass())
        {
            this.timeProvider.Advance(TimeSpan.FromSeconds(9));
        }

        // Assert
        Assert.Equal(9d, Assert.Single(measurements.ValuesOf(PassDurationInstrumentName)));
    }

    /// <summary>Ending a pass twice is one pass, because a doubled duration would read as a move that had got slower.</summary>
    [Fact]
    public void Dispose_APassEndedTwice_PublishesItsDurationOnce()
    {
        // Arrange
        var telemetry = new StoredContentMoveTelemetry(this.timeProvider);
        using var measurements = new RecordedMailFathomMeasurements(PassDurationInstrumentName);
        var pass = telemetry.BeginPass();

        // Act
        pass.Dispose();
        pass.Dispose();

        // Assert
        Assert.Single(measurements.ValuesOf(PassDurationInstrumentName));
    }

    /// <summary>
    /// Reaching the end of the content is an event rather than a counter: it happens once per move and what it settles
    /// is which pass a deployment was on when the backlog ran out.
    /// </summary>
    [Fact]
    public void ReachedEndOfContent_ThePassThatWalkedPastTheLastPayload_PublishesItOnItsOwnSpan()
    {
        // Arrange
        var telemetry = new StoredContentMoveTelemetry(this.timeProvider);

        // Act
        using (var pass = telemetry.BeginPass())
        {
            pass.ReachedEndOfContent();
        }

        // Assert
        Assert.Equal(
            StoredContentMoveTelemetry.ReachedEndEventName,
            Assert.Single(this.OnlyPass().Events).Name);
    }

    /// <summary>A pass that found payloads remaining says nothing about an end it did not reach.</summary>
    [Fact]
    public void Dispose_APassThatLeftPayloadsBehind_PublishesNoEndOfContentEvent()
    {
        // Arrange
        var telemetry = new StoredContentMoveTelemetry(this.timeProvider);

        // Act
        using (var pass = telemetry.BeginPass())
        {
            pass.Copied(1);
        }

        // Assert
        Assert.Empty(this.OnlyPass().Events);
    }

    private Activity OnlyPass() =>
        Assert.Single(this.published, activity => activity.OperationName == StoredContentMoveTelemetry.PassSpanName);
}
