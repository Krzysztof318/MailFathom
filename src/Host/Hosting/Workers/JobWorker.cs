// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Execution;
using MailFathom.Application.Persistence;
using MailFathom.Host.Configuration.Jobs;
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
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed partial class JobWorker : BackgroundService
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly JobQueueOptions settings;
    private readonly ILogger<JobWorker> logger;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes a new job worker.</summary>
    public JobWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<JobQueueOptions> settings,
        ILogger<JobWorker> logger,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(settings);

        this.scopeFactory = scopeFactory;
        this.settings = settings.Value;
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

        var handledTypeNames = this.ReadHandledTypeNames();

        if (handledTypeNames.Length == 0)
        {
            this.LogNoHandlerRegistered();

            return;
        }

        var handledJobTypes = string.Join(", ", handledTypeNames);

        this.LogWorkerStarted(handledJobTypes);

        while (!stoppingToken.IsCancellationRequested)
        {
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
    private string[] ReadHandledTypeNames()
    {
        using var scope = this.scopeFactory.CreateScope();

        var handlers = scope.ServiceProvider.GetRequiredService<JobHandlerRegistry>();

        return [.. handlers.HandledTypes.Select(handledType => handledType.Name)];
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
}
