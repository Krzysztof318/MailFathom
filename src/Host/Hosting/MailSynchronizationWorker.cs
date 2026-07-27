// Copyright © 2026 Krzysztof Kasprowicz

using System.Diagnostics.CodeAnalysis;
using MailMcp.Application.Persistence;
using MailMcp.Application.Synchronization;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Folders;
using MailMcp.Host.Configuration;

namespace MailMcp.Host.Hosting;

/// <summary>Runs periodic IMAP reconciliation in scoped work units.</summary>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed partial class MailSynchronizationWorker : BackgroundService
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly ISettingsSnapshot<MailSynchronizationOptions> settings;
    private readonly ILogger<MailSynchronizationWorker> logger;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes a new mail synchronization worker.</summary>
    public MailSynchronizationWorker(
        IServiceScopeFactory scopeFactory,
        ISettingsSnapshot<MailSynchronizationOptions> settings,
        ILogger<MailSynchronizationWorker> logger,
        TimeProvider timeProvider)
    {
        this.scopeFactory = scopeFactory;
        this.settings = settings;
        this.logger = logger;
        this.timeProvider = timeProvider;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Whether synchronization runs at all and how often are read once, because both shape the loop this method is.
    /// Everything a run reads — accounts, folders, and the references behind their secrets — is taken from the
    /// published snapshot when that run begins, so a configuration reload or a rotated credential reaches the next run
    /// and never the one already under way.
    /// </remarks>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var startupSettings = this.settings.Current;
        if (!startupSettings.Enabled)
        {
            this.LogSynchronizationDisabled();

            return;
        }

        using var timer = new PeriodicTimer(startupSettings.Interval, this.timeProvider);

        do
        {
            await this.RunOnceAsync(this.settings.Current, stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The hosted worker isolates unexpected per-folder failures so later folders and intervals can continue.")]
    private async Task RunOnceAsync(MailSynchronizationOptions currentOptions, CancellationToken cancellationToken)
    {
        var scheduledFolders = currentOptions.Accounts.SelectMany(
            account => account.EffectiveFolders,
            (account, folder) => (AccountId: account.AccountId, FolderName: folder));

        foreach (var (accountId, folderName) in scheduledFolders)
        {
            try
            {
                using var scope = this.scopeFactory.CreateScope();

                var synchronizer = scope.ServiceProvider.GetRequiredService<MailboxSynchronizer>();
                var result = await synchronizer.SynchronizeAsync(MailAccountId.Create(accountId), MailFolderName.Create(folderName), cancellationToken);

                this.LogFolderSynchronized(
                    accountId,
                    folderName,
                    result.StoredEmailCount,
                    result.SkippedOversizedEmailCount,
                    result.HasMoreEmails);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (PersistenceConcurrencyConflictException exception)
            {
                this.LogFolderSynchronizationDeferredAfterConcurrencyConflict(exception, accountId, folderName);
            }
            catch (Exception exception)
            {
                this.LogFolderSynchronizationFailed(exception, accountId, folderName);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "IMAP synchronization worker is disabled.")]
    private partial void LogSynchronizationDisabled();

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Synchronized IMAP folder {AccountId}/{FolderName}; stored {StoredEmailCount} messages, skipped {SkippedOversizedEmailCount} oversized messages, and has more work: {HasMoreEmails}.")]
    private partial void LogFolderSynchronized(
        string accountId,
        string folderName,
        int storedEmailCount,
        int skippedOversizedEmailCount,
        bool hasMoreEmails);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "IMAP synchronization failed for {AccountId}/{FolderName}; the worker will continue with remaining folders and retry on a later interval.")]
    private partial void LogFolderSynchronizationFailed(
        Exception exception,
        string accountId,
        string folderName);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Deferred IMAP folder synchronization for {AccountId}/{FolderName} after an unresolved optimistic concurrency conflict; the next interval will retry from the persisted checkpoint.")]
    private partial void LogFolderSynchronizationDeferredAfterConcurrencyConflict(
        Exception exception,
        string accountId,
        string folderName);
}
