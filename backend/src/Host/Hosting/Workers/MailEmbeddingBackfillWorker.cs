// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Embeddings.Backfill;
using MailFathom.Application.Emails.Embeddings.Generations;
using MailFathom.Application.Persistence;
using MailFathom.Host.Configuration.Embeddings;
using MailFathom.Infrastructure.Observability;
using Microsoft.Extensions.Options;

namespace MailFathom.Host.Hosting.Workers;

/// <summary>Runs the embedding upkeep pass in scoped work units, pacing itself by what the last one found.</summary>
/// <remarks>
/// <para>
/// The pass is the backfill sweep and the two things that ride it: completing a generation the sweep has finished
/// filling, and removing the vectors of one it replaced. They share this loop and its interval because they are one
/// pipeline, and because each is bounded — the worker's job is to keep starting passes, not to decide what a pass does.
/// </para>
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
/// <para>
/// The pause is chosen by the pass that just ended, so a wait can be sitting out an interval an operator's act has made
/// stale — most visibly the first activation on an instance, where every pass before it found nothing to do and took
/// the long one. <see cref="EmbeddingBackfillSchedule" /> is where the wait is taken for that reason: the act that
/// creates the work releases it, and the instant it would otherwise end at is readable while it lasts.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed partial class MailEmbeddingBackfillWorker : BackgroundService
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly EmailEmbeddingBackfillTelemetry telemetry;
    private readonly EmbeddingBackfillSchedule schedule;
    private readonly EmbeddingBackfillOptions settings;
    private readonly ILogger<MailEmbeddingBackfillWorker> logger;
    private readonly TimeProvider timeProvider;

    /// <summary>The period an owner's ceiling has already been reported for, so one line is written per period.</summary>
    /// <remarks>
    /// The sweep steps past such a message rather than ending, so this fact is true of every pass for as long as the
    /// period lasts — and a busy instance takes the short interval, which would write the same warning every few
    /// seconds and bury the rest of the log. The first pass of a period writes it and the meter carries the rest.
    /// <c>MailEmbeddingWorker</c> holds the same field for the live path, for the same reason. It is only ever touched
    /// from the single loop below, which is why it needs no synchronization.
    /// </remarks>
    private DateTimeOffset? ownerCeilingReportedForPeriodEndingAt;

    /// <summary>Initializes a new embedding backfill worker.</summary>
    public MailEmbeddingBackfillWorker(
        IServiceScopeFactory scopeFactory,
        EmailEmbeddingBackfillTelemetry telemetry,
        EmbeddingBackfillSchedule schedule,
        IOptions<EmbeddingBackfillOptions> settings,
        ILogger<MailEmbeddingBackfillWorker> logger,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(settings);

        this.scopeFactory = scopeFactory;
        this.telemetry = telemetry;
        this.schedule = schedule;
        this.settings = settings.Value;
        this.logger = logger;
        this.timeProvider = timeProvider;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!this.settings.Enabled)
        {
            // Said before returning, because this is the one loop that would ever take a pass: without it an
            // activation would record a due instant nothing is going to reach, and the status surface would report an
            // overdue pass for the life of the process.
            this.schedule.NoPassWillRun();
            this.LogBackfillDisabled();

            return;
        }

        this.LogWorkerStarted();

        while (!stoppingToken.IsCancellationRequested)
        {
            var pause = await this.RunOnceAsync(stoppingToken);

            this.LogNextPassScheduled(pause);

            if (await this.schedule.WaitForNextPassAsync(pause, stoppingToken))
            {
                this.LogNextPassBroughtForward();
            }
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
        // Opened around the whole pass, so the provider calls it makes and the commands it issues are this span's
        // children rather than parentless work an interval caused and nothing in a trace explains.
        using var pass = this.telemetry.BeginPass();

        try
        {
            using var scope = this.scopeFactory.CreateScope();

            var upkeep = scope.ServiceProvider.GetRequiredService<EmbeddingGenerationUpkeep>();
            var result = await upkeep.RunAsync(cancellationToken);

            pass.Ended(result);
            this.telemetry.RecordPass(result);
            this.Report(result);

            return this.PaceAfter(result);
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

    /// <summary>Chooses how long to wait before the next pass, which the spend ceiling answers exactly.</summary>
    /// <remarks>
    /// Neither interval applies to a pass the ceiling stopped. The short one would re-read a ceiling already known to
    /// bind, over and over until the period ended; the long one would leave a rolled-over period idle for as much as a
    /// quarter of an hour. The roll-over is an instant the run itself reported, so the wait is exactly it. The removal
    /// of a superseded generation is deliberately not exempted — it reaches no provider, but it rides this same pass,
    /// and the alternative is a loop that spends the pause deleting rows nobody is waiting for.
    /// </remarks>
    private TimeSpan PaceAfter(EmbeddingGenerationUpkeepResult result)
    {
        if (result.Sweep.SpendPeriodEndsAt is { } periodEndsAt)
        {
            var wait = periodEndsAt - this.timeProvider.GetUtcNow();
            if (wait > TimeSpan.Zero)
            {
                this.LogSpendCeilingReached(wait);

                return wait;
            }
        }

        return result.MoreWorkIsWorthTryingSoon ? this.settings.Interval : this.settings.IdleSweepInterval;
    }

    /// <summary>Says what the pass means for an operator, at the level each part of it deserves.</summary>
    /// <remarks>
    /// The switch and the removal are reported before the sweep, because they are the events an operator watching a
    /// model change is waiting for and the sweep's own counters are what they have been reading in the meantime.
    /// </remarks>
    private void Report(EmbeddingGenerationUpkeepResult result)
    {
        if (result.Transition == EmbeddingGenerationTransition.Switched)
        {
            this.LogGenerationSwitched();
        }

        if (result.RemovedSupersededVectorCount > 0)
        {
            this.LogSupersededVectorsRemoved(result.RemovedSupersededVectorCount);
        }

        this.ReportSweep(result.Sweep);
    }

    /// <summary>Says what the walk part of the pass means for an operator, at the level that outcome deserves.</summary>
    /// <remarks>
    /// Progress and a completed sweep are ordinary, and an instance that has activated no profile is the state ADR 0006
    /// makes supported, so none of the three is a warning. The two that need an operator are a declaration disagreeing
    /// with what was activated and a provider that refused.
    /// </remarks>
    private void ReportSweep(StoredEmailEmbeddingBackfillResult result)
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

        // Reported beside the run's ending for the same reason, and it is the one number that says a bound was reached
        // without the run stopping: the walk steps past a message whose owner has spent their period so that everybody
        // else's mail keeps being embedded, which leaves nothing else for an operator to read it from. Once per period
        // rather than once per pass, because the run does not end on it and every pass until the rollover would repeat
        // the same fact — which is why the result names the period rather than only the count.
        if (result.OwnerSpendCeilingEmailCount > 0
            && this.ownerCeilingReportedForPeriodEndingAt != result.OwnerSpendPeriodEndsAt)
        {
            this.ownerCeilingReportedForPeriodEndingAt = result.OwnerSpendPeriodEndsAt;
            this.LogOwnerSpendCeilingReached(result.OwnerSpendCeilingEmailCount);
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

    /// <summary>Says how long the pause the last pass chose lasts, which is the one thing a quiet deployment never stated.</summary>
    /// <remarks>
    /// At <see cref="LogLevel.Debug" /> because it is written after every pass, including the thirty-second ones a busy
    /// instance takes. What an operator reads instead is <c>mfctl embedding status</c>, which reports the instant this
    /// pause ends at from <see cref="EmbeddingBackfillSchedule" /> without a level being turned up first. A line
    /// following it saying the pass was brought forward is not a contradiction: this one names the pause that act cut
    /// short.
    /// </remarks>
    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "The next embedding backfill pass is due in {Pause}.")]
    private partial void LogNextPassScheduled(TimeSpan pause);

    /// <summary>Says that an operator's act cut the pause short, which happens only when one has been performed.</summary>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Activating or cancelling a generation brought the next embedding backfill pass forward, so it starts now rather than when the pause the last pass chose would have ended.")]
    private partial void LogNextPassBroughtForward();

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
        Level = LogLevel.Information,
        Message = "The generation being built is complete and is now the one searches are answered from; the generation it replaced is superseded and its vectors are being removed.")]
    private partial void LogGenerationSwitched();

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Removed {RemovedVectorCount} vectors of a superseded generation; they are derived personal data whose purpose ended at the switch, so none of them is kept for a rollback.")]
    private partial void LogSupersededVectorsRemoved(int removedVectorCount);

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
        Message = "This deployment's embedding spend ceiling for this period is reached, so the backfill is paused for {Wait} until the period rolls over; the position it committed is where it resumes and nothing is lost by the wait. Raise Embeddings:MaxInputCharactersPerPeriod to spend more per period.")]
    private partial void LogSpendCeilingReached(TimeSpan wait);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "{OwnerSpendCeilingEmailCount} messages were stepped past because the owner they belong to has spent what one period admits for them; every other owner's mail kept being embedded, and the rolled-over period reaches these. Raise Embeddings:MaxInputCharactersPerPeriodPerOwner to admit more per owner, or set it to zero to bound only the deployment.")]
    private partial void LogOwnerSpendCeilingReached(int ownerSpendCeilingEmailCount);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Deferred the embedding backfill after an unresolved optimistic concurrency conflict; the next interval will resume from the committed position.")]
    private partial void LogBackfillDeferredAfterConcurrencyConflict(Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The embedding backfill run failed; the next interval will resume from the committed position.")]
    private partial void LogBackfillFailed(Exception exception);
}
