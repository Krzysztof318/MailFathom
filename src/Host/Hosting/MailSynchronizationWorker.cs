// Copyright © 2026 Krzysztof Kasprowicz

using System.Diagnostics.CodeAnalysis;
using MailMcp.Application.Synchronization;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Folders;
using MailMcp.Host.Configuration;
using Microsoft.Extensions.Options;

namespace MailMcp.Host.Hosting;

/// <summary>Runs periodic IMAP reconciliation in scoped work units.</summary>
public sealed partial class MailSynchronizationWorker : BackgroundService
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly IOptions<MailSynchronizationOptions> options;
    private readonly ILogger<MailSynchronizationWorker> logger;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes a new mail synchronization worker.</summary>
    public MailSynchronizationWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<MailSynchronizationOptions> options,
        ILogger<MailSynchronizationWorker> logger,
        TimeProvider timeProvider)
    {
        this.scopeFactory = scopeFactory;
        this.options = options;
        this.logger = logger;
        this.timeProvider = timeProvider;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var currentOptions = this.options.Value;
        if (!currentOptions.Enabled)
        {
            this.LogSynchronizationDisabled();

            return;
        }

        using var timer = new PeriodicTimer(currentOptions.Interval, this.timeProvider);

        do
        {
            await this.RunOnceAsync(currentOptions, stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The hosted worker isolates unexpected per-folder failures so later folders and intervals can continue.")]
    private async Task RunOnceAsync(MailSynchronizationOptions currentOptions, CancellationToken cancellationToken)
    {
        foreach (var account in currentOptions.Accounts)
        {
            foreach (var folder in account.EffectiveFolders)
            {
                try
                {
                    using var scope = this.scopeFactory.CreateScope();

                    var synchronizer = scope.ServiceProvider.GetRequiredService<MailboxSynchronizer>();
                    var result = await synchronizer.SynchronizeAsync(MailAccountId.Create(account.AccountId), MailFolderName.Create(folder), cancellationToken);

                    this.LogFolderSynchronized(
                        account.AccountId,
                        folder,
                        result.StoredEmailCount,
                        result.SkippedOversizedEmailCount,
                        result.HasMoreEmails);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    this.LogFolderSynchronizationFailed(exception, account.AccountId, folder);
                }
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
}
