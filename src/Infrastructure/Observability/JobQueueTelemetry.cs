// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Execution;
using MailFathom.Application.Jobs.Scheduling;
using MailFathom.Common.Observability;

namespace MailFathom.Infrastructure.Observability;

/// <summary>Makes durable background work legible from outside the process: what ran, how long it took, and what is waiting.</summary>
/// <remarks>
/// <para>
/// Six instruments, and each answers a question the others cannot. The attempts and their durations say what the queue
/// is doing; the retries say how much of that is work being repeated, which is what separates an instance that is busy
/// from one that is failing and trying again; the dead letters say what has stopped, which is the only one of the four
/// that waits for a person. The depth beside them is the level the first three are a rate against — a backlog is
/// visible there while it is still small, and long before the effect of it reaches anything else. The two beside those
/// belong to recurring dispatch: what each schedule's occasion did, and how many occasions were deliberately not run,
/// which is the one thing a queue's own instruments could never show — an occasion that was skipped enqueued nothing and
/// would otherwise be indistinguishable from an interval nobody declared.
/// </para>
/// <para>
/// Dead letters carry their own instrument rather than being a dimension of the attempts, because the classification
/// that ended a job belongs on that measurement and on no other: a permanent failure is a defect or a declaration to
/// fix, and a transient one that exhausted its attempts is a dependency that stayed broken. Putting that dimension on
/// every attempt would make it a tag reading "none" on almost every series.
/// </para>
/// <para>
/// The depth is a gauge over the last figure the worker measured rather than a live count, for the reason the embedding
/// backlog's is: an exact live count is a query, and making it a gauge would put that query on whatever interval a
/// collector happened to be configured with. It is published per type and replaced whole, so a type whose queue emptied
/// reports zero rather than its last non-zero depth, and it saturates at the configured depth bound — which is exactly
/// where enqueuing is already being refused as backpressure.
/// </para>
/// <para>
/// <strong>Nothing published here is mail or derived from it.</strong> The dimensions are MailFathom's own closed sets —
/// a job type's name, an execution outcome, and a failure classification — and the values are counts and durations. The
/// job's payload, its idempotency key, its account, and the reason recorded against a failure all stay off every
/// measurement: a key names folders and occurrences and a reason is unbounded in the way a metric dimension may not be,
/// so both are read from the queue rather than from a dashboard.
/// </para>
/// </remarks>
public sealed class JobQueueTelemetry
{
    /// <summary>The name one attempt at a durable job opens its span under.</summary>
    /// <remarks>
    /// A job is dispatched by an interval rather than by a request, so without a span of its own everything an attempt
    /// does — its database commands above all — reaches a trace store parentless, competing with whatever else the
    /// process was doing at that moment. Named after what the attempt does rather than after the worker that dispatches
    /// it, so it stays right if a job is ever run from somewhere else.
    /// <para>
    /// Published rather than kept inside this assembly, because the span is opened around a boundary the composition
    /// root draws — one attempt, one scope — and what asserts that nesting therefore lives with the host rather than
    /// here. A second copy of the word would be a second place for it to drift.
    /// </para>
    /// </remarks>
    public const string AttemptSpanName = "run_job";

    internal const string JobTypeTagName = "mailfathom.job.type";
    internal const string OutcomeTagName = "mailfathom.job.outcome";
    internal const string FailureTagName = "mailfathom.job.failure";

    /// <summary>Which attempt at the job this was, counting from one.</summary>
    /// <remarks>
    /// The one thing a span carries that the instruments do not. An attempt's number says whether a trace is the first
    /// try or the fifth, which is what separates a slow job from a job that has been failing all day — and it is bounded
    /// by the retry policy rather than by anything a caller or a message decides.
    /// </remarks>
    internal const string AttemptNumberTagName = "mailfathom.job.attempt";

    private readonly ConcurrentDictionary<string, int> waitingByJobType = new(StringComparer.Ordinal);
    private readonly Counter<long> attemptCount;
    private readonly Histogram<double> attemptDuration;
    private readonly Counter<long> retryCount;
    private readonly Counter<long> deadLetterCount;
    private readonly Counter<long> scheduleDispatchCount;
    private readonly Counter<long> scheduleSkipCount;

