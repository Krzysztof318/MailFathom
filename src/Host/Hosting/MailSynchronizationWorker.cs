// Copyright © 2026 Krzysztof Kasprowicz

using System.Diagnostics.CodeAnalysis;
using MailMcp.Application.Synchronization;

namespace MailMcp.Host.Hosting;

/// <summary>Runs periodic IMAP reconciliation in scoped work units.</summary>
public sealed class MailSynchronizationWorker : BackgroundService
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly ISynchronizationSettingsReader settingsReader;
    private readonly ILogger<MailSynchronizationWorker> logger;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes a new mail synchronization worker.</summary>
    public MailSynchronizationWorker(IServiceScopeFactory scopeFactory, ISynchronizationSettingsReader settingsReader, ILogger<MailSynchronizationWorker> logger, TimeProvider timeProvider)
    {
        this.scopeFactory = scopeFactory;
        this.settingsReader = settingsReader;
        this.logger = logger;
        this.timeProvider = timeProvider;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var startupSettings = this.settingsReader.GetCurrentSettings();
        using var timer = new PeriodicTimer(startupSettings.Interval, this.timeProvider);
        do
        {
            await this.RunOnceAsync(this.settingsReader.GetCurrentSettings(), stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The hosted worker isolates unexpected per-folder failures so later folders and intervals can continue.")]
    private async Task RunOnceAsync(MailSynchronizationSettings currentSettings, CancellationToken cancellationToken)
    {
        if (!currentSettings.Enabled)
        {
            this.logger.LogDebug("IMAP synchronization worker is disabled for this interval.");
            return;
        }

        foreach (var account in currentSettings.Accounts)
        {
            foreach (var folder in account.Folders)
            {
                try
                {
                    using var scope = this.scopeFactory.CreateScope();
                    var synchronizer = scope.ServiceProvider.GetRequiredService<MailboxSynchronizer>();
                    var result = await synchronizer.SynchronizeAsync(account.AccountId, folder, cancellationToken);
                    if (this.logger.IsEnabled(LogLevel.Information))
                    {
                        this.logger.LogInformation("Synchronized IMAP folder {AccountId}/{FolderName}; stored {StoredMessageCount} messages, skipped {SkippedOversizedMessageCount} oversized messages, and has more work: {HasMoreMessages}.", account.AccountId.Value, folder.Value, result.StoredMessageCount, result.SkippedOversizedMessageCount, result.HasMoreMessages);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    this.logger.LogWarning(exception, "IMAP synchronization failed for {AccountId}/{FolderName}; the worker will continue with remaining folders and retry on a later interval.", account.AccountId.Value, folder.Value);
                }
            }
        }
    }
}
