// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Embeddings.Backfill;
using MailFathom.Application.Persistence;
using MailFathom.Host.Configuration.Embeddings;
using MailFathom.Infrastructure.Observability;
using Microsoft.Extensions.Options;

namespace MailFathom.Host.Hosting.Workers;

/// <summary>Runs the embedding backfill in scoped work units, pacing itself by what the last run found.</summary>
/// <remarks>
/// <para>
/// Unlike the extraction backfill, this worker never ends itself. Its walk is a repeating sweep, because a message the
/// live backlog's bound turned away and a message a refused provider call left part-way through both keep passages with
/// no vector after the position has stepped past them, and a worker that stopped at the end of one pass would be
/// promising to reach them and not doing it.
/// </para>
/// <para>
/// What it does instead is wait longer when nothing would be gained by asking again soon. A run that still has messages
/// in front of it is followed by the short interval, and one that reached the end of the stored mail — or that an
/// operator has to settle before anything can be embedded at all — by the long one.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed partial class MailEmbeddingBackfillWorker : BackgroundService
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly EmailEmbeddingBackfillTelemetry telemetry;
    private readonly EmbeddingBackfillOptions settings;
    private readonly ILogger<MailEmbeddingBackfillWorker> logger;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes a new embedding backfill worker.</summary>
    public MailEmbeddingBackfillWorker(
        IServiceScopeFactory scopeFactory,
        EmailEmbeddingBackfillTelemetry telemetry,
        IOptions<EmbeddingBackfillOptions> settings,
        ILogger<MailEmbeddingBackfillWorker> logger,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(settings);

        this.scopeFactory = scopeFactory;
        this.telemetry = telemetry;
        this.settings = settings.Value;
        this.logger = logger;
        this.timeProvider = timeProvider;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!this.settings.Enabled)
        {
            this.LogBackfillDisabled();

            return;
        }

        this.LogWorkerStarted();

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = await this.RunOnceAsync(stoppingToken);

            await Task.Delay(delay, this.timeProvider, stoppingToken);
        }
    }

    /// <summary>Runs one bounded pass and reports how long to wait before the next one.</summary>
    /// <remarks>
    /// A failed run keeps the worker alive on purpose, and waits the short interval rather than the long one. The
    /// database being briefly unavailable, or a competing writer winning a race, says nothing about whether messages
    /// still await embedding, and the committed position means the next run resumes rather than restarts.
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The hosted worker isolates an unexpected failure so a later interval can resume from the committed position.")]
    private async Task<TimeSpan> RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = this.scopeFactory.CreateScope();

            var backfill = scope.ServiceProvider.GetRequiredService<StoredEmailEmbeddingBackfill>();
            var result = await backfill.RunAsync(cancellationToken);

            this.telemetry.RecordRun(result);
            this.Report(result);

            return result.MoreWorkIsWorthTryingSoon ? this.settings.Interval : this.settings.IdleSweepInterval;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (PersistenceConcurrencyConflictException exception)
        {
            this.LogBackfillDeferredAfterConcurrencyConflict(exception);

            return this.settings.Interval;
        }
        catch (Exception exception)
        {
            this.LogBackfillFailed(exception);

            return this.settings.Interval;
        }
    }

    /// <summary>Says what the run means for an operator, at the level that outcome deserves.</summary>
    /// <remarks>
    /// Progress and a completed sweep are ordinary, and an instance that has activated no profile is the state ADR 0006
    /// makes supported, so none of the three is a warning. The two that need an operator are a declaration disagreeing
    /// with what was activated and a provider that refused.
    /// </remarks>
    private void Report(StoredEmailEmbeddingBackfillResult result)
    {
        switch (result.Outcome)
        {
            case StoredEmailEmbeddingBackfillOutcome.SweepCompleted:
                this.LogSweepCompleted();

                break;

            case StoredEmailEmbeddingBackfillOutcome.NoActiveProfile:
                this.LogNoActiveProfile();

                break;

            case StoredEmailEmbeddingBackfillOutcome.GeneratorDisagreesWithProfile:
                this.LogGeneratorDisagreesWithProfile();

                break;

            // The classification is matched rather than defaulted, because inventing one would report a failure the
            // provider never gave. A ProviderFailed result without it is unconstructible, so the case does not arise.
            case StoredEmailEmbeddingBackfillOutcome.ProviderFailed when result.Failure is { } failure:
                this.LogProviderFailed(failure, result.EmbeddedChunkCount);

                break;

            default:
                break;
        }

        if (result.OutstandingEmailCountAtSweepStart is { } outstanding
            && result.Outcome is not StoredEmailEmbeddingBackfillOutcome.NoActiveProfile)
        {
            this.LogSweepStarted(outstanding);
        }

        // Reported beside the run's ending rather than as one, because a message needing more calls than a turn allows
        // says something about that message's length and stops nothing. It still needs an operator: the walk steps past
        // it, so a mailbox where several sweeps go by before one message is finished is otherwise indistinguishable from
        // one that is finishing them.
        if (result.CallBudgetExhaustedEmailCount > 0)
        {
            this.LogCallBudgetExhausted(result.CallBudgetExhaustedEmailCount);
        }

        // Every one of the three is asked about, because a run can move the third alone: a message that spends its whole
        // call budget mid-way is neither cut nor brought up to date, and the hundreds of vectors it did write would go
        // unreported by a line that only counted whole messages — while the meter recorded them either way.
        if (result.ChunkedEmailCount > 0 || result.EmbeddedEmailCount > 0 || result.EmbeddedChunkCount > 0)
        {
            this.LogBackfillProgressed(
                result.ChunkedEmailCount,
                result.EmbeddedEmailCount,
                result.EmbeddedChunkCount);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The embedding backfill is disabled, so mail stored before the active profile stays unembedded until it is turned on.")]
    private partial void LogBackfillDisabled();

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The embedding backfill is walking the mail this instance already had.")]
    private partial void LogWorkerStarted();

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "An embedding backfill sweep began with {OutstandingEmailCount} messages awaiting embedding.")]
    private partial void LogSweepStarted(int outstandingEmailCount);

    /// <summary>Reports one run in counts only; no subject, address, passage, or vector may reach a log.</summary>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Embedding backfill run finished; cut {ChunkedEmailCount} messages into passages and gave {EmbeddedEmailCount} messages {EmbeddedChunkCount} vectors.")]
    private partial void LogBackfillProgressed(int chunkedEmailCount, int embeddedEmailCount, int embeddedChunkCount);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The embedding backfill has reached the end of the stored mail; the next sweep starts from the beginning to pick up whatever a refused call or a full queue left behind.")]
    private partial void LogSweepCompleted();

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "No embedding profile is active, so the backfill has no vector space to work towards. Activating a declared profile is what starts it.")]
    private partial void LogNoActiveProfile();

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The configured embedding model is not the one the active profile records, so the backfill wrote nothing. Activate the current declaration, or restore the one the stored vectors belong to.")]
    private partial void LogGeneratorDisagreesWithProfile();

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "An embedding provider call failed with {Failure} and ended the backfill run after {EmbeddedChunkCount} passages; the next interval resumes past the message it failed on.")]
    private partial void LogProviderFailed(EmbeddingGenerationFailure failure, int embeddedChunkCount);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "{CallBudgetExhaustedEmailCount} messages each spent every provider call a turn is allowed and are still not fully embedded; a later sweep reaches the rest. A message needing that many calls means Embeddings:MaxPassagesPerRequest is far below what one message of this length carries.")]
    private partial void LogCallBudgetExhausted(int callBudgetExhaustedEmailCount);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Deferred the embedding backfill after an unresolved optimistic concurrency conflict; the next interval will resume from the committed position.")]
    private partial void LogBackfillDeferredAfterConcurrencyConflict(Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The embedding backfill run failed; the next interval will resume from the committed position.")]
    private partial void LogBackfillFailed(Exception exception);
}
