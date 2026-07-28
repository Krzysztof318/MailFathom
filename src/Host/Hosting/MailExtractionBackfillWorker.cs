// Copyright © 2026 Krzysztof Kasprowicz

using System.Diagnostics.CodeAnalysis;
using MailMcp.Application.Emails;
using MailMcp.Application.Persistence;
using MailMcp.Host.Configuration;
using Microsoft.Extensions.Options;

namespace MailMcp.Host.Hosting;

/// <summary>Runs the extraction backfill in scoped work units until no stored email awaits extraction.</summary>
/// <remarks>
/// The worker ends itself once a run reports no remaining work, rather than idling on its interval forever. Every email
/// stored from then on is extracted as it is written, so a completed backfill has nothing left to find and a query per
/// interval would only be a query per interval.
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed partial class MailExtractionBackfillWorker : BackgroundService
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly MailExtractionBackfillOptions settings;
    private readonly ILogger<MailExtractionBackfillWorker> logger;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes a new extraction backfill worker.</summary>
    public MailExtractionBackfillWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<MailExtractionBackfillOptions> settings,
        ILogger<MailExtractionBackfillWorker> logger,
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
            this.LogBackfillDisabled();

            return;
        }

        using var timer = new PeriodicTimer(this.settings.Interval, this.timeProvider);

        do
        {
            if (!await this.RunOnceAsync(stoppingToken))
            {
                return;
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
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
        try
        {
            using var scope = this.scopeFactory.CreateScope();

            var backfill = scope.ServiceProvider.GetRequiredService<StoredEmailExtractionBackfill>();
            var result = await backfill.RunAsync(cancellationToken);

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
            throw;
        }
        catch (PersistenceConcurrencyConflictException exception)
        {
            this.LogBackfillDeferredAfterConcurrencyConflict(exception);

            return true;
        }
        catch (Exception exception)
        {
            this.LogBackfillFailed(exception);

            return true;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Extracted-text backfill is disabled.")]
    private partial void LogBackfillDisabled();

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
