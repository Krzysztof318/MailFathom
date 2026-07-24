// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.Synchronization;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Folders;
using MailMcp.Infrastructure.Mail.MailKit;
using Microsoft.Extensions.Options;

namespace MailMcp.Host.Configuration;

/// <summary>Maps host-bound mail synchronization options into immutable application settings for new operations.</summary>
public sealed class MailSynchronizationSettingsReader : ISynchronizationSettingsReader, IMailKitImapAccountSettingsProvider, IDisposable
{
    private readonly ILogger<MailSynchronizationSettingsReader> logger;
    private readonly IDisposable? reloadSubscription;
    private Snapshot currentSnapshot;

    /// <summary>Initializes a reader with a validated startup snapshot and last-known-good reload behavior.</summary>
    public MailSynchronizationSettingsReader(IOptionsMonitor<MailSynchronizationOptions> optionsMonitor, ILogger<MailSynchronizationSettingsReader> logger)
    {
        this.logger = logger;
        this.currentSnapshot = Map(optionsMonitor.CurrentValue);
        this.reloadSubscription = optionsMonitor.OnChange(this.PublishReloadIfValid);
    }

    /// <inheritdoc />
    public MailSynchronizationSettings GetCurrentSettings() => this.currentSnapshot.Settings;

    /// <inheritdoc />
    public MailKitImapAccountSettings GetSettings(string accountId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        return this.currentSnapshot.ConnectionSettings[accountId];
    }

    /// <inheritdoc />
    public void Dispose() => this.reloadSubscription?.Dispose();

    private void PublishReloadIfValid(MailSynchronizationOptions options)
    {
        try
        {
            this.currentSnapshot = Map(options);
        }
        catch (ArgumentException exception)
        {
            this.logger.LogWarning(exception, "Rejected MailSynchronization configuration reload with code {ErrorCode}; the previous validated settings snapshot remains active.", "MailSynchronizationReloadInvalid");
        }
    }

    private static Snapshot Map(MailSynchronizationOptions source)
    {
        var accounts = source.Accounts.Select(account => new MailSynchronizationAccountSettings(
            MailAccountId.Create(account.AccountId),
            account.EffectiveFolders.Select(folder => MailFolderName.Create(folder)).ToArray())).ToArray();
        var limits = new MailboxSynchronizationOptions
        {
            MaxMetadataBatchSize = source.MaxMetadataBatchSize,
            MaxRawMimeBytes = source.MaxRawMimeBytes,
            MaxUidWindowsPerRun = source.MaxUidWindowsPerRun,
        };
        var connectionSettings = source.Accounts.ToDictionary(
            account => account.AccountId,
            account => new MailKitImapAccountSettings(account.AccountId, account.Host, account.Port, account.UseTls, account.UserName, account.Password),
            StringComparer.Ordinal);

        return new Snapshot(new MailSynchronizationSettings(source.Enabled, source.Interval, limits, accounts), connectionSettings);
    }

    private sealed record Snapshot(MailSynchronizationSettings Settings, IReadOnlyDictionary<string, MailKitImapAccountSettings> ConnectionSettings);
}
