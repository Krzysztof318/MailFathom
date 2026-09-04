// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Accounts;
using MailFathom.Application.Signals;
using MailFathom.Application.Synchronization.Administration;
using MailFathom.Domain.Accounts;
using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Infrastructure.Observability;
using Microsoft.Extensions.Primitives;

namespace MailFathom.Host.Hosting.Workers;

/// <summary>Supervises one <see cref="AccountSynchronizationSupervisor" /> per configured account.</summary>
/// <remarks>
/// <para>
/// The coordinator itself reaches no mail server and holds no scoped service. It decides which accounts are
/// supervised, bounds how many of them run at once, and owns the shutdown sequence; everything a run does belongs to
/// the supervisor of the account it runs for.
/// </para>
/// <para>
/// It reconciles when a published snapshot changes, when a supervisor ends, and on its own interval. A reload therefore
/// adds, replaces, or removes a supervisor without a restart; an unexpected end is likewise restarted instead of
/// leaving one account silently unsynchronized. Replacing a supervisor cancels scheduling and never its in-flight
/// work-unit token, so a run drains against the snapshot it began with.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed partial class MailSynchronizationCoordinator : BackgroundService
{
    private readonly Dictionary<string, SupervisedAccount> supervisedAccounts = new(StringComparer.Ordinal);
    private readonly IServiceScopeFactory scopeFactory;
    private readonly ISettingsSnapshot<MailSynchronizationOptions> settings;
    private readonly MailSynchronizationTelemetry telemetry;
    private readonly MailSynchronizationRunLedger runLedger;
    private readonly ClientSignals signals;
    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger<MailSynchronizationCoordinator> logger;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes a new mail synchronization coordinator.</summary>
    /// <param name="scopeFactory">Creates the scope each folder work unit runs in.</param>
    /// <param name="settings">Supplies the snapshot the supervised account set is read from.</param>
    /// <param name="telemetry">Published to by every supervisor this coordinator starts, which is why one instance is handed to all of them.</param>
    /// <param name="runLedger">Written to by every supervisor this coordinator starts, for the reason the telemetry is: it is one account of what the whole process is doing.</param>
    /// <param name="signals">Handed to every supervisor this coordinator starts, for the reason the telemetry is: one publisher folds what every account observed rather than one per account.</param>
    /// <param name="loggerFactory">Supplies this coordinator's logger and the logger of every supervisor it starts, so a supervisor logs under its own category.</param>
    /// <param name="timeProvider">Drives the supervision interval and bounds the shutdown drain.</param>
    public MailSynchronizationCoordinator(
        IServiceScopeFactory scopeFactory,
        ISettingsSnapshot<MailSynchronizationOptions> settings,
        MailSynchronizationTelemetry telemetry,
        MailSynchronizationRunLedger runLedger,
        ClientSignals signals,
        ILoggerFactory loggerFactory,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        this.scopeFactory = scopeFactory;
        this.settings = settings;
        this.telemetry = telemetry;
        this.runLedger = runLedger;
        this.signals = signals;
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
            do
            {
                var settingsReload = this.settings.GetReloadToken();
                var settingsSnapshot = this.settings.Current;

                this.SuperviseConfiguredAccounts(
                    settingsSnapshot,
                    accountRunSlots,
                    stoppingToken,
                    workUnitCancellation.Token);

                await this.WaitForNextSupervisionPassAsync(settingsReload, startupSettings.Interval, stoppingToken);
            }
            while (!stoppingToken.IsCancellationRequested);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown. No further supervisor is started, and the running ones are drained below.
        }

        await this.DrainSupervisedAccountsAsync(workUnitCancellation, startupSettings.ShutdownDrainTimeout);
    }

    /// <summary>Waits until the supervision interval elapses or a usable settings snapshot replaces the current one.</summary>
    private async Task WaitForNextSupervisionPassAsync(
        IChangeToken settingsReload,
        TimeSpan interval,
        CancellationToken stoppingToken)
    {
        using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        using var reloadSubscription = settingsReload.RegisterChangeCallback(
            static state => ((CancellationTokenSource)state!).Cancel(),
            waitCancellation);

        var intervalElapsed = Task.Delay(interval, this.timeProvider, waitCancellation.Token);
        Task supervisionEnded = this.supervisedAccounts.Count == 0
            ? Task.Delay(Timeout.InfiniteTimeSpan, waitCancellation.Token)
            : Task.WhenAny(this.supervisedAccounts.Values.Select(static account => account.Task));

        await Task.WhenAny(intervalElapsed, supervisionEnded);
        await waitCancellation.CancelAsync();
    }

    /// <summary>Starts a supervisor for every configured account that has none running.</summary>
    /// <remarks>
    /// A supervisor task that has completed is one whose account was removed, or one that ended unexpectedly. Both are
    /// answered the same way: if the current snapshot still names the account, it is supervised again. A supervisor
    /// never faults, so replacing a completed task leaves nothing unobserved.
    /// </remarks>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Ownership of each linked cancellation source passes to the supervised-account record and is released when that supervisor ends or the coordinator drains.")]
    private void SuperviseConfiguredAccounts(
        MailSynchronizationOptions settingsSnapshot,
        SemaphoreSlim accountRunSlots,
        CancellationToken schedulingToken,
        CancellationToken workUnitToken)
    {
        foreach (var accountId in this.supervisedAccounts
            .Where(static entry => entry.Value.Task.IsCompleted)
            .Select(static entry => entry.Key)
            .ToArray())
        {
            this.supervisedAccounts.Remove(accountId, out var completed);
            completed!.SchedulingCancellation.Dispose();
        }

        foreach (var supervision in this.supervisedAccounts.Values
            .Where(supervision => !ReferenceEquals(supervision.SettingsSnapshot, settingsSnapshot)))
        {
            supervision.SchedulingCancellation.Cancel();
        }

        // Read through the application port in a scope of its own rather than off the configuration snapshot, because
        // the served set is now configuration plus the owner a startup gate established and only the composed port
        // holds both. The scope lives for the read: a supervisor gets a scope of its own per work unit.
        using var accountScope = this.scopeFactory.CreateScope();
        accountScope.ServiceProvider
            .GetRequiredService<ScopedMailSynchronizationSettings>()
            .UseRunSnapshot(settingsSnapshot);

        var servedAccounts = accountScope.ServiceProvider
            .GetRequiredService<IDeploymentMailAccountCatalog>()
            .ServedAccounts;

        foreach (var account in servedAccounts.Select(static account => account.Identity))
        {
            if (this.supervisedAccounts.ContainsKey(account.Id.Value))
            {
                continue;
            }

            var accountScheduling = CancellationTokenSource.CreateLinkedTokenSource(schedulingToken);
            var task = this.StartSupervisor(
                account,
                accountRunSlots,
                accountScheduling.Token,
                workUnitToken);

            this.supervisedAccounts[account.Id.Value] = new SupervisedAccount(
                settingsSnapshot,
                accountScheduling,
                task);

            this.LogAccountSupervisionStarted(account.Id.Value);
        }
    }

    private Task StartSupervisor(
        MailAccountIdentity account,
        SemaphoreSlim accountRunSlots,
        CancellationToken schedulingToken,
        CancellationToken workUnitToken)
    {
        // The watch is built here and owned by the supervisor, so a supervisor that ends releases the connections it was
        // holding and the replacement starts with none.
        var pushNotifications = new AccountPushNotificationWatch(
            account.Id,
            this.scopeFactory,
            this.loggerFactory.CreateLogger<AccountPushNotificationWatch>(),
            this.timeProvider);

        var supervisor = new AccountSynchronizationSupervisor(
            account,
            this.scopeFactory,
            this.settings,
            accountRunSlots,
            pushNotifications,
            this.telemetry,
            this.runLedger,
            this.signals,
            this.loggerFactory.CreateLogger<AccountSynchronizationSupervisor>());

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

        var supervision = Task.WhenAll(this.supervisedAccounts.Values.Select(static account => account.Task));

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

        foreach (var account in this.supervisedAccounts.Values)
        {
            account.SchedulingCancellation.Dispose();
        }

        this.supervisedAccounts.Clear();
    }

    /// <summary>One account's supervisor, the snapshot it represents, and the scheduling stop it owns.</summary>
    private sealed record SupervisedAccount(
        MailSynchronizationOptions SettingsSnapshot,
        CancellationTokenSource SchedulingCancellation,
        Task Task);

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
