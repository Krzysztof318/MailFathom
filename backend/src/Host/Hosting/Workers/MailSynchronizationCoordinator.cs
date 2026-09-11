// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Accounts;
using MailFathom.Application.Signals;
using MailFathom.Application.Synchronization;
using MailFathom.Application.Synchronization.Administration;
using MailFathom.Domain.Accounts;
using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Infrastructure.Observability;
using Microsoft.Extensions.Primitives;

namespace MailFathom.Host.Hosting.Workers;

/// <summary>Supervises one <see cref="AccountSynchronizationSupervisor" /> per configured account this replica holds.</summary>
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
/// <para>
/// Every replica of a deployment reads the same account set, so a supervisor is started only for an account whose
/// lease this replica took, as <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0031-dividing-singleton-work-between-replicas-with-a-leased-scope.md">ADR 0031</see>
/// decides. An account another replica holds is asked for again on each pass, which is how it moves here once that
/// replica stops.
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
    private readonly MailAccountRunSignal runSignal;
    private readonly ClientSignals signals;
    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger<MailSynchronizationCoordinator> logger;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes a new mail synchronization coordinator.</summary>
    /// <param name="scopeFactory">Creates the scope each folder work unit and each lease statement runs in.</param>
    /// <param name="settings">Supplies the snapshot the supervised account set is read from.</param>
    /// <param name="telemetry">Published to by every supervisor this coordinator starts, which is why one instance is handed to all of them.</param>
    /// <param name="runLedger">Written to by every supervisor this coordinator starts, for the reason the telemetry is: it is one account of what the whole process is doing.</param>
    /// <param name="runSignal">Handed to every supervisor this coordinator starts, for the reason the telemetry is: one registry carries what was authored for any account to the supervisor waiting on that account.</param>
    /// <param name="signals">Handed to every supervisor this coordinator starts, for the reason the telemetry is: one publisher folds what every account observed rather than one per account.</param>
    /// <param name="loggerFactory">Supplies this coordinator's logger and the logger of every supervisor and hold it starts, so each logs under its own category.</param>
    /// <param name="timeProvider">Drives the supervision interval, the lease renewals, and bounds the shutdown drain.</param>
    public MailSynchronizationCoordinator(
        IServiceScopeFactory scopeFactory,
        ISettingsSnapshot<MailSynchronizationOptions> settings,
        MailSynchronizationTelemetry telemetry,
        MailSynchronizationRunLedger runLedger,
        MailAccountRunSignal runSignal,
        ClientSignals signals,
        ILoggerFactory loggerFactory,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        this.scopeFactory = scopeFactory;
        this.settings = settings;
        this.telemetry = telemetry;
        this.runLedger = runLedger;
        this.runSignal = runSignal;
        this.signals = signals;
        this.loggerFactory = loggerFactory;
        this.logger = loggerFactory.CreateLogger<MailSynchronizationCoordinator>();
        this.timeProvider = timeProvider;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Whether synchronization runs at all, how often the account set is re-read, how many accounts may run at once,
    /// how long an account is held and renewed, and how long shutdown drains are read once, because all of them shape
    /// the loop this method is rather than the work one run does. Everything a run reads is taken from the published
    /// snapshot when that run begins.
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

                await this.SuperviseConfiguredAccountsAsync(
                    settingsSnapshot,
                    startupSettings,
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

    /// <summary>Starts a supervisor for every configured account that has none running and whose lease this replica takes.</summary>
    /// <remarks>
    /// A supervisor task that has completed is one whose account was removed, whose hold was lost, or one that ended
    /// unexpectedly. All three are answered the same way: if the current snapshot still names the account, its lease is
    /// asked for again. A supervisor never faults, so replacing a completed task leaves nothing unobserved.
    /// </remarks>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Ownership of each linked cancellation source passes to the supervised-account record and is released when that supervisor ends or the coordinator drains; each hold is disposed by the supervision it guards.")]
    private async Task SuperviseConfiguredAccountsAsync(
        MailSynchronizationOptions settingsSnapshot,
        MailSynchronizationOptions startupSettings,
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

        foreach (var account in this.ReadServedAccounts(settingsSnapshot))
        {
            if (this.supervisedAccounts.ContainsKey(account.Id.Value))
            {
                continue;
            }

            var hold = await WorkLeaseHold.TryTakeAsync(
                MailAccountSupervisionScope.For(account),
                startupSettings.LeaseDuration,
                startupSettings.LeaseRenewalInterval,
                this.scopeFactory,
                this.loggerFactory.CreateLogger<WorkLeaseHold>(),
                this.timeProvider,
                schedulingToken);

            if (hold is null)
            {
                this.LogAccountHeldElsewhere(account.Id.Value);

                continue;
            }

            var accountScheduling = CancellationTokenSource.CreateLinkedTokenSource(schedulingToken, hold.Lost);
            var task = this.SuperviseWhileHeldAsync(
                account,
                hold,
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

    /// <summary>Reads the accounts the deployment serves under the snapshot this pass supervises against.</summary>
    /// <remarks>
    /// Read through the application port in a scope of its own rather than off the configuration snapshot, because the
    /// served set is configuration plus the user a startup gate established and only the composed port holds both. The
    /// scope lives for the read: a supervisor gets a scope of its own per work unit.
    /// </remarks>
    private MailAccountIdentity[] ReadServedAccounts(MailSynchronizationOptions settingsSnapshot)
    {
        using var accountScope = this.scopeFactory.CreateScope();
        accountScope.ServiceProvider
            .GetRequiredService<ScopedMailSynchronizationSettings>()
            .UseRunSnapshot(settingsSnapshot);

        return [.. accountScope.ServiceProvider
            .GetRequiredService<IDeploymentMailAccountCatalog>()
            .ServedAccounts
            .Select(static account => account.Identity)];
    }

    /// <summary>Supervises one account for as long as this replica holds it, and gives the account back once supervision ends.</summary>
    /// <remarks>
    /// The hold is renewed for the whole of the supervision, the shutdown drain included, and given back only after the
    /// supervisor and the push watch it owns have ended, so no connection of this replica's outlives its hold. A hold
    /// that is lost cancels the run in flight through its work-unit token rather than draining it: the drain is for a
    /// host that stops while it still holds the account, and a replica that lost the hold has only the rest of the
    /// lease's margin before another replica may open its own sessions.
    /// </remarks>
    private async Task SuperviseWhileHeldAsync(
        MailAccountIdentity account,
        WorkLeaseHold hold,
        SemaphoreSlim accountRunSlots,
        CancellationToken schedulingToken,
        CancellationToken workUnitToken)
    {
        using var renewalStop = new CancellationTokenSource();
        using var heldWorkUnits = CancellationTokenSource.CreateLinkedTokenSource(workUnitToken, hold.Lost);

        var renewals = hold.KeepAsync(renewalStop.Token);

        try
        {
            await this.StartSupervisor(account, accountRunSlots, schedulingToken, heldWorkUnits.Token);
        }
        finally
        {
            await renewalStop.CancelAsync();
            await renewals;
            await hold.ReleaseAsync();
            hold.Dispose();
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
            this.runSignal,
            this.signals,
            this.loggerFactory.CreateLogger<AccountSynchronizationSupervisor>());

        return supervisor.RunAsync(schedulingToken, workUnitToken);
    }

    /// <summary>Waits, bounded, for the work units still running and cancels whatever outlives that bound.</summary>
    /// <remarks>
    /// Cancelling every supervisor at the moment the host stops would tear a run down wherever it happened to be. The
    /// drain instead lets a work unit finish what it started — the local write that follows a fetch, and the
    /// checkpoint that follows that write — and cancels only once the configured bound has passed, at which point the
    /// progress already committed is durable and the next start resumes from it. Each supervision gives its account
    /// back as it ends, so what the drain waits for includes the release.
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

    /// <summary>Records the ordinary answer for an account another replica supervises, which is why it is not worth more than debug.</summary>
    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Account {AccountId} is not supervised here because its lease is held elsewhere; it is asked for again on the next pass.")]
    private partial void LogAccountHeldElsewhere(string accountId);

    /// <summary>Records that shutdown ran out of patience, because a run cut short is what the next start has to resume.</summary>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Synchronization work was still running {DrainTimeout} after shutdown began and was cancelled; the progress already committed stays durable and the next start resumes from it.")]
    private partial void LogInFlightWorkUnitsCancelled(TimeSpan drainTimeout);
}
