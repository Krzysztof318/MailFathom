// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using System.Diagnostics;
using MailFathom.Common.Observability;
using MailFathom.Infrastructure.Observability;
using MailFathom.Infrastructure.UnitTests.TestDoubles;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Observability;

/// <summary>Covers the span and the instruments a read and a write of stored raw MIME publish.</summary>
/// <remarks>
/// It listens to the real activity source and narrows to this span's own name, so a span published by another test class
/// at the same moment is not mistaken for one of these.
/// </remarks>
public sealed class StoredEmailContentTelemetryTests : IDisposable
{
    private const string ReadBytesInstrument = "mailfathom.mail.content.read.bytes";

    private const string ReadDurationInstrument = "mailfathom.mail.content.read.duration";

    private const string WriteBytesInstrument = "mailfathom.mail.content.write.bytes";

    private const string WriteDurationInstrument = "mailfathom.mail.content.write.duration";

    private const string OutcomeTagName = "mailfathom.mail.content.outcome";

    private readonly ConcurrentBag<Activity> published = [];
    private readonly ActivityListener listener;

    public StoredEmailContentTelemetryTests()
    {
        this.listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == Telemetry.Name,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.OperationName == StoredEmailContentTelemetry.ReadSpanName)
                {
                    this.published.Add(activity);
                }
            },
        };

        ActivitySource.AddActivityListener(this.listener);
    }

    public void Dispose() => this.listener.Dispose();

    /// <summary>What a read that found content says: that it found some, and how many bytes of it there were.</summary>
    [Fact]
    public void BeginRead_ContentThatWasFound_PublishesItsSize()
    {
        // Arrange
        var telemetry = TelemetryOver(out _);

        // Act
        using (var read = telemetry.BeginRead())
        {
            read.Found(42_000);
        }

        // Assert
        var span = Assert.Single(this.published);

        Assert.Equal("read_stored_email_content", span.OperationName);
        Assert.Equal(
            [
                ("mailfathom.mail.content.found", "True"),
                ("mailfathom.mail.content.bytes", "42000"),
            ],
            span.TagObjects.Select(tag => (tag.Key, tag.Value?.ToString())));
        Assert.Equal(ActivityStatusCode.Ok, span.Status);
    }

    /// <summary>An email this deployment holds no content for is an answer rather than a failure.</summary>
    [Fact]
    public void BeginRead_ContentThatIsNotStored_PublishesTheAbsenceWithoutASize()
    {
        // Arrange
        var telemetry = TelemetryOver(out _);

        // Act
        using (var read = telemetry.BeginRead())
        {
            read.Absent();
        }

        // Assert
        var span = Assert.Single(this.published);

        Assert.Equal(false, span.GetTagItem("mailfathom.mail.content.found"));
        Assert.Null(span.GetTagItem("mailfathom.mail.content.bytes"));
        Assert.Equal(ActivityStatusCode.Ok, span.Status);
    }

    /// <summary>A read that reported neither outcome is one that threw, and the span says so rather than staying silent.</summary>
    [Fact]
    public void BeginRead_AReadThatReportedNothing_PublishesItAsAnError()
    {
        // Arrange
        var telemetry = TelemetryOver(out _);

        // Act
        using (telemetry.BeginRead())
        {
        }

        // Assert
        var span = Assert.Single(this.published);

        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Empty(span.TagObjects);
    }

    /// <summary>
    /// The payload this span describes is a whole message, so what it may say about it is a size and nothing else: no
    /// stored identity, no account, no folder, and no part of the mail itself.
    /// </summary>
    [Fact]
    public void BeginRead_AnyRead_PublishesNothingBeyondASizeAndWhetherAnythingWasThere()
    {
        // Arrange
        var telemetry = TelemetryOver(out _);

        // Act
        using (var read = telemetry.BeginRead())
        {
            read.Found(1_024);
        }

        // Assert
        Assert.Equal(
            ["mailfathom.mail.content.found", "mailfathom.mail.content.bytes"],
            Assert.Single(this.published).TagObjects.Select(tag => tag.Key));
    }

    /// <summary>
    /// A span answers why one read was slow; the histograms answer whether reads are getting slower and whether the
    /// messages are getting larger, which is what a per-read span cannot be asked.
    /// </summary>
    [Fact]
    public void BeginRead_ContentThatWasFound_RecordsItsSizeAndDurationAsDistributions()
    {
        // Arrange
        var telemetry = TelemetryOver(out var timeProvider);
        using var measurements = new RecordedMailFathomMeasurements(ReadBytesInstrument, ReadDurationInstrument);

        // Act
        using (var read = telemetry.BeginRead())
        {
            timeProvider.Advance(TimeSpan.FromMilliseconds(250));
            read.Found(42_000);
        }

        // Assert
        Assert.Equal([42_000d], measurements.ValuesOf(ReadBytesInstrument));
        Assert.Equal([0.25], measurements.ValuesOf(ReadDurationInstrument));
        Assert.Equal(["found"], measurements.DimensionOf(ReadDurationInstrument, OutcomeTagName));
    }

    /// <summary>
    /// An absent message is timed and not sized, because there was nothing to size — a zero there would pull the
    /// distribution an operator sizes storage from towards a message that never existed.
    /// </summary>
    [Fact]
    public void BeginRead_ContentThatIsNotStored_RecordsADurationAndNoSize()
    {
        // Arrange
        var telemetry = TelemetryOver(out _);
        using var measurements = new RecordedMailFathomMeasurements(ReadBytesInstrument, ReadDurationInstrument);

        // Act
        using (var read = telemetry.BeginRead())
        {
            read.Absent();
        }

        // Assert
        Assert.Empty(measurements.ValuesOf(ReadBytesInstrument));
        Assert.Equal(["absent"], measurements.DimensionOf(ReadDurationInstrument, OutcomeTagName));
    }

    /// <summary>A read that threw is timed under its own outcome, so a store that started failing is not simply absent.</summary>
    [Fact]
    public void BeginRead_AReadThatReportedNothing_TimesItAsAFailure()
    {
        // Arrange
        var telemetry = TelemetryOver(out _);
        using var measurements = new RecordedMailFathomMeasurements(ReadDurationInstrument);

        // Act
        using (telemetry.BeginRead())
        {
        }

        // Assert
        Assert.Equal(["failed"], measurements.DimensionOf(ReadDurationInstrument, OutcomeTagName));
    }

    /// <summary>A write is measured and not spanned, because one span per stored message would say less than this does.</summary>
    [Fact]
    public void BeginWrite_ContentThatWasStored_RecordsItsSizeAndDurationAndNoSpan()
    {
        // Arrange
        var telemetry = TelemetryOver(out var timeProvider);
        using var measurements = new RecordedMailFathomMeasurements(WriteBytesInstrument, WriteDurationInstrument);

        // Act
        using (var write = telemetry.BeginWrite())
        {
            timeProvider.Advance(TimeSpan.FromMilliseconds(500));
            write.Stored(96_000);
        }

        // Assert
        Assert.Equal([96_000d], measurements.ValuesOf(WriteBytesInstrument));
        Assert.Equal([0.5], measurements.ValuesOf(WriteDurationInstrument));
        Assert.Equal(["stored"], measurements.DimensionOf(WriteDurationInstrument, OutcomeTagName));
        Assert.Empty(this.published);
    }

    /// <summary>
    /// A write that reported nothing is one that threw. It is counted rather than left out, because a store that starts
    /// failing would otherwise show up as writes that stopped arriving, which reads as an idle deployment.
    /// </summary>
    [Fact]
    public void BeginWrite_AWriteThatReportedNothing_TimesItAsAFailureWithoutASize()
    {
        // Arrange
        var telemetry = TelemetryOver(out _);
        using var measurements = new RecordedMailFathomMeasurements(WriteBytesInstrument, WriteDurationInstrument);

        // Act
        using (telemetry.BeginWrite())
        {
        }

        // Assert
        Assert.Empty(measurements.ValuesOf(WriteBytesInstrument));
        Assert.Equal(["failed"], measurements.DimensionOf(WriteDurationInstrument, OutcomeTagName));
    }

    /// <summary>The outcome is the only dimension either family carries; a size is mail's shape and an identity is mail.</summary>
    [Fact]
    public void Begin_AnyReadOrWrite_PublishesNoDimensionBeyondTheOutcome()
    {
        // Arrange
        var telemetry = TelemetryOver(out _);
        using var measurements = new RecordedMailFathomMeasurements(
            ReadBytesInstrument,
            ReadDurationInstrument,
            WriteBytesInstrument,
            WriteDurationInstrument);

        // Act
        using (var read = telemetry.BeginRead())
        {
            read.Found(10);
        }

        using (var write = telemetry.BeginWrite())
        {
            write.Stored(10);
        }

        // Assert
        Assert.All(
            measurements.Recorded,
            measurement => Assert.All(
                measurement.Dimensions.Keys,
                dimension => Assert.Equal(OutcomeTagName, dimension)));
    }

    private static StoredEmailContentTelemetry TelemetryOver(out FakeTimeProvider timeProvider)
    {
        timeProvider = new FakeTimeProvider();

        return new StoredEmailContentTelemetry(timeProvider);
    }
}
