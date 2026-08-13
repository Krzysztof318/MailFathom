// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Extraction;
using MailFathom.Infrastructure.Observability;
using MailFathom.Infrastructure.UnitTests.TestDoubles;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Observability;

/// <summary>Covers what a bounded extraction pass publishes: what it moved, how long it took, and what is left.</summary>
public sealed class MailExtractionBackfillTelemetryTests
{
    private const string ExtractedInstrument = "mailfathom.mail.extraction.backfill.extracted";

    private const string UnreadableInstrument = "mailfathom.mail.extraction.backfill.unreadable";

    private const string MissingContentInstrument = "mailfathom.mail.extraction.backfill.missing_content";

    private const string RunDurationInstrument = "mailfathom.mail.extraction.backfill.run.duration";

    private const string OutstandingGauge = "mailfathom.mail.extraction.backfill.outstanding";

    private const string OutcomeTagName = "mailfathom.mail.extraction.backfill.outcome";

    /// <summary>The three counters are the throughput a rate is read from, so each carries what the pass reached.</summary>
    [Fact]
    public void RecordCompleted_APassThatMovedWork_CountsWhatItExtractedAndWhatItSteppedOver()
    {
        // Arrange
        var telemetry = new MailExtractionBackfillTelemetry();
        using var measurements = new RecordedMailFathomMeasurements(
            ExtractedInstrument,
            UnreadableInstrument,
            MissingContentInstrument);

        // Act
        telemetry.RecordCompleted(
            ResultWith(extracted: 40, unreadable: 2, missingContent: 3),
            TimeSpan.FromSeconds(4));

        // Assert
        Assert.Equal([40d], measurements.ValuesOf(ExtractedInstrument));
        Assert.Equal([2d], measurements.ValuesOf(UnreadableInstrument));
        Assert.Equal([3d], measurements.ValuesOf(MissingContentInstrument));
    }

    /// <summary>
    /// A pass that moved nothing adds nothing, because a stream of zeroes every interval would make an instance with
    /// nothing left to extract indistinguishable from one working through a mailbox.
    /// </summary>
    [Fact]
    public void RecordCompleted_APassThatMovedNothing_CountsNothingAtAll()
    {
        // Arrange
        var telemetry = new MailExtractionBackfillTelemetry();
        using var measurements = new RecordedMailFathomMeasurements(
            ExtractedInstrument,
            UnreadableInstrument,
            MissingContentInstrument);

        // Act
        telemetry.RecordCompleted(ResultWith(extracted: 0, unreadable: 0, missingContent: 0), TimeSpan.FromSeconds(1));

        // Assert
        Assert.Empty(measurements.Recorded);
    }

    /// <summary>Every ending is timed, and the outcome is what separates a deferred pass from one that failed.</summary>
    [Theory]
    [InlineData("succeeded")]
    [InlineData("deferred")]
    [InlineData("failed")]
    [InlineData("interrupted")]
    public void Record_EachWayAPassCanEnd_TimesItUnderItsOwnOutcome(string expectedOutcome)
    {
        // Arrange
        var telemetry = new MailExtractionBackfillTelemetry();
        using var measurements = new RecordedMailFathomMeasurements(RunDurationInstrument);
        var duration = TimeSpan.FromMilliseconds(2500);

        // Act
        switch (expectedOutcome)
        {
            case "succeeded":
                telemetry.RecordCompleted(ResultWith(extracted: 1, unreadable: 0, missingContent: 0), duration);
                break;
            case "deferred":
                telemetry.RecordDeferred(duration);
                break;
            case "failed":
                telemetry.RecordFailed(duration);
                break;
            default:
                telemetry.RecordInterrupted(duration);
                break;
        }

        // Assert
        Assert.Contains(expectedOutcome, measurements.DimensionOf(RunDurationInstrument, OutcomeTagName));
        Assert.Contains(2.5, measurements.ValuesOf(RunDurationInstrument));
    }

    /// <summary>
    /// The backlog answers whether a backfill will finish, and it is fed once per pass rather than measured when a
    /// collector asks, so it holds the last figure a pass established.
    /// </summary>
    [Fact]
    public void RecordCompleted_APassThatMeasuredTheBacklog_PublishesItUntilTheNextPassMeasuresAgain()
    {
        // Arrange
        var telemetry = new MailExtractionBackfillTelemetry();
        using var measurements = new RecordedMailFathomMeasurements(OutstandingGauge);

        // Act
        telemetry.RecordCompleted(ResultWith(extracted: 1, unreadable: 0, missingContent: 0, outstanding: 8_413), TimeSpan.FromSeconds(2));
        measurements.ObserveGauges();
        measurements.ObserveGauges();

        // Assert
        // Counted rather than compared as a sequence: every telemetry an earlier test built published a gauge of this
        // name that is still alive on the process-wide meter and still answers for its own pass, so one observation
        // records several numbers and only this one's is the figure under test.
        Assert.Equal(2, measurements.ValuesOf(OutstandingGauge).Count(outstanding => outstanding == 8_413));
    }

    /// <summary>A finished backfill reports zero rather than the last figure it was behind, or it reads as stalled.</summary>
    [Fact]
    public void RecordCompleted_APassThatFoundNothingLeft_PublishesAnEmptyBacklog()
    {
        // Arrange
        var telemetry = new MailExtractionBackfillTelemetry();

        // Act
        telemetry.RecordCompleted(ResultWith(extracted: 5, unreadable: 0, missingContent: 0, outstanding: 5), TimeSpan.FromSeconds(1));

        using var measurements = new RecordedMailFathomMeasurements(OutstandingGauge);
        telemetry.RecordCompleted(ResultWith(extracted: 0, unreadable: 0, missingContent: 0, outstanding: 0, emailsRemain: false), TimeSpan.FromSeconds(1));
        measurements.ObserveGauges();

        // Assert
        Assert.DoesNotContain(5d, measurements.ValuesOf(OutstandingGauge));
        Assert.Contains(0d, measurements.ValuesOf(OutstandingGauge));
    }

    /// <summary>Nothing about a message may dimension any of these, so the outcome is the only tag on any of them.</summary>
    [Fact]
    public void Record_AnyPass_PublishesNoDimensionBeyondTheOutcome()
    {
        // Arrange
        var telemetry = new MailExtractionBackfillTelemetry();
        using var measurements = new RecordedMailFathomMeasurements(
            ExtractedInstrument,
            UnreadableInstrument,
            MissingContentInstrument,
            RunDurationInstrument);

        // Act
        telemetry.RecordCompleted(ResultWith(extracted: 3, unreadable: 1, missingContent: 1), TimeSpan.FromSeconds(1));

        // Assert
        Assert.All(
            measurements.Recorded,
            measurement => Assert.All(
                measurement.Dimensions.Keys,
                dimension => Assert.Equal(OutcomeTagName, dimension)));
    }

    private static StoredEmailExtractionBackfillResult ResultWith(
        int extracted,
        int unreadable,
        int missingContent,
        int outstanding = 100,
        bool emailsRemain = true) =>
        new(extracted, unreadable, missingContent, outstanding, emailsRemain);
}
