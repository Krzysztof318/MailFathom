// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Resilience;

namespace MailFathom.Application.Jobs.Execution;

/// <summary>Runs one leased job through its handler, holds the lease while it works, and records how it ended.</summary>
/// <remarks>
/// <para>
/// Three things can stop a handler and they are not the same event. Host shutdown gives the lease back, so the job is
/// claimable immediately and the deployment costs it nothing; the execution timeout records a failure, because the job
/// exceeded what it was allowed; and a lost lease writes nothing at all, because the row already belongs to the attempt
/// that replaced this one. Collapsing the three would make a rolling restart read as a burst of failures and would let
/// a late writer overwrite a newer attempt's outcome.
/// </para>
/// <para>
/// The lease is renewed while the handler works, so a job that legitimately takes longer than one lease is not reclaimed
/// underneath it. A renewal the store refuses is the signal that it already was: the attempt is cancelled at once, so
/// the handler stops rather than going on to produce a second execution's effects.
/// </para>
/// <para>
/// Every write recording an outcome is made outside the caller's cancellation, deliberately. The one moment a result
/// most needs to be durable is the shutdown that stopped the work, and a write cancelled by the same token as the
/// handler would leave the job held until its lease ran out on its own.
/// </para>
/// <para>
/// A failure is classified before the attempt budget is consulted, which is what keeps a permanent failure from
/// spending the budget several times over to reach the answer it had on its first attempt. Only a transient failure is
/// scheduled again, on a jittered delay; a permanent one, and a transient one that has run out of attempts, becomes a
/// dead letter that keeps the classification and the reason it ended on.
/// </para>
/// </remarks>
public sealed class JobExecutor
{
    private readonly IJobStore store;
    private readonly JobHandlerRegistry handlers;
    private readonly IJobFailureClassifier failureClassifier;
    private readonly JobExecutionSettings settings;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the executor from the queue it writes to and the bounds it runs under.</summary>
    /// <param name="store">Renews, completes, retries, dead-letters, and releases the lease this attempt holds.</param>
    /// <param name="handlers">Answers which handler runs the job's type.</param>
    /// <param name="failureClassifier">Decides whether a failure is worth attempting again, and names it safely.</param>
    /// <param name="settings">The execution timeout, the lease duration, the attempt bound, and the retry delays.</param>
    /// <param name="timeProvider">Times the attempt, the timeout, the renewal interval, and the next attempt's instant.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    public JobExecutor(
        IJobStore store,
        JobHandlerRegistry handlers,
        IJobFailureClassifier failureClassifier,
        JobExecutionSettings settings,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(handlers);
        ArgumentNullException.ThrowIfNull(failureClassifier);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.store = store;
        this.handlers = handlers;
        this.failureClassifier = failureClassifier;
        this.settings = settings;
        this.timeProvider = timeProvider;
    }

    /// <summary>Runs one job this process holds a lease on, and records the outcome against it.</summary>
    /// <param name="job">The job this attempt holds.</param>
    /// <param name="stoppingToken">Cancels the work because the host is stopping; the job is then released rather than failed.</param>
    /// <returns>What the attempt did, in terms that carry no mail content.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="job" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A job dispatched while the host is already stopping is released without being started, which is what a batch
    /// claimed just before a shutdown needs: the jobs behind the one that was running are handed back too, rather than
    /// waiting out a lease nobody is holding.
    /// </remarks>
    public async Task<JobExecutionResult> ExecuteAsync(LeasedJob job, CancellationToken stoppingToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        var startingTimestamp = this.timeProvider.GetTimestamp();

        if (stoppingToken.IsCancellationRequested)
        {
            return await this.ReleaseAsync(job, startingTimestamp);
        }

        if (!this.handlers.TryGetHandler(job.JobType, out var handler))
        {
            return await this.RecordFailureAsync(
                job,
                JobExecutionOutcome.HandlerMissing,
                JobFailureRecord.HandlerMissing,
                startingTimestamp);
        }

        return await this.RunHandlerAsync(job, handler, startingTimestamp, stoppingToken);
    }

    /// <summary>Runs the handler under the timeout and the renewal, and turns what happened into one outcome.</summary>
    /// <remarks>
    /// The order the outcomes are read in is the order of what they cost. Work that finished is completed whatever else
    /// happened, because the effect is already there. A shutdown is read next, so a job stopped as the host went down is
    /// released even where its timeout had already elapsed: releasing work that would have failed only runs it again,
    /// while failing work a deployment interrupted records the operator's act against the job.
    /// </remarks>
    [SuppressMessage(
        "Reliability",
        "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
        Justification = "The renewal is cancelled and awaited below, before any of the token sources it was handed leaves this scope.")]
    private async Task<JobExecutionResult> RunHandlerAsync(
        LeasedJob job,
        IJobHandler handler,
        long startingTimestamp,
        CancellationToken stoppingToken)
    {
        using var timeoutSource = new CancellationTokenSource(this.settings.ExecutionTimeout, this.timeProvider);
        using var leaseLostSource = new CancellationTokenSource();
        using var renewalSource = new CancellationTokenSource();
        using var attemptSource = CancellationTokenSource.CreateLinkedTokenSource(
            stoppingToken,
            timeoutSource.Token,
            leaseLostSource.Token);

        var renewal = this.RenewLeaseWhileRunningAsync(job, leaseLostSource, renewalSource.Token);

        var failure = await RunToCompletionAsync(handler, job.Payload, attemptSource.Token);

        await renewalSource.CancelAsync();
        await renewal;

        if (failure is null)
        {
            return await this.RecordCompletionAsync(job, startingTimestamp);
        }

        // Whatever a handler raised while the host was stopping, the job goes back to the queue. A handler that turns
        // its cancellation into an exception of its own would otherwise have its work recorded as failed because a
        // deployment happened, and releasing a job that really was failing only runs it again.
        if (stoppingToken.IsCancellationRequested)
        {
            return await this.ReleaseAsync(job, startingTimestamp);
        }

        if (failure is OperationCanceledException)
        {
            if (leaseLostSource.IsCancellationRequested)
            {
                // Nothing is written: the row is owned by the attempt that reclaimed it, and every write here is
                // conditional on the owner anyway, so asking would only be a refused statement.
                return this.Report(job, JobExecutionOutcome.LeaseLost, attemptFailure: null, startingTimestamp);
            }

            if (timeoutSource.IsCancellationRequested)
            {
                return await this.RecordFailureAsync(
                    job,
                    JobExecutionOutcome.TimedOut,
                    JobFailureRecord.ExecutionTimedOut,
                    startingTimestamp);
            }
        }

        // The exception is classified and then dropped. What travels on is the record it produced, because a handler
        // works on mail and everything downstream of here is a log line, a counter, or a span.
        return await this.RecordFailureAsync(
            job,
            JobExecutionOutcome.HandlerFailed,
            this.failureClassifier.Classify(failure),
            startingTimestamp);
    }

    /// <summary>Runs the handler and answers with whatever it raised, so one job's failure ends only that job.</summary>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The worker records whatever a handler raised against that job, so the jobs behind it in the batch still run.")]
    private static async Task<Exception?> RunToCompletionAsync(
        IJobHandler handler,
        IJobPayload payload,
        CancellationToken attemptToken)
    {
        try
        {
            await handler.RunAsync(payload, attemptToken);

            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    /// <summary>Pushes the lease further out while the handler works, and cancels the attempt when it cannot.</summary>
    /// <remarks>
    /// A renewal the store refuses means another attempt holds the job, so the work stops rather than going on to
    /// produce a second execution's effects. A renewal that fails for any other reason stops the renewing without
    /// stopping the work: the lease may well still hold, the timeout still bounds the attempt, and every write that
    /// ends it is conditional on the owner, so a lease that really was lost is reported as such when the outcome is
    /// recorded.
    /// </remarks>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A renewal that fails is not the attempt's failure, and must not replace the outcome the handler is about to produce.")]
    [SuppressMessage(
        "Roslynator",
        "RCS1075:Avoid empty catch clause that catches System.Exception",
        Justification = "There is no second action to take: the timeout still bounds the attempt, and the compare-and-set on the write that ends the job is what settles whether the lease was really lost.")]
    private async Task RenewLeaseWhileRunningAsync(
        LeasedJob job,
        CancellationTokenSource leaseLostSource,
        CancellationToken renewalToken)
    {
        try
        {
            while (!renewalToken.IsCancellationRequested)
            {
                await Task.Delay(this.settings.LeaseRenewalInterval, this.timeProvider, renewalToken);

                var renewedLease = await this.store.RenewLeaseAsync(
                    job.JobId,
                    job.Lease.Owner,
                    this.settings.LeaseDuration,
                    renewalToken);

                if (renewedLease is null)
                {
                    await leaseLostSource.CancelAsync();

                    return;
                }
            }
        }
        catch (Exception)
        {
            // Deliberately silent: the outcome the handler produces is what this attempt reports, and a lease that was
            // genuinely lost is caught by the compare-and-set on the write that ends the job.
        }
    }

    private async Task<JobExecutionResult> RecordCompletionAsync(LeasedJob job, long startingTimestamp)
    {
        var completed = await this.store.CompleteAsync(job.JobId, job.Lease.Owner, CancellationToken.None);

        return this.Report(
            job,
            completed ? JobExecutionOutcome.Succeeded : JobExecutionOutcome.LeaseLost,
            attemptFailure: null,
            startingTimestamp);
    }

    /// <summary>Decides between another attempt and a dead letter, and writes whichever it decided.</summary>
    /// <remarks>
    /// The classification is read before the attempt count, because the two answer different questions and only one of
    /// them can save an attempt: work that could never succeed stops now, however much budget is left. A job that has
    /// already been handed out as many times as it is allowed is terminal whatever the classification says, which is
    /// what bounds a dependency that stays broken.
    /// </remarks>
    private async Task<JobExecutionResult> RecordFailureAsync(
        LeasedJob job,
        JobExecutionOutcome outcome,
        JobFailureRecord failure,
        long startingTimestamp)
    {
        var attemptsRemain = job.AttemptCount < this.settings.MaxAttempts;

        if (failure.Classification is JobFailureClassification.Transient && attemptsRemain)
        {
            return await this.ScheduleRetryAsync(job, outcome, failure, startingTimestamp);
        }

        var deadLettered = await this.store.DeadLetterAsync(
            job.JobId,
            job.Lease.Owner,
            failure,
            CancellationToken.None);

        return deadLettered
            ? this.Report(
                job,
                outcome,
                new JobAttemptFailure(failure, JobFailureDisposition.DeadLettered),
                startingTimestamp)
            : this.Report(job, JobExecutionOutcome.LeaseLost, attemptFailure: null, startingTimestamp);
    }

    private async Task<JobExecutionResult> ScheduleRetryAsync(
        LeasedJob job,
        JobExecutionOutcome outcome,
        JobFailureRecord failure,
        long startingTimestamp)
    {
        var delay = JitteredRetryBackoff.DelayBeforeNextAttempt(
            this.settings.RetryBaseDelay,
            this.settings.RetryMaxDelay,
            minimumDelay: TimeSpan.Zero,
            job.AttemptCount);
        var availableAt = this.timeProvider.GetUtcNow() + delay;

        var scheduled = await this.store.ScheduleRetryAsync(
            job.JobId,
            job.Lease.Owner,
            failure,
            availableAt,
            CancellationToken.None);

        return scheduled
            ? this.Report(
                job,
                outcome,
                new JobAttemptFailure(failure, JobFailureDisposition.RetryScheduled)
                {
                    NextAttemptAt = availableAt,
                },
                startingTimestamp)
            : this.Report(job, JobExecutionOutcome.LeaseLost, attemptFailure: null, startingTimestamp);
    }

    private async Task<JobExecutionResult> ReleaseAsync(LeasedJob job, long startingTimestamp)
    {
        var released = await this.store.ReleaseAsync(job.JobId, job.Lease.Owner, CancellationToken.None);

        return this.Report(
            job,
            released ? JobExecutionOutcome.ReleasedForShutdown : JobExecutionOutcome.LeaseLost,
            attemptFailure: null,
            startingTimestamp);
    }

    private JobExecutionResult Report(
        LeasedJob job,
        JobExecutionOutcome outcome,
        JobAttemptFailure? attemptFailure,
        long startingTimestamp)
    {
        var duration = this.timeProvider.GetElapsedTime(startingTimestamp);

        return new JobExecutionResult(job.JobId, job.JobType, job.AttemptCount, outcome, duration)
        {
            AttemptFailure = attemptFailure,
        };
    }
}