    /// <summary>Initializes the instruments every job attempt and every queue measurement reports through.</summary>
    public JobQueueTelemetry()
    {
        this.attemptCount = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.jobs.attempts",
            unit: "{attempt}",
            description: "Attempts at durable background work, by job type and how each one ended.");
        this.attemptDuration = Telemetry.Meter.CreateHistogram<double>(
            "mailfathom.jobs.attempt.duration",
            unit: "s",
            description: "How long one attempt at a job took, from dispatch to the recorded outcome, by job type and outcome.");
        this.retryCount = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.jobs.retries",
            unit: "{retry}",
            description: "Failed attempts the queue scheduled again, by job type and the outcome that failed.");
        this.deadLetterCount = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.jobs.dead_letters",
            unit: "{job}",
            description: "Jobs nothing will attempt again, by job type, the outcome that ended them, and its classification.");
        this.scheduleDispatchCount = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.jobs.schedule.dispatches",
            unit: "{decision}",
            description: "Decisions a recurring dispatch reached about one schedule, by job type and what it did.");
        this.scheduleSkipCount = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.jobs.schedule.skipped_occurrences",
            unit: "{occurrence}",
            description: "Scheduled occasions that were deliberately not run, by job type and why the dispatch passed over them.");
        Telemetry.Meter.CreateObservableGauge(
            "mailfathom.jobs.queue.depth",
            this.ObserveWaitingCounts,
            unit: "{job}",
            description: "Jobs of each type waiting to be claimed, as the worker last measured it.");
    }

    /// <summary>Opens the span one attempt at a job is reported as, and returns the scope that ends it.</summary>
    /// <param name="jobType">The kind of work being attempted, which is the only thing about the job the span names.</param>
    /// <returns>The scope, which the caller must dispose; a scope disposed without <see cref="JobAttemptScope.Ended" /> reports an attempt that produced no result.</returns>
    /// <remarks>
    /// The span is opened around the attempt rather than around the pass that dispatched it, so a pass running several
    /// jobs at once produces one span each instead of one span covering all of them. Nothing about the job itself
    /// reaches it beyond the type: not the job's identifier, not the account, and above all not the idempotency key,
    /// which is composed of folder aliases and message occurrences.
    /// </remarks>
    public JobAttemptScope BeginAttempt(JobType jobType)
    {
        var activity = Telemetry.ActivitySource.StartActivity(AttemptSpanName);
        activity?.SetTag(JobTypeTagName, jobType.Name);

        return new JobAttemptScope(activity);
    }

    /// <summary>Records one attempt: that it happened, how long it took, and what became of the job.</summary>
    /// <param name="result">What the attempt did.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="result" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// An attempt the host released on shutdown and one whose lease had already moved on are both counted, because both
    /// occupied a worker and neither is a failure of the work. Which they were is the outcome dimension, so a rolling
    /// deployment shows up as released attempts rather than as an unexplained gap.
    /// </remarks>
    public void RecordAttempt(JobExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var tags = new TagList
        {
            { JobTypeTagName, result.JobType.Name },
            { OutcomeTagName, OutcomeTagOf(result.Outcome) },
        };

        this.attemptCount.Add(1, tags);
        this.attemptDuration.Record(result.Duration.TotalSeconds, tags);

        switch (result.AttemptFailure)
        {
            case { Disposition: JobFailureDisposition.RetryScheduled }:
                this.retryCount.Add(1, tags);

                break;

            case { Disposition: JobFailureDisposition.DeadLettered } attemptFailure:
                this.deadLetterCount.Add(
                    1,
                    new TagList
                    {
                        { JobTypeTagName, result.JobType.Name },
                        { OutcomeTagName, OutcomeTagOf(result.Outcome) },
                        { FailureTagName, FailureTagOf(attemptFailure.Record.Classification) },
                    });

                break;

            default:
                break;
        }
    }

    /// <summary>Records what a pass decided about one schedule, and how many occasions that decision passed over.</summary>
    /// <param name="dispatch">What the pass decided.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="dispatch" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The skipped occasions carry their own instrument rather than being a dimension of the decisions, because a
    /// decision is one event and the occasions it stepped over are a count that can be any number. Both are broken down
    /// by the outcome, which is what separates the two reasons an occasion is skipped: a process that was down over it,
    /// and a queue that was full or a previous run that was still going. Neither carries the schedule's own identity —
    /// it is composed of an account and a rule name, which is a dimension that grows with the configuration.
    /// </remarks>
    public void RecordScheduleDispatch(JobScheduleDispatch dispatch)
    {
        ArgumentNullException.ThrowIfNull(dispatch);

        var tags = new TagList
        {
            { JobTypeTagName, dispatch.JobType.Name },
            { OutcomeTagName, ScheduleTagOf(dispatch.Outcome) },
        };

        this.scheduleDispatchCount.Add(1, tags);

        if (dispatch.SkippedOccurrenceCount > 0)
        {
            this.scheduleSkipCount.Add(dispatch.SkippedOccurrenceCount, tags);
        }
    }

    /// <summary>Publishes what the worker last measured the queue's depth to be.</summary>
    /// <param name="readings">One reading per job type measured.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="readings" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Each type is replaced rather than accumulated, so a measurement is the depth at that moment. A type nothing has
    /// ever measured is absent from the gauge entirely, which is what keeps a flat zero meaning "measured and empty"
    /// rather than "never looked at".
    /// </remarks>
    public void RecordQueueDepth(IReadOnlyList<JobQueueDepthReading> readings)
    {
        ArgumentNullException.ThrowIfNull(readings);

        foreach (var reading in readings)
        {
            this.waitingByJobType[reading.JobType.Name] = reading.WaitingCount;
        }
    }

    /// <summary>Reads the published depths, materialized before the meter sees them.</summary>
    /// <remarks>
    /// A gauge callback is invoked on the collector's schedule, so a deferred query would be enumerated against
    /// whatever the dictionary held then rather than against what this call read.
    /// </remarks>
    private IEnumerable<Measurement<long>> ObserveWaitingCounts() =>
    [
        .. this.waitingByJobType.Select(waiting => new Measurement<long>(
            waiting.Value,
            new TagList { { JobTypeTagName, waiting.Key } })),
    ];

    private static string OutcomeTagOf(JobExecutionOutcome outcome) => outcome switch
    {
        JobExecutionOutcome.Succeeded => "succeeded",
        JobExecutionOutcome.HandlerFailed => "handler_failed",
        JobExecutionOutcome.HandlerMissing => "handler_missing",
        JobExecutionOutcome.TimedOut => "timed_out",
        JobExecutionOutcome.ReleasedForShutdown => "released_for_shutdown",
        JobExecutionOutcome.LeaseLost => "lease_lost",
        _ => "unknown",
    };

    private static string ScheduleTagOf(JobScheduleDispatchOutcome outcome) => outcome switch
    {
        JobScheduleDispatchOutcome.Seeded => "seeded",
        JobScheduleDispatchOutcome.NotDue => "not_due",
        JobScheduleDispatchOutcome.Dispatched => "dispatched",
        JobScheduleDispatchOutcome.AlreadyDispatched => "already_dispatched",
        JobScheduleDispatchOutcome.PreviousRunInFlight => "previous_run_in_flight",
        JobScheduleDispatchOutcome.RefusedAtCapacity => "refused_at_capacity",
        _ => "unknown",
    };

    private static string FailureTagOf(JobFailureClassification classification) => classification switch
    {
        JobFailureClassification.Transient => "transient",
        JobFailureClassification.Permanent => "permanent",
        _ => "unknown",
    };

    /// <summary>Carries one attempt at a job from the span that opens it to the result that ends it.</summary>
    /// <remarks>
    /// An attempt that reached no result is published with an error status and no outcome, because there is no word for
    /// it: every outcome this queue publishes is one the executor decided, and inventing one here would report a
    /// decision nothing made. That happens where the attempt could not be dispatched at all, which is a defect in the
    /// composition rather than a state of the work.
    /// </remarks>
    public sealed class JobAttemptScope : IDisposable
    {
        private readonly Activity? activity;

        private bool reported;

        internal JobAttemptScope(Activity? activity) => this.activity = activity;

        /// <summary>Records what the attempt turned out to have done.</summary>
        /// <param name="result">What the attempt did.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="result" /> is <see langword="null" />.</exception>
        public void Ended(JobExecutionResult result)
        {
            ArgumentNullException.ThrowIfNull(result);

            this.reported = true;

            this.activity?.SetTag(AttemptNumberTagName, result.AttemptCount);
            this.activity?.SetTag(OutcomeTagName, OutcomeTagOf(result.Outcome));
            this.activity?.SetStatus(
                result.Outcome is JobExecutionOutcome.Succeeded ? ActivityStatusCode.Ok : ActivityStatusCode.Error);

            if (result.AttemptFailure is { Disposition: JobFailureDisposition.DeadLettered } deadLettered)
            {
                this.activity?.SetTag(FailureTagName, FailureTagOf(deadLettered.Record.Classification));
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (!this.reported)
            {
                this.activity?.SetStatus(ActivityStatusCode.Error);
            }

            this.activity?.Dispose();
        }
    }
}
