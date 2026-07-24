// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.Synchronization;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Folders;
using MailMcp.Host.Configuration;
using Microsoft.Extensions.Options;

namespace MailMcp.Host.Hosting;

/// <summary>Runs periodic IMAP reconciliation in scoped work units.</summary>
public sealed class MailSynchronizationWorker(IServiceScopeFactory scopeFactory, IOptions<MailSynchronizationOptions> options, ILogger<MailSynchronizationWorker> logger, TimeProvider timeProvider) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var currentOptions = options.Value;
        if (!currentOptions.Enabled)
        {
            logger.LogInformation("IMAP synchronization worker is disabled.");
            return;
        }

        using var timer = new PeriodicTimer(currentOptions.Interval, timeProvider);
        do
        {
            await RunOnceAsync(currentOptions, stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunOnceAsync(MailSynchronizationOptions currentOptions, CancellationToken cancellationToken)
    {
        foreach (var account in currentOptions.Accounts)
        {
            foreach (var folder in account.EffectiveFolders)
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var synchronizer = scope.ServiceProvider.GetRequiredService<MailboxSynchronizer>();
                    var result = await synchronizer.SynchronizeAsync(MailAccountId.Create(account.AccountId), MailFolderName.Create(folder), cancellationToken);
                    logger.LogInformation("Synchronized IMAP folder {AccountId}/{FolderName}; stored {StoredMessageCount} messages, skipped {SkippedOversizedMessageCount} oversized messages, and has more work: {HasMoreMessages}.", account.AccountId, folder, result.StoredMessageCount, result.SkippedOversizedMessageCount, result.HasMoreMessages);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "IMAP synchronization failed for {AccountId}/{FolderName}; the worker will continue with remaining folders and retry on a later interval.", account.AccountId, folder);
                }
            }
        }
    }
}
