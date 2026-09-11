// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Coordination;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Persistence;
using MailFathom.Common.Observability;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Infrastructure.Observability;
using Microsoft.Extensions.Options;

namespace MailFathom.Host.Hosting.Workers;

/// <summary>Runs the extraction backfill in scoped work units until no stored email awaits extraction.</summary>
/// <remarks>
/// <para>
/// The worker ends itself once a run reports no remaining work, rather than idling on its interval forever. Every email
/// stored from then on is extracted as it is written, so a completed backfill has nothing left to find and a query per
/// interval would only be a query per interval.
/// </para>
/// <para>
/// A run happens on one replica at a time, as <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0031-dividing-singleton-work-between-replicas-with-a-leased-scope.md">ADR 0031</see>
/// decides. Each run takes the walk's lease before it reads the position and gives it back when it ends, so a replica
/// refused it reads nothing and asks again on its next interval, and a replica that ends itself leaves the walk free for
/// every other replica to find the same answer on its own.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed partial class MailExtractionBackfillWorker : BackgroundService
{
    /// <summary>The name one bounded backfill pass opens its span under.</summary>
    /// <remarks>
    /// A run of this worker is the one piece of extraction nothing else in a trace explains: it is caused by an
    /// interval rather than by a request, so without a span of its own its database commands appear as parentless work
    /// beside the requests they compete with. Named after what the pass does rather than after the worker, so it reads
    /// as the work that was done if the pass is ever scheduled from somewhere else.
    /// </remarks>
    internal const string RunSpanName = "backfill_email_extraction";

    internal const string ExtractedTagName = "mailfathom.mail.extraction.backfill.extracted";
    internal const string UnreadableTagName = "mailfathom.mail.extraction.backfill.unreadable";
    internal const string MissingContentTagName = "mailfathom.mail.extraction.backfill.missing_content";
    internal const string RemainingTagName = "mailfathom.mail.extraction.backfill.remaining";

    /// <summary>The tag name and the four outcomes, taken from the instruments so a span and a series cannot disagree.</summary>
    internal const string OutcomeTagName = MailExtractionBackfillTelemetry.OutcomeTagName;

    internal const string SucceededOutcomeName = MailExtractionBackfillTelemetry.SucceededOutcomeName;
    internal const string DeferredOutcomeName = MailExtractionBackfillTelemetry.DeferredOutcomeName;
    internal const string FailedOutcomeName = MailExtractionBackfillTelemetry.FailedOutcomeName;
    internal const string InterruptedOutcomeName = MailExtractionBackfillTelemetry.InterruptedOutcomeName;

    /// <summary>The scope one run is held under, named after the position row the walk commits its cursor to.</summary>
    internal static readonly WorkScope WalkScope = WorkScope.Create("stored-email-extraction");

    private readonly IServiceScopeFactory scopeFactory;
    private readonly MailExtractionBackfillOptions settings;
    private readonly MailExtractionBackfillTelemetry telemetry;
    private readonly ILogger<MailExtractionBackfillWorker> logger;
    private readonly ILoggerFactory loggerFactory;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes a new extraction backfill worker.</summary>
    public MailExtractionBackfillWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<MailExtractionBackfillOptions> settings,
        MailExtractionBackfillTelemetry telemetry,
        ILogger<MailExtractionBackfillWorker> logger,
        ILoggerFactory loggerFactory,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(settings);

        this.scopeFactory = scopeFactory;
        this.settings = settings.Value;
        this.telemetry = telemetry;
        this.logger = logger;
        this.loggerFactory = loggerFactory;
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

        using var timer = new PeriodicTimer(this.settings.Interval, this.timeProvider);

        do
        {
            if (!await this.RunOnceWhereHeldAsync(stoppingToken))
            {
                return;
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Runs one pass where this replica is granted the walk, and reports whether the worker should keep going.</summary>
    /// <remarks>
    /// A refusal and a lost hold both keep the worker going, because neither says whether emails still await
    /// extraction: the walk is another replica's for now, and only a run of this replica's own that found nothing left
    /// ends it. The hold is given back before that answer is returned, so a worker ending itself frees the walk rather
    /// than keeping it from the replicas still asking.
    /// </remarks>
    private async Task<bool> RunOnceWhereHeldAsync(CancellationToken stoppingToken)
    {
        using var hold = await WorkLeaseHold.TryTakeAsync(
            WalkScope,
            this.settings.LeaseDuration,
            this.settings.LeaseRenewalInterval,
            this.scopeFactory,
            this.loggerFactory.CreateLogger<WorkLeaseHold>(),
            this.timeProvider,
            stoppingToken);

        if (hold is null)
        {
            this.LogRunHeldElsewhere(this.settings.Interval);

            return true;
        }

        try
        {
            return await hold.RunWhileHeldAsync(this.RunOnceAsync, stoppingToken);
        }
        catch (OperationCanceledException) when (hold.Lost.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
        {
            // The hold has said why it was lost, and the run was published as interrupted. What it committed stays
            // durable, and whichever replica holds the walk next resumes from it.
            return true;
        }
    }

    /// <summary>Runs one bounded pass and reports whether the worker should keep going.</summary>
    /// <remarks>
    /// A failed run keeps the worker alive on purpose. The database being briefly unavailable, or a competing writer
    /// winning a race, says nothing about whether emails still await extraction, and the committed position means the
    /// next interval resumes rather than restarts.
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The hosted worker isolates an unexpected failure so a later interval can resume from the committed position.")]
    private async Task<bool> RunOnceAsync(CancellationToken cancellationToken)
    {
        using var run = Telemetry.ActivitySource.StartActivity(RunSpanName);
        var startedAt = this.timeProvider.GetTimestamp();

        try
        {
            using var scope = this.scopeFactory.CreateScope();

            var backfill = scope.ServiceProvider.GetRequiredService<StoredEmailExtractionBackfill>();
            var result = await backfill.RunAsync(cancellationToken);

            this.telemetry.RecordCompleted(result, this.timeProvider.GetElapsedTime(startedAt));

            run?.SetTag(ExtractedTagName, result.ExtractedEmailCount);
            run?.SetTag(UnreadableTagName, result.UnreadableEmailCount);
            run?.SetTag(MissingContentTagName, result.MissingContentEmailCount);
            run?.SetTag(RemainingTagName, result.EmailsRemain);
            run?.SetTag(OutcomeTagName, SucceededOutcomeName);
            run?.SetStatus(ActivityStatusCode.Ok);

            this.LogBackfillProgressed(
                result.ExtractedEmailCount,
                result.UnreadableEmailCount,
                result.MissingContentEmailCount,
                result.EmailsRemain);

            if (!result.EmailsRemain)
            {
                this.LogBackfillCompleted();
            }

            return result.EmailsRemain;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown or a lost hold rather than a failure, exactly as an interrupted synchronization cycle is, so a
            // rolling restart or a handover between replicas does not read as a backfill that broke.
            run?.SetTag(OutcomeTagName, InterruptedOutcomeName);
            this.telemetry.RecordInterrupted(this.timeProvider.GetElapsedTime(startedAt));

            throw;
        }
        catch (PersistenceConcurrencyConflictException exception)
        {
            run?.SetTag(OutcomeTagName, DeferredOutcomeName);
            this.telemetry.RecordDeferred(this.timeProvider.GetElapsedTime(startedAt));

            this.LogBackfillDeferredAfterConcurrencyConflict(exception);

            return true;
        }
        catch (Exception exception)
        {
            run?.SetTag(OutcomeTagName, FailedOutcomeName);
            run?.SetStatus(ActivityStatusCode.Error);
            this.telemetry.RecordFailed(this.timeProvider.GetElapsedTime(startedAt));

            this.LogBackfillFailed(exception);

            return true;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Extracted-text backfill is disabled.")]
    private partial void LogBackfillDisabled();

    /// <summary>Records the ordinary answer for a replica whose walk another one is running, which is why it is not worth more than debug.</summary>
    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Another replica is running the extracted-text backfill, so this one reads nothing and asks again in {Interval}.")]
    private partial void LogRunHeldElsewhere(TimeSpan interval);

    /// <summary>Reports one run in counts only; no subject, address, or fragment of body text may reach a log.</summary>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Extracted-text backfill run finished; extracted {ExtractedEmailCount} messages, stepped over {UnreadableEmailCount} unreadable messages and {MissingContentEmailCount} messages without stored content, and has more work: {EmailsRemain}.")]
    private partial void LogBackfillProgressed(
        int extractedEmailCount,
        int unreadableEmailCount,
        int missingContentEmailCount,
        bool emailsRemain);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Extracted-text backfill has reached the end of the stored emails; every message synchronized from now on is extracted as it is written.")]
    private partial void LogBackfillCompleted();

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Deferred the extracted-text backfill after an unresolved optimistic concurrency conflict; the next interval will resume from the committed position.")]
    private partial void LogBackfillDeferredAfterConcurrencyConflict(Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The extracted-text backfill run failed; the next interval will resume from the committed position.")]
    private partial void LogBackfillFailed(Exception exception);
}
