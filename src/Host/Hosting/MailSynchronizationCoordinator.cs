// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Domain.Accounts;
using MailFathom.Host.Configuration;

namespace MailFathom.Host.Hosting;

/// <summary>Supervises one <see cref="AccountSynchronizationSupervisor" /> per configured account.</summary>
/// <remarks>
/// <para>
/// The coordinator itself reaches no mail server and holds no scoped service. It decides which accounts are
/// supervised, bounds how many of them run at once, and owns the shutdown sequence; everything a run does belongs to
/// the supervisor of the account it runs for.
/// </para>
/// <para>
/// It keeps checking on its own interval rather than starting the supervisors once, for two reasons that share one
/// mechanism: an account a configuration reload adds gets a supervisor without a restart, and a supervisor that ended
/// unexpectedly is started again instead of leaving one account silently unsynchronized. A supervisor whose account
/// was removed ends itself, so nothing here has to cancel it.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed partial class MailSynchronizationCoordinator : BackgroundService
{
    private readonly Dictionary<string, Task> supervisedAccounts = new(StringComparer.Ordinal);
    private readonly IServiceScopeFactory scopeFactory;
    private readonly ISettingsSnapshot<MailSynchronizationOptions> settings;
    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger<MailSynchronizationCoordinator> logger;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes a new mail synchronization coordinator.</summary>
    /// <param name="scopeFactory">Creates the scope each folder work unit runs in.</param>
    /// <param name="settings">Supplies the snapshot the supervised account set is read from.</param>
    /// <param name="loggerFactory">Supplies this coordinator's logger and the logger of every supervisor it starts, so a supervisor logs under its own category.</param>
    /// <param name="timeProvider">Drives the supervision interval and bounds the shutdown drain.</param>
    public MailSynchronizationCoordinator(
        IServiceScopeFactory scopeFactory,
        ISettingsSnapshot<MailSynchronizationOptions> settings,
        ILoggerFactory loggerFactory,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        this.scopeFactory = scopeFactory;
        this.settings = settings;
        this.loggerFactory = loggerFactory;
        this.logger = loggerFactory.CreateLogger<MailSynchronizationCoordinator>();
        this.timeProvider = timeProvider;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Whether synchronization runs at all, how often the account set is re-read, how many accounts may run at once,
    /// and how long shutdown drains are read once, because all four shape the loop this method is rather than the work
    /// one run does. Everything a run reads is taken from the published snapshot when that run begins.
    /// </remarks>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var startupSettings = this.settings.Current;

        if (!startupSettings.Enabled)
        {
            this.LogSynchronizationDisabled();

            return;
        }

        using var accountRunSlots = new SemaphoreSlim(startupSettings.MaxConcurrentAccounts);

        // Deliberately not linked to the stopping token. Shutdown stops scheduling at once and only then bounds how
        // long the work already under way may take, which is the whole difference between draining a run and cutting
        // one off between persisting content and advancing its checkpoint.
        using var workUnitCancellation = new CancellationTokenSource();

        try
        {
            using var supervisionTimer = new PeriodicTimer(startupSettings.Interval, this.timeProvider);

            do
            {
                this.SuperviseConfiguredAccounts(accountRunSlots, stoppingToken, workUnitCancellation.Token);
            }
            while (await supervisionTimer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown. No further supervisor is started, and the running ones are drained below.
        }

        await this.DrainSupervisedAccountsAsync(workUnitCancellation, startupSettings.ShutdownDrainTimeout);
    }

    /// <summary>Starts a supervisor for every configured account that has none running.</summary>
    /// <remarks>
    /// A supervisor task that has completed is one whose account was removed, or one that ended unexpectedly. Both are
    /// answered the same way: if the current snapshot still names the account, it is supervised again. A supervisor
    /// never faults, so replacing a completed task leaves nothing unobserved.
    /// </remarks>
    private void SuperviseConfiguredAccounts(
        SemaphoreSlim accountRunSlots,
        CancellationToken schedulingToken,
        CancellationToken workUnitToken)
    {
        foreach (var accountId in this.settings.Current.ServedAccountIds)
        {
            if (this.supervisedAccounts.TryGetValue(accountId.Value, out var supervision) && !supervision.IsCompleted)
            {
                continue;
            }

            this.supervisedAccounts[accountId.Value] = this.StartSupervisor(
                accountId,
                accountRunSlots,
                schedulingToken,
                workUnitToken);

            this.LogAccountSupervisionStarted(accountId.Value);
        }
    }

    private Task StartSupervisor(
        MailAccountId accountId,
        SemaphoreSlim accountRunSlots,
        CancellationToken schedulingToken,
        CancellationToken workUnitToken)
    {
        var supervisor = new AccountSynchronizationSupervisor(
            accountId,
            this.scopeFactory,
            this.settings,
            accountRunSlots,
            this.loggerFactory.CreateLogger<AccountSynchronizationSupervisor>(),
            this.timeProvider);

        return supervisor.RunAsync(schedulingToken, workUnitToken);
    }

    /// <summary>Waits, bounded, for the work units still running and cancels whatever outlives that bound.</summary>
    /// <remarks>
    /// Cancelling every supervisor at the moment the host stops would tear a run down wherever it happened to be. The
    /// drain instead lets a work unit finish what it started — the local write that follows a fetch, and the
    /// checkpoint that follows that write — and cancels only once the configured bound has passed, at which point the
    /// progress already committed is durable and the next start resumes from it.
    /// </remarks>
    private async Task DrainSupervisedAccountsAsync(CancellationTokenSource workUnitCancellation, TimeSpan drainTimeout)
    {
        if (this.supervisedAccounts.Count == 0)
        {
            return;
        }

        var supervision = Task.WhenAll(this.supervisedAccounts.Values);

        try
        {
            await supervision.WaitAsync(drainTimeout, this.timeProvider);
        }
        catch (TimeoutException)
        {
            this.LogInFlightWorkUnitsCancelled(drainTimeout);

            await workUnitCancellation.CancelAsync();
            await supervision;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "IMAP synchronization is disabled.")]
    private partial void LogSynchronizationDisabled();

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Account {AccountId} is now supervised on a synchronization schedule of its own.")]
    private partial void LogAccountSupervisionStarted(string accountId);

    /// <summary>Records that shutdown ran out of patience, because a run cut short is what the next start has to resume.</summary>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Synchronization work was still running {DrainTimeout} after shutdown began and was cancelled; the progress already committed stays durable and the next start resumes from it.")]
    private partial void LogInFlightWorkUnitsCancelled(TimeSpan drainTimeout);
}
