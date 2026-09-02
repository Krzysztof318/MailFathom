// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Execution;
using MailFathom.Application.Jobs.Scheduling;
using MailFathom.Application.Persistence;
using MailFathom.Host.Configuration.Jobs;
using MailFathom.Infrastructure.Observability;
using Microsoft.Extensions.Options;

namespace MailFathom.Host.Hosting.Workers;

/// <summary>Keeps taking passes over the queue of durable background work, one scoped pass at a time.</summary>
/// <remarks>
/// <para>
/// The worker owns the pacing and nothing else. What a pass does — which types it may claim, how long it holds them,
/// how one job is stopped and what is recorded about it — belongs to the application, so a claim that finds nothing and
/// a job that fails reach this loop as the same thing: a pass that ended.
/// </para>
/// <para>
/// A pass that filled its batch is followed by another at once, because a queue with work in it is drained rather than
/// polled; only a pass that came back short waits the configured interval. That is what keeps an idle instance quiet
/// without making enqueued work wait for a schedule.
/// </para>
/// <para>
/// One pass at a time, and the concurrency inside it is the application's. A second loop claiming beside this one would
/// be a second unstated bound on how much work is in flight, where the configured ceilings are the stated one.
/// </para>
/// <para>
/// An instance with no registered handler stops the loop before it starts. Nothing here can run any of the declared job
/// types until a consumer registers a handler for one, and a worker that claimed under those conditions would be taking
/// work it would have to hand straight back.
/// </para>
/// <para>
/// What each pass did is reported twice, to the log and to the queue's instruments, because the two are read at
/// different distances: a line names one job and an instrument names a rate. The queue's depth is measured here rather
/// than published from the store, because a level is only worth a database read on a schedule somebody chose — this one
/// measures at most once per poll interval, so a busy instance draining its queue back to back pays for it no more
/// often than an idle one.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed partial class JobWorker : BackgroundService
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly JobQueueOptions settings;
    private readonly JobQueueTelemetry telemetry;
    private readonly ILogger<JobWorker> logger;
    private readonly TimeProvider timeProvider;

    private long lastDepthMeasurementTimestamp;
    private bool hasMeasuredDepth;

    /// <summary>Initializes a new job worker.</summary>
    public JobWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<JobQueueOptions> settings,
        JobQueueTelemetry telemetry,
        ILogger<JobWorker> logger,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(telemetry);

        this.scopeFactory = scopeFactory;
        this.settings = settings.Value;
        this.telemetry = telemetry;
        this.logger = logger;
        this.timeProvider = timeProvider;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!this.settings.Enabled)
        {
            this.LogWorkerDisabled();

            return;
        }

        var handledTypes = this.ReadHandledTypes();

        if (handledTypes.Length == 0)
        {
            this.LogNoHandlerRegistered();

            return;
        }

        var handledJobTypes = string.Join(", ", handledTypes.Select(handledType => handledType.Name));

        this.LogWorkerStarted(handledJobTypes);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (this.IsPeriodicWorkDue())
            {
                await this.DispatchSchedulesAsync(stoppingToken);
                await this.MeasureQueueDepthAsync(handledTypes, stoppingToken);
            }

            var results = await this.RunPassAsync(stoppingToken);

            this.Report(results);

            if (results.Count >= this.settings.BatchSize)
            {
                continue;
            }

            await this.PauseAsync(stoppingToken);
        }
    }

    /// <summary>Reads which job types this build can run, which decides whether the loop starts at all.</summary>
    /// <remarks>Read once rather than per pass: handlers are registered by the composition root, so the answer cannot change while the process runs.</remarks>
    private JobType[] ReadHandledTypes()
    {
        using var scope = this.scopeFactory.CreateScope();

        var handlers = scope.ServiceProvider.GetRequiredService<JobHandlerRegistry>();

        return [.. handlers.HandledTypes];
    }

    /// <summary>Measures how much is waiting, no more often than one poll interval, and publishes it.</summary>
    /// <remarks>
    /// <para>
    /// The interval is what keeps the measurement a level rather than a cost. A pass that filled its batch runs the
    /// next one at once, so an instance working through a backlog takes passes as fast as the database serves them, and
    /// a reading per pass would put a bounded count on that same cadence for a number that only has to be roughly
    /// current.
    /// </para>
    /// <para>
    /// A failed measurement is swallowed and the depth is left where it was. Nothing about the queue depends on it —
    /// the pass that follows claims whether or not it was measured — and a worker that stopped because a gauge could
    /// not be read would trade the work for the reporting of it.
    /// </para>
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A measurement that failed must not stop the pass that follows it, and the last published depth stays until the next successful one.")]
    private async Task MeasureQueueDepthAsync(IReadOnlyList<JobType> handledTypes, CancellationToken stoppingToken)
    {
        try
        {
            using var scope = this.scopeFactory.CreateScope();

            var depths = scope.ServiceProvider.GetRequiredService<IJobQueueDepthReader>();

            this.telemetry.RecordQueueDepth(await depths.ReadWaitingDepthsAsync(handledTypes, stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The loop's own condition ends it; a stopping host is not a failure of the measurement.
        }
        catch (Exception exception)
        {
            this.LogQueueDepthMeasurementFailed(exception);
        }
    }

    /// <summary>Dispatches every schedule whose occasion has passed, isolating a failure from the claim that follows it.</summary>
    /// <remarks>
    /// <para>
    /// Recurring work reaches the queue here rather than through a loop of its own, which is what makes a schedule an
    /// occasion on the existing worker instead of a second scheduler: the jobs it writes are claimed by the very next
    /// pass, under the same concurrency ceiling, the same depth bound, and the same retry and dead-letter path.
    /// </para>
    /// <para>
    /// A failure is swallowed for the reason a failed depth measurement is. Nothing about the queue depends on it, the
    /// schedules are read again on the next interval, and an occasion missed because the database was briefly away is a
    /// skipped occasion — which is the answer this mechanism gives a missed occasion anyway.
    /// </para>
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A dispatch that failed must not stop the claim that follows it; the schedules are read again on the next interval.")]
    private async Task DispatchSchedulesAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = this.scopeFactory.CreateScope();

            var pass = scope.ServiceProvider.GetRequiredService<JobSchedulePass>();

            foreach (var dispatch in await pass.RunAsync(stoppingToken))
            {
                this.telemetry.RecordScheduleDispatch(dispatch);
                this.ReportSchedule(dispatch);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The loop's own condition ends it; a stopping host is not a failure of the dispatch.
        }
        catch (Exception exception)
        {
            this.LogScheduleDispatchFailed(exception);
        }
    }

    /// <summary>Says what a schedule's occasion did, at the level each outcome deserves.</summary>
    /// <remarks>
    /// A schedule with nothing due says nothing at all, because that is the ordinary state of every schedule between its
    /// occasions and a line per schedule per interval would be the whole log. What is reported is the occasion that ran
    /// and, separately, the occasions that did not — the second at a level an operator sees, because a schedule quietly
    /// not running is the failure this whole mechanism exists to make visible.
    /// </remarks>
    private void ReportSchedule(JobScheduleDispatch dispatch)
    {
        if (dispatch is { SkippedOccurrenceCount: > 0 })
        {
            this.LogScheduleOccurrencesSkipped(
                dispatch.Id.Value,
                dispatch.SkippedOccurrenceCount,
                dispatch.Outcome,
                dispatch.OccurrenceAt);
        }

        switch (dispatch.Outcome)
        {
            case JobScheduleDispatchOutcome.Seeded:
                this.LogScheduleSeeded(dispatch.Id.Value);

                break;

            case JobScheduleDispatchOutcome.Dispatched or JobScheduleDispatchOutcome.AlreadyDispatched:
                this.LogScheduleDispatched(dispatch.Id.Value, dispatch.JobType.Name, dispatch.OccurrenceAt);

                break;

            default:
                break;
        }
    }

    /// <summary>Reports whether the poll interval has elapsed since the last time the periodic steps ran, and stamps it when it has.</summary>
    /// <remarks>
    /// The first pass always takes them, so an instance that starts with a backlog publishes it before it begins
    /// draining rather than one interval later, and a schedule whose occasion passed while the process was down is
    /// decided about at once. Both steps share one interval because both are levels rather than work: a pass that filled
    /// its batch takes the next one immediately, and neither a gauge nor a schedule is worth a database read on that
    /// cadence.
    /// </remarks>
    private bool IsPeriodicWorkDue()
    {
        var now = this.timeProvider.GetTimestamp();

        if (this.hasMeasuredDepth
            && this.timeProvider.GetElapsedTime(this.lastDepthMeasurementTimestamp, now) < this.settings.PollInterval)
        {
            return false;
        }

        this.lastDepthMeasurementTimestamp = now;
        this.hasMeasuredDepth = true;

        return true;
    }

    /// <summary>Runs one scoped pass, isolating whatever goes wrong from the passes after it.</summary>
    /// <remarks>
    /// A failed pass keeps the worker alive on purpose. What can fail here is the claim itself — a job's own failure is
    /// already recorded against its row by the pass — and a database that is briefly unavailable says nothing about
    /// whether there is work to do. Anything a pass had claimed keeps its lease and is claimable again when that lease
    /// expires, so nothing is lost by waiting an interval and asking again.
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The hosted worker isolates an unexpected failure so a later pass can claim the work again.")]
    private async Task<IReadOnlyList<JobExecutionResult>> RunPassAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = this.scopeFactory.CreateScope();

            var pass = scope.ServiceProvider.GetRequiredService<JobQueuePass>();

            return await pass.RunAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return [];
        }
        catch (PersistenceConcurrencyConflictException exception)
        {
            this.LogPassDeferredAfterConcurrencyConflict(exception);

            return [];
        }
        catch (Exception exception)
        {
            this.LogPassFailed(exception);

            return [];
        }
    }

    /// <summary>Waits out the poll interval, and returns rather than throwing when the host stops during it.</summary>
    private async Task PauseAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(this.settings.PollInterval, this.timeProvider, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // The loop's own condition ends it; a stopping host is not a failure of the worker.
        }
    }

    /// <summary>Says what a pass did, at the level each outcome deserves.</summary>
    /// <remarks>
    /// <para>
    /// A pass that ran nothing says nothing at all, because an idle queue is the ordinary state of an instance and a
    /// line per poll would be the whole log.
    /// </para>
    /// <para>
    /// A failed attempt is reported by what became of the job rather than by what stopped it, because that is what an
    /// operator waiting on the work needs: a scheduled retry is a job still on its way and a dead letter is one that
    /// needs somebody. The outcome and the recorded reason are properties of the line either way, so nothing is lost by
    /// reading the disposition first.
    /// </para>
    /// </remarks>
    private void Report(IReadOnlyList<JobExecutionResult> results)
    {
        foreach (var result in results)
        {
            this.telemetry.RecordAttempt(result);

            switch (result)
            {
                case { AttemptFailure: { Disposition: JobFailureDisposition.RetryScheduled } attemptFailure }:
                    this.LogRetryScheduled(
                        result.JobType.Name,
                        result.AttemptCount,
                        result.Outcome,
                        attemptFailure.Record.Reason,
                        attemptFailure.NextAttemptAt);

                    break;

                case { AttemptFailure: { Disposition: JobFailureDisposition.DeadLettered } attemptFailure }:
                    this.LogJobDeadLettered(
                        result.JobType.Name,
                        result.AttemptCount,
                        result.Outcome,
                        attemptFailure.Record.Classification,
                        attemptFailure.Record.Reason);

                    break;

                case { Outcome: JobExecutionOutcome.Succeeded }:
                    this.LogJobSucceeded(result.JobType.Name, result.AttemptCount, result.Duration);

                    break;

                case { Outcome: JobExecutionOutcome.ReleasedForShutdown }:
                    this.LogJobReleasedForShutdown(result.JobType.Name);

                    break;

                case { Outcome: JobExecutionOutcome.LeaseLost }:
                    this.LogLeaseLost(result.JobType.Name, result.AttemptCount);

                    break;

                default:
                    break;
            }
        }
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The durable job worker is switched off, so nothing runs enqueued background work on this instance.")]
    private partial void LogWorkerDisabled();

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "No job handler is registered, so the durable job worker claims nothing. Work of a type this build cannot run is left for a deployment that can.")]
    private partial void LogNoHandlerRegistered();

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The durable job worker is running work of these types: {HandledJobTypes}.")]
    private partial void LogWorkerStarted(string handledJobTypes);

    /// <summary>Reports one job by its type, its attempt, and its duration; a payload names mail and never reaches a log.</summary>
    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Ran a {JobType} job on attempt {AttemptCount} in {Duration}.")]
    private partial void LogJobSucceeded(string jobType, int attemptCount, TimeSpan duration);

    /// <summary>
    /// Reports an attempt the queue will make again. The failure is named by the record rather than by the exception
    /// that produced it, because a handler works on mail and a library's message may quote it; a handler that wants its
    /// own failure diagnosed in full is the one place that knows what in it is safe to write.
    /// </summary>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "A {JobType} job ended attempt {AttemptCount} as {Outcome} with a transient failure recorded as {FailureReason}, and is claimable again at {NextAttemptAt}.")]
    private partial void LogRetryScheduled(
        string jobType,
        int attemptCount,
        JobExecutionOutcome outcome,
        string failureReason,
        DateTimeOffset? nextAttemptAt);

    /// <summary>Reports a job nothing will attempt again, which is the one job state that waits for a person.</summary>
    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "A {JobType} job ended attempt {AttemptCount} as {Outcome} and is dead-lettered: the failure is {FailureClassification}, recorded as {FailureReason}. Nothing claims it again, and it holds up no other job. An outcome of HandlerMissing means a handler was withdrawn while the process ran; TimedOut means the work outlasted Jobs:ExecutionTimeout, which is worth raising where this kind of work legitimately takes longer.")]
    private partial void LogJobDeadLettered(
        string jobType,
        int attemptCount,
        JobExecutionOutcome outcome,
        JobFailureClassification failureClassification,
        string failureReason);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "A {JobType} job was given back unfinished because the host is stopping; it is claimable again immediately and no attempt is held against it.")]
    private partial void LogJobReleasedForShutdown(string jobType);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "A {JobType} job's lease had moved to another attempt by the time attempt {AttemptCount} finished, so nothing was recorded for it. The attempt that holds it now is the one whose outcome counts.")]
    private partial void LogLeaseLost(string jobType, int attemptCount);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "A durable job pass was deferred after an unresolved optimistic concurrency conflict; the next pass claims again.")]
    private partial void LogPassDeferredAfterConcurrencyConflict(Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "A durable job pass failed; anything it was holding stays leased until the lease expires and the next pass claims again.")]
    private partial void LogPassFailed(Exception exception);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "The depth of the durable job queue could not be measured, so the gauge keeps the last figure it was given. Nothing about running the work depends on it.")]
    private partial void LogQueueDepthMeasurementFailed(Exception exception);

    /// <summary>Reports a schedule seen for the first time, which dispatches nothing because a schedule is a when rather than a debt.</summary>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Schedule {ScheduleId} was seen for the first time, so its occasions count from now and the one that had already passed is not owed.")]
    private partial void LogScheduleSeeded(string scheduleId);

    /// <summary>Reports an occasion that reached the queue; the schedule's identity is MailFathom's own names and no mail.</summary>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Schedule {ScheduleId} enqueued a {JobType} job for the occasion at {OccurrenceAt}.")]
    private partial void LogScheduleDispatched(string scheduleId, string jobType, DateTimeOffset? occurrenceAt);

    /// <summary>Reports occasions that were deliberately not run, which is the one thing a queue's own instruments cannot show.</summary>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Schedule {ScheduleId} passed over {SkippedOccurrenceCount} occasion(s) and resolved as {Outcome} at the occasion of {OccurrenceAt}. Occasions that passed while this instance was down, while the queue was full, or while the previous run was still going are skipped rather than replayed.")]
    private partial void LogScheduleOccurrencesSkipped(
        string scheduleId,
        int skippedOccurrenceCount,
        JobScheduleDispatchOutcome outcome,
        DateTimeOffset? occurrenceAt);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Recurring dispatch failed, so no schedule advanced on this interval; the schedules are read again on the next one and a missed occasion is skipped rather than replayed.")]
    private partial void LogScheduleDispatchFailed(Exception exception);
}
