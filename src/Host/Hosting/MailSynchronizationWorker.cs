// Copyright © 2026 Krzysztof Kasprowicz

using System.Diagnostics.CodeAnalysis;
using MailMcp.Application.Persistence;
using MailMcp.Application.Synchronization;
using MailMcp.Domain.Accounts;
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
    private async Task RunOnceAsync(MailSynchronizationOptions runSettings, CancellationToken cancellationToken)
    {
        var scheduledFolders = runSettings.Accounts.SelectMany(
            account => account.EffectiveFolders,
            (account, folder) => (AccountId: account.AccountId, ConfiguredFolder: folder));

        foreach (var (accountId, configuredFolder) in scheduledFolders)
        {
            // The configured folder is turned into a mapping inside the guarded body rather than while the sequence is
            // built, so a folder whose configuration reached the worker unusable fails that folder and not the loop.
            var folderAlias = configuredFolder.Alias;

            try
            {
                var folderMapping = configuredFolder.CreateMapping();
                folderAlias = folderMapping.Alias.Value;

                using var scope = this.scopeFactory.CreateScope();

                // The folder was scheduled from this run's snapshot, so the scope must connect with that snapshot too.
                // Letting the scope read the published one would pair an account list from before a reload with an
                // endpoint, policy, and credential from after it.
                scope.ServiceProvider.GetRequiredService<ScopedMailSynchronizationSettings>().UseRunSnapshot(runSettings);

                var synchronizer = scope.ServiceProvider.GetRequiredService<MailboxSynchronizer>();
                var result = await synchronizer.SynchronizeAsync(MailAccountId.Create(accountId), folderMapping, cancellationToken);

                this.ReportFolderOutcome(accountId, folderAlias, result);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (PersistenceConcurrencyConflictException exception)
            {
                this.LogFolderSynchronizationDeferredAfterConcurrencyConflict(exception, accountId, folderAlias);
            }
            catch (MailboxUnavailableException exception)
            {
                this.LogFolderSynchronizationDeferredAfterMailServerUnavailable(exception, accountId, folderAlias);
            }
            catch (Exception exception)
            {
                this.LogFolderSynchronizationFailed(exception, accountId, folderAlias);
            }
        }
    }

    /// <summary>Reports one folder's run, keeping an alias that named no single folder separate from a failure.</summary>
    private void ReportFolderOutcome(string accountId, string folderAlias, MailboxSynchronizationResult result)
    {
        if (result.Outcome == MailboxSynchronizationOutcome.FolderAliasUnresolved)
        {
            this.LogFolderAliasUnresolved(accountId, folderAlias);

            return;
        }

        if (result.Outcome == MailboxSynchronizationOutcome.FolderAliasAmbiguous)
        {
            this.LogFolderAliasAmbiguous(accountId, folderAlias);

            return;
        }

        this.LogFolderSynchronized(
            accountId,
            folderAlias,
            result.StoredEmailCount,
            result.SkippedOversizedEmailCount,
            result.UnreadableMimeEmailCount,
            result.HasMoreEmails);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "IMAP synchronization worker is disabled.")]
    private partial void LogSynchronizationDisabled();

    /// <summary>Reports the counts a run produced; the unreadable count is how a malformed message stays visible without its content being logged.</summary>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Synchronized IMAP folder {AccountId}/{FolderAlias}; stored {StoredEmailCount} messages, skipped {SkippedOversizedEmailCount} oversized messages, could not read the MIME of {UnreadableMimeEmailCount} stored messages, and has more work: {HasMoreEmails}.")]
    private partial void LogFolderSynchronized(
        string accountId,
        string folderAlias,
        int storedEmailCount,
        int skippedOversizedEmailCount,
        int unreadableMimeEmailCount,
        bool hasMoreEmails);

    /// <summary>Separates a folder the server does not advertise from a folder that failed, because only one of them is the operator's to fix in configuration.</summary>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Folder alias {AccountId}/{FolderAlias} matched no folder the mail server advertises; it was not synchronized and the remaining folders of this account continue.")]
    private partial void LogFolderAliasUnresolved(
        string accountId,
        string folderAlias);

    /// <summary>Names the remedy, because an ambiguous role is fixed by configuring a path rather than by waiting.</summary>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Folder alias {AccountId}/{FolderAlias} matched several folders the mail server advertises, so it was not synchronized; configure its RemotePath to state which folder it names.")]
    private partial void LogFolderAliasAmbiguous(
        string accountId,
        string folderAlias);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "IMAP synchronization failed for {AccountId}/{FolderAlias}; the worker will continue with remaining folders and retry on a later interval.")]
    private partial void LogFolderSynchronizationFailed(
        Exception exception,
        string accountId,
        string folderAlias);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Deferred IMAP folder synchronization for {AccountId}/{FolderAlias} after an unresolved optimistic concurrency conflict; the next interval will retry from the persisted checkpoint.")]
    private partial void LogFolderSynchronizationDeferredAfterConcurrencyConflict(
        Exception exception,
        string accountId,
        string folderAlias);

    /// <summary>Separates a mail server that is refusing work from a host that is shutting down, which cancellation already reports.</summary>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Deferred IMAP folder synchronization for {AccountId}/{FolderAlias} because the mail server did not serve it within its configured resilience budget; the next interval will retry from the persisted checkpoint.")]
    private partial void LogFolderSynchronizationDeferredAfterMailServerUnavailable(
        Exception exception,
        string accountId,
        string folderAlias);
}
