// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Execution;
using MailFathom.Application.Jobs.Scheduling;
using MailFathom.Infrastructure.Observability;
using MailFathom.TestSupport;
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
    private const string ScheduleDispatchesInstrumentName = "mailfathom.jobs.schedule.dispatches";
    private const string ScheduleSkipsInstrumentName = "mailfathom.jobs.schedule.skipped_occurrences";
    private const string JobTypeTagName = "mailfathom.job.type";

    private static readonly JobQueueTelemetry QueueTelemetry = new();

    /// <summary>Every attempt is counted and timed, whatever became of the job, and both carry how it ended.</summary>
    [Fact]
    public void RecordAttempt_AnAttemptThatSucceeded_CountsItAndRecordsItsDuration()
    {
        // Arrange
        using var measurements = new RecordedMailFathomMeasurements(AttemptsInstrumentName, DurationInstrumentName);

        // Act
        QueueTelemetry.RecordAttempt(Result(JobExecutionOutcome.Succeeded, TimeSpan.FromSeconds(3)));

        // Assert
        var attempt = Assert.Single(PublishedFor(measurements, JobType.ClassifyEmailSpam, AttemptsInstrumentName));
        Assert.Equal(1, attempt.Value);
        Assert.Equal("succeeded", attempt.Tags["mailfathom.job.outcome"]);
        Assert.Equal(
            3,
            Assert.Single(PublishedFor(measurements, JobType.ClassifyEmailSpam, DurationInstrumentName)).Value);
    }

    /// <summary>
    /// A retry is what separates an instance that is busy from one that is failing and trying again, so it is counted
    /// apart from the attempt that produced it rather than left to be derived.
    /// </summary>
    [Fact]
    public void RecordAttempt_AFailureTheQueueWillTryAgain_CountsARetryAndNoDeadLetter()
    {
        // Arrange
        using var measurements = new RecordedMailFathomMeasurements(
            RetriesInstrumentName,
            DeadLettersInstrumentName);

        // Act
        QueueTelemetry.RecordAttempt(Result(
            JobExecutionOutcome.HandlerFailed,
            TimeSpan.FromSeconds(1),
            new JobAttemptFailure(
                JobFailureRecord.Create(JobFailureClassification.Transient, "TransportFailure"),
                JobFailureDisposition.RetryScheduled)));

        // Assert
        Assert.Equal(
            1,
            Assert.Single(PublishedFor(measurements, JobType.ClassifyEmailSpam, RetriesInstrumentName)).Value);
        Assert.Empty(PublishedFor(measurements, JobType.ClassifyEmailSpam, DeadLettersInstrumentName));
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
        using var measurements = new RecordedMailFathomMeasurements(
            DeadLettersInstrumentName,
            RetriesInstrumentName);

        // Act
        QueueTelemetry.RecordAttempt(Result(
            JobExecutionOutcome.HandlerFailed,
            TimeSpan.FromSeconds(2),
            new JobAttemptFailure(
                JobFailureRecord.Create(JobFailureClassification.Permanent, "PayloadUnreadable"),
                JobFailureDisposition.DeadLettered)));

        // Assert
        var deadLetter = Assert.Single(PublishedFor(measurements, JobType.ClassifyEmailSpam, DeadLettersInstrumentName));
        Assert.Equal(1, deadLetter.Value);
        Assert.Equal("permanent", deadLetter.Tags["mailfathom.job.failure"]);
        Assert.Empty(PublishedFor(measurements, JobType.ClassifyEmailSpam, RetriesInstrumentName));
    }

    /// <summary>
    /// A rolling deployment shows up as released attempts rather than as an unexplained gap, so an attempt the host
    /// gave back is counted like any other and is told apart by its outcome.
    /// </summary>
    [Fact]
    public void RecordAttempt_AnAttemptTheHostGaveBack_CountsItAsReleasedRatherThanAsAFailure()
    {
        // Arrange
        using var measurements = new RecordedMailFathomMeasurements(AttemptsInstrumentName);

        // Act
        QueueTelemetry.RecordAttempt(Result(JobExecutionOutcome.ReleasedForShutdown, TimeSpan.FromSeconds(1)));

        // Assert
        Assert.Equal(
            "released_for_shutdown",
            Assert.Single(PublishedFor(measurements, JobType.ClassifyEmailSpam, AttemptsInstrumentName))
                .Tags["mailfathom.job.outcome"]);
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
        using var measurements = new RecordedMailFathomMeasurements(
            AttemptsInstrumentName,
            DurationInstrumentName,
            DeadLettersInstrumentName);

        // Act
        QueueTelemetry.RecordAttempt(result);

        // Assert
        var published = PublishedFor(
            measurements,
            JobType.ClassifyEmailSpam,
            AttemptsInstrumentName,
            DurationInstrumentName,
            DeadLettersInstrumentName);
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
        using var measurements = new RecordedMailFathomMeasurements(DepthInstrumentName);

        // Act
        QueueTelemetry.RecordQueueDepth([new JobQueueDepthReading(JobType.ClassifyEmailSpam, 41)]);

        // Assert
        measurements.ObserveGaugesAfresh();

        var depth = Assert.Single(PublishedFor(measurements, JobType.ClassifyEmailSpam, DepthInstrumentName));
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
        using var measurements = new RecordedMailFathomMeasurements(DepthInstrumentName);
        QueueTelemetry.RecordQueueDepth([new JobQueueDepthReading(JobType.ClassifyEmailSpam, 41)]);

        // Act
        QueueTelemetry.RecordQueueDepth([new JobQueueDepthReading(JobType.ClassifyEmailSpam, 0)]);

        // Assert
        measurements.ObserveGaugesAfresh();

        Assert.Equal(
            0,
            Assert.Single(PublishedFor(measurements, JobType.ClassifyEmailSpam, DepthInstrumentName)).Value);
    }

    /// <summary>An occasion that reached the queue is one decision, and it stepped over nothing.</summary>
    [Fact]
    public void RecordScheduleDispatch_AnOccasionThatWasDispatched_CountsTheDecisionAndNoSkippedOccasion()
    {
        // Arrange
        using var measurements = new RecordedMailFathomMeasurements(
            ScheduleDispatchesInstrumentName,
            ScheduleSkipsInstrumentName);

        // Act
        QueueTelemetry.RecordScheduleDispatch(Dispatch(JobScheduleDispatchOutcome.Dispatched));

        // Assert
        var dispatch = Assert.Single(
            PublishedFor(measurements, JobType.RunScheduledMailRules, ScheduleDispatchesInstrumentName));
        Assert.Equal(1, dispatch.Value);
        Assert.Equal("dispatched", dispatch.Tags["mailfathom.job.outcome"]);
        Assert.Empty(PublishedFor(measurements, JobType.RunScheduledMailRules, ScheduleSkipsInstrumentName));
    }

    /// <summary>An occasion that passed while nothing was running is skipped rather than replayed, and the skip is counted.</summary>
    [Theory]
    [InlineData(JobScheduleDispatchOutcome.Dispatched, "dispatched")]
    [InlineData(JobScheduleDispatchOutcome.PreviousRunInFlight, "previous_run_in_flight")]
    [InlineData(JobScheduleDispatchOutcome.RefusedAtCapacity, "refused_at_capacity")]
    public void RecordScheduleDispatch_OccasionsThatWereNotRun_CountsThemUnderWhyTheyWerePassedOver(
        JobScheduleDispatchOutcome outcome,
        string expectedTag)
    {
        // Arrange
        using var measurements = new RecordedMailFathomMeasurements(ScheduleSkipsInstrumentName);

        // Act
        QueueTelemetry.RecordScheduleDispatch(Dispatch(outcome, skippedOccurrenceCount: 4));

        // Assert
        var skipped = Assert.Single(
            PublishedFor(measurements, JobType.RunScheduledMailRules, ScheduleSkipsInstrumentName));
        Assert.Equal(4, skipped.Value);
        Assert.Equal(expectedTag, skipped.Tags["mailfathom.job.outcome"]);
    }

    /// <summary>A schedule's identity is an account and a rule name, so it never becomes a dimension of a metric.</summary>
    [Fact]
    public void RecordScheduleDispatch_AnyOccasion_PublishesNeitherTheScheduleNorTheInstantItNamed()
    {
        // Arrange
        using var measurements = new RecordedMailFathomMeasurements(
            ScheduleDispatchesInstrumentName,
            ScheduleSkipsInstrumentName);
        var dispatch = Dispatch(JobScheduleDispatchOutcome.Dispatched, skippedOccurrenceCount: 1);

        // Act
        QueueTelemetry.RecordScheduleDispatch(dispatch);

        // Assert
        var published = PublishedFor(
            measurements,
            JobType.RunScheduledMailRules,
            ScheduleDispatchesInstrumentName,
            ScheduleSkipsInstrumentName);
        Assert.All(
            published,
            measurement => Assert.Equal(
                ["mailfathom.job.type", "mailfathom.job.outcome"],
                measurement.Tags.Keys));
        Assert.All(
            published.SelectMany(measurement => measurement.Tags.Values),
            value => Assert.NotEqual(dispatch.Id.Value, value as string));
    }

    private static JobScheduleDispatch Dispatch(
        JobScheduleDispatchOutcome outcome,
        int skippedOccurrenceCount = 0) =>
        new(
            JobScheduleId.Create("mail-rules:work:housekeeping"),
            JobType.RunScheduledMailRules,
            outcome,
            new DateTimeOffset(2026, 8, 13, 3, 0, 0, TimeSpan.Zero),
            skippedOccurrenceCount);

    private static JobExecutionResult Result(
        JobExecutionOutcome outcome,
        TimeSpan duration,
        JobAttemptFailure? attemptFailure = null) =>
        new(JobId.Create(Guid.CreateVersion7()), JobType.ClassifyEmailSpam, 1, outcome, duration)
        {
            AttemptFailure = attemptFailure,
        };

    /// <summary>Selects what the named instruments published for one job type out of what the shared meter recorded.</summary>
    private static IReadOnlyList<RecordedMeasurement> PublishedFor(
        RecordedMailFathomMeasurements measurements,
        JobType jobType,
        params string[] instrumentNames) =>
        [
            .. instrumentNames
                .SelectMany(measurements.Read)
                .Where(measurement => Equals(measurement.Tags.GetValueOrDefault(JobTypeTagName), jobType.Name)),
        ];
}
