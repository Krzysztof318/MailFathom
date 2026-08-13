// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Globalization;
using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Execution;
using MailFathom.Common.Observability;
using MailFathom.Infrastructure.Observability;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Observability;

/// <summary>Covers what durable background work publishes, and what it must never publish with it.</summary>
/// <remarks>
/// One telemetry instance serves the whole class, because it is a singleton in the process it belongs to and its
/// instruments are created on the application's one meter — an instance per test would leave a gauge per test observing
/// the meter for the rest of the run. The measurements are read back by instrument name and by the job type carried on
/// them, which is what keeps them apart from whatever another test class published to the same meter.
/// </remarks>
public sealed class JobQueueTelemetryTests
{
    private const string AttemptsInstrumentName = "mailfathom.jobs.attempts";
    private const string DurationInstrumentName = "mailfathom.jobs.attempt.duration";
    private const string RetriesInstrumentName = "mailfathom.jobs.retries";
    private const string DeadLettersInstrumentName = "mailfathom.jobs.dead_letters";
    private const string DepthInstrumentName = "mailfathom.jobs.queue.depth";
    private const string JobTypeTagName = "mailfathom.job.type";

    private static readonly JobQueueTelemetry QueueTelemetry = new();

    /// <summary>Every attempt is counted and timed, whatever became of the job, and both carry how it ended.</summary>
    [Fact]
    public void RecordAttempt_AnAttemptThatSucceeded_CountsItAndRecordsItsDuration()
    {
        // Arrange
        using var collector = new MeasurementCollector();

        // Act
        QueueTelemetry.RecordAttempt(Result(JobExecutionOutcome.Succeeded, TimeSpan.FromSeconds(3)));

        // Assert
        var attempt = Assert.Single(collector.Read(AttemptsInstrumentName));
        Assert.Equal(1, attempt.Value);
        Assert.Equal("succeeded", attempt.Tags["mailfathom.job.outcome"]);
        Assert.Equal(3, Assert.Single(collector.Read(DurationInstrumentName)).Value);
    }

    /// <summary>
    /// A retry is what separates an instance that is busy from one that is failing and trying again, so it is counted
    /// apart from the attempt that produced it rather than left to be derived.
    /// </summary>
    [Fact]
    public void RecordAttempt_AFailureTheQueueWillTryAgain_CountsARetryAndNoDeadLetter()
    {
        // Arrange
        using var collector = new MeasurementCollector();

        // Act
        QueueTelemetry.RecordAttempt(Result(
            JobExecutionOutcome.HandlerFailed,
            TimeSpan.FromSeconds(1),
            new JobAttemptFailure(
                JobFailureRecord.Create(JobFailureClassification.Transient, "TransportFailure"),
                JobFailureDisposition.RetryScheduled)));

        // Assert
        Assert.Equal(1, Assert.Single(collector.Read(RetriesInstrumentName)).Value);
        Assert.Empty(collector.Read(DeadLettersInstrumentName));
    }

    /// <summary>
    /// The classification belongs on the dead letter and on no other measurement: a permanent failure is a defect to
    /// fix and a transient one that ran out of attempts is a dependency that stayed broken, and an operator reading the
    /// one instrument that waits for them needs to be told which.
    /// </summary>
    [Fact]
    public void RecordAttempt_AJobNothingWillAttemptAgain_CountsItWithTheClassificationThatEndedIt()
    {
        // Arrange
        using var collector = new MeasurementCollector();

        // Act
        QueueTelemetry.RecordAttempt(Result(
            JobExecutionOutcome.HandlerFailed,
            TimeSpan.FromSeconds(2),
            new JobAttemptFailure(
                JobFailureRecord.Create(JobFailureClassification.Permanent, "PayloadUnreadable"),
                JobFailureDisposition.DeadLettered)));

        // Assert
        var deadLetter = Assert.Single(collector.Read(DeadLettersInstrumentName));
        Assert.Equal(1, deadLetter.Value);
        Assert.Equal("permanent", deadLetter.Tags["mailfathom.job.failure"]);
        Assert.Empty(collector.Read(RetriesInstrumentName));
    }

    /// <summary>
    /// A rolling deployment shows up as released attempts rather than as an unexplained gap, so an attempt the host
    /// gave back is counted like any other and is told apart by its outcome.
    /// </summary>
    [Fact]
    public void RecordAttempt_AnAttemptTheHostGaveBack_CountsItAsReleasedRatherThanAsAFailure()
    {
        // Arrange
        using var collector = new MeasurementCollector();

        // Act
        QueueTelemetry.RecordAttempt(Result(JobExecutionOutcome.ReleasedForShutdown, TimeSpan.FromSeconds(1)));

        // Assert
        Assert.Equal(
            "released_for_shutdown",
            Assert.Single(collector.Read(AttemptsInstrumentName)).Tags["mailfathom.job.outcome"]);
    }

