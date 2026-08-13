// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
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
/// An instance with no registered handler stops the loop before it starts. Nothing here can run any of the declared job
/// types until a consumer registers a handler for one, and a worker that claimed under those conditions would be taking
/// work it would have to hand straight back.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed partial class JobWorker : BackgroundService
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly JobWorkerOptions settings;
    private readonly ILogger<JobWorker> logger;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes a new job worker.</summary>
    public JobWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<JobWorkerOptions> settings,
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
    /// A pass that ran nothing says nothing at all, because an idle queue is the ordinary state of an instance and a
    /// line per poll would be the whole log. What needs an operator is a job that could not be run and a job that ran
    /// out of time; a job whose handler failed is reported by the handler's own exception beside it.
    /// </remarks>
    private void Report(IReadOnlyList<JobExecutionResult> results)
    {
        foreach (var result in results)
        {
            switch (result.Outcome)
            {
                case JobExecutionOutcome.Succeeded:
                    this.LogJobSucceeded(result.JobType.Name, result.AttemptCount, result.Duration);

                    break;

                case JobExecutionOutcome.HandlerFailed:
                    this.LogJobFailed(result.JobType.Name, result.AttemptCount, result.Failure);

                    break;

                case JobExecutionOutcome.HandlerMissing:
                    this.LogHandlerMissing(result.JobType.Name);

                    break;

                case JobExecutionOutcome.TimedOut:
                    this.LogJobTimedOut(result.JobType.Name, result.AttemptCount, result.Duration);

                    break;

                case JobExecutionOutcome.ReleasedForShutdown:
                    this.LogJobReleasedForShutdown(result.JobType.Name);

                    break;

                case JobExecutionOutcome.LeaseLost:
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

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "A {JobType} job failed on attempt {AttemptCount} and is recorded as failed; nothing will attempt it again.")]
    private partial void LogJobFailed(string jobType, int attemptCount, Exception? exception);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "A {JobType} job was claimed and no handler is registered for it, so it is recorded as failed rather than claimed again. A claim is filtered to the types this build runs, so reaching this means a handler was registered and then withdrawn while the process was running.")]
    private partial void LogHandlerMissing(string jobType);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "A {JobType} job was cancelled on attempt {AttemptCount} after running for {Duration}, which is the configured Jobs:ExecutionTimeout, and is recorded as failed. Raise the timeout where this kind of work legitimately takes longer.")]
    private partial void LogJobTimedOut(string jobType, int attemptCount, TimeSpan duration);

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