    /// <summary>
    /// A metric dimension is where personal data would outlive the run that produced it and reach every exporter, so
    /// the job's identity, its idempotency key, its account, and the recorded reason stay off every measurement. The
    /// key is the one that would carry mail: it is composed of folder aliases and message occurrences.
    /// </summary>
    [Fact]
    public void RecordAttempt_AFailedAttempt_PublishesNothingBeyondTheThreeClosedDimensions()
    {
        // Arrange
        const string Reason = "PayloadUnreadable";
        string[] permittedTagKeys =
            ["mailfathom.job.type", "mailfathom.job.outcome", "mailfathom.job.failure"];
        var result = Result(
            JobExecutionOutcome.HandlerFailed,
            TimeSpan.FromSeconds(1),
            new JobAttemptFailure(
                JobFailureRecord.Create(JobFailureClassification.Permanent, Reason),
                JobFailureDisposition.DeadLettered));
        using var collector = new MeasurementCollector();

        // Act
        QueueTelemetry.RecordAttempt(result);

        // Assert
        var published = collector.Read(AttemptsInstrumentName, DurationInstrumentName, DeadLettersInstrumentName);
        Assert.NotEmpty(published);
        Assert.All(published, measurement => Assert.All(
            measurement.Tags,
            tag => Assert.Contains(tag.Key, permittedTagKeys)));
        Assert.All(published, measurement => Assert.DoesNotContain(
            measurement.Tags.Values,
            value => value is string text
                && (StringComparer.Ordinal.Equals(text, Reason)
                    || StringComparer.Ordinal.Equals(text, result.JobId.Value.ToString()))));
    }

    /// <summary>The depth is what a backlog becomes visible in first, and it is published per kind of work.</summary>
    [Fact]
    public void RecordQueueDepth_AMeasuredQueue_PublishesTheDepthOfEachTypeMeasured()
    {
        // Arrange
        using var collector = new MeasurementCollector();

        // Act
        QueueTelemetry.RecordQueueDepth([new JobQueueDepthReading(JobType.ClassifyEmailSpam, 41)]);

        // Assert
        var depth = Assert.Single(collector.ReadObservable(DepthInstrumentName));
        Assert.Equal(41, depth.Value);
        Assert.Equal(JobType.ClassifyEmailSpam.Name, depth.Tags[JobTypeTagName]);
    }

    /// <summary>
    /// A queue that emptied has to report zero rather than its last non-zero depth. A gauge that kept the earlier
    /// figure would say an instance is behind long after it caught up, which is the opposite of what it exists for.
    /// </summary>
    [Fact]
    public void RecordQueueDepth_AQueueThatDrained_ReplacesTheDepthRatherThanKeepingTheEarlierOne()
    {
        // Arrange
        using var collector = new MeasurementCollector();
        QueueTelemetry.RecordQueueDepth([new JobQueueDepthReading(JobType.ClassifyEmailSpam, 41)]);

        // Act
        QueueTelemetry.RecordQueueDepth([new JobQueueDepthReading(JobType.ClassifyEmailSpam, 0)]);

        // Assert
        Assert.Equal(0, Assert.Single(collector.ReadObservable(DepthInstrumentName)).Value);
    }

    private static JobExecutionResult Result(
        JobExecutionOutcome outcome,
        TimeSpan duration,
        JobAttemptFailure? attemptFailure = null) =>
        new(JobId.Create(Guid.CreateVersion7()), JobType.ClassifyEmailSpam, 1, outcome, duration)
        {
            AttemptFailure = attemptFailure,
        };

    /// <summary>Reads MailFathom's own meter, which is the only way an instrument published on it can be asserted on.</summary>
    private sealed class MeasurementCollector : IDisposable
    {
        private readonly MeterListener listener = new();

        // Concurrent because the listener is enabled for every instrument on MailFathom's one meter, so any other test
        // class publishing to it writes here while this one reads — which a plain list reports as a modified collection.
        private readonly ConcurrentQueue<PublishedMeasurement> measurements = [];

        internal MeasurementCollector()
        {
            this.listener.InstrumentPublished = (instrument, activeListener) =>
            {
                if (StringComparer.Ordinal.Equals(instrument.Meter.Name, Telemetry.Name))
                {
                    activeListener.EnableMeasurementEvents(instrument);
                }
            };
            this.listener.SetMeasurementEventCallback<long>(this.Record);
            this.listener.SetMeasurementEventCallback<double>(this.Record);
            this.listener.Start();
        }

        public void Dispose() => this.listener.Dispose();

        /// <summary>Returns what the named instruments published for this class's job type.</summary>
        internal IReadOnlyList<PublishedMeasurement> Read(params string[] instrumentNames) =>
        [
            .. this.measurements.ToArray().Where(measurement =>
                instrumentNames.Contains(measurement.InstrumentName, StringComparer.Ordinal)
                && Names(measurement, JobType.ClassifyEmailSpam)),
        ];

        /// <summary>Collects every gauge once and returns what one instrument published for this class's job type.</summary>
        internal IReadOnlyList<PublishedMeasurement> ReadObservable(string instrumentName)
        {
            this.measurements.Clear();
            this.listener.RecordObservableInstruments();

            return this.Read(instrumentName);
        }

        private static bool Names(PublishedMeasurement measurement, JobType jobType) =>
            measurement.Tags.TryGetValue(JobTypeTagName, out var published) && Equals(published, jobType.Name);

        private void Record<TMeasurement>(
            Instrument instrument,
            TMeasurement measurement,
            ReadOnlySpan<KeyValuePair<string, object?>> tags,
            object? state)
            where TMeasurement : struct =>
            this.measurements.Enqueue(new PublishedMeasurement(
                instrument.Name,
                Convert.ToDouble(measurement, CultureInfo.InvariantCulture),
                tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value, StringComparer.Ordinal)));
    }

    /// <summary>One measurement an instrument published, with the dimensions it carried.</summary>
    private sealed record PublishedMeasurement(
        string InstrumentName,
        double Value,
        IReadOnlyDictionary<string, object?> Tags);
}
