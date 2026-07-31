// Copyright © 2026 Krzysztof Kasprowicz

using System.Diagnostics.CodeAnalysis;
using MailMcp.Application.Persistence;
using MailMcp.Application.Synchronization;
using MailMcp.Domain.Accounts;
using MailMcp.Host.Configuration;

namespace MailMcp.Host.Hosting;

/// <summary>Runs one configured account's synchronization on a schedule and failure state of its own.</summary>
/// <remarks>
/// <para>
/// One supervisor per account is what keeps an unreachable server from delaying every other account: its runs, its
/// consecutive failure count, and its backoff belong to it alone, and the only thing it shares with the other accounts
/// is the slot count that bounds how many of them run at once.
/// </para>
/// <para>
/// A supervisor holds no scoped service. Each folder is a work unit with a scope of its own, and the run's settings
/// snapshot is handed into that scope so the account list a folder was scheduled from is the one it connects with.
/// </para>
/// </remarks>
internal sealed partial class AccountSynchronizationSupervisor
{
    private readonly MailAccountId accountId;
    private readonly IServiceScopeFactory scopeFactory;
    private readonly ISettingsSnapshot<MailSynchronizationOptions> settings;
    private readonly SemaphoreSlim accountRunSlots;
    private readonly ILogger<AccountSynchronizationSupervisor> logger;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes a supervisor for one configured account.</summary>
    /// <param name="accountId">The account this supervisor synchronizes and names in every line it logs.</param>
    /// <param name="scopeFactory">Creates the scope each folder work unit runs in.</param>
    /// <param name="settings">Supplies the snapshot every run is scheduled from.</param>
    /// <param name="accountRunSlots">Bounds how many accounts run at once; owned by the coordinator and never released beyond what this supervisor took.</param>
    /// <param name="logger">Records run outcomes, which carry account and folder aliases and no message-level data.</param>
    /// <param name="timeProvider">Measures run duration and the wait between runs.</param>
    public AccountSynchronizationSupervisor(
        MailAccountId accountId,
        IServiceScopeFactory scopeFactory,
        ISettingsSnapshot<MailSynchronizationOptions> settings,
        SemaphoreSlim accountRunSlots,
        ILogger<AccountSynchronizationSupervisor> logger,
        TimeProvider timeProvider)
    {
        this.accountId = accountId;
        this.scopeFactory = scopeFactory;
        this.settings = settings;
        this.accountRunSlots = accountRunSlots;
        this.logger = logger;
        this.timeProvider = timeProvider;
    }

    /// <summary>Supervises the account until scheduling stops or the account leaves configuration.</summary>
    /// <param name="schedulingToken">Stops the supervisor from starting another run; cancelled when the host begins shutting down.</param>
    /// <param name="workUnitToken">Tears down a run already under way; cancelled only once the coordinator's bounded shutdown drain expires.</param>
    /// <returns>A task that completes when the account is no longer supervised, and that never faults.</returns>
    /// <remarks>
    /// <para>
    /// The two tokens are the whole shutdown contract. The first stops new work immediately, the second is what
    /// interrupts the work already running, and the gap between them is the drain that lets a work unit finish
    /// persisting what it fetched instead of being cut off mid-run.
    /// </para>
    /// <para>
    /// The task completes rather than faults on any outcome, because the coordinator reads a completed supervisor as
    /// an account to supervise again. An unexpected failure therefore costs the account one interval, not its
    /// synchronization, and takes no other account with it.
    /// </para>
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A supervisor isolates its account: an unexpected failure ends this account's supervision and the coordinator starts it again, while every other account keeps running.")]
    internal async Task RunAsync(CancellationToken schedulingToken, CancellationToken workUnitToken)
    {
        try
        {
            await this.SuperviseAsync(schedulingToken, workUnitToken);
        }
        catch (OperationCanceledException)
        {
            this.LogSupervisionStopped(this.accountId.Value);
        }
        catch (Exception exception)
        {
            this.LogSupervisionFailed(exception, this.accountId.Value);
        }
    }

    /// <summary>Runs the account, waits, and runs it again until scheduling stops.</summary>
    /// <remarks>
    /// Everything a run reads is taken from the published snapshot when that run begins, so a reload or a rotated
    /// credential reaches the next run and never the one already under way. The account is looked up in that same
    /// snapshot, which is how a supervisor learns that the operator removed the account it was serving.
    /// </remarks>
    private async Task SuperviseAsync(CancellationToken schedulingToken, CancellationToken workUnitToken)
    {
        var consecutiveFailureCount = 0;

        while (!schedulingToken.IsCancellationRequested)
        {
            var runSettings = this.settings.Current;
            var account = runSettings.FindConfiguredAccount(this.accountId);

            if (account is null)
            {
                this.LogAccountNoLongerConfigured(this.accountId.Value);

                return;
            }

            var runFailed = await this.RunOnceAsync(runSettings, account, schedulingToken, workUnitToken);

            consecutiveFailureCount = runFailed ? consecutiveFailureCount + 1 : 0;

            var delayBeforeNextRun = SynchronizationRunBackoff.DelayBeforeNextRun(
                runSettings.Interval,
                runSettings.MaxFailureBackoff,
                consecutiveFailureCount);

            if (consecutiveFailureCount > 0)
            {
                this.LogNextRunBackedOff(this.accountId.Value, consecutiveFailureCount, delayBeforeNextRun);
            }

            await Task.Delay(delayBeforeNextRun, this.timeProvider, schedulingToken);
        }
    }

    /// <summary>Runs every configured folder of the account once, bounded by the configured folder concurrency.</summary>
    /// <returns><see langword="true" /> when at least one folder failed, which is what puts the account into backoff.</returns>
    /// <remarks>
    /// An alias that matched no advertised folder, or several, is not counted as a failure. It is a configuration
    /// mistake whose remedy is an edit rather than a wait, and backing the account off for it would slow every folder
    /// that is working.
    /// </remarks>
    private async Task<bool> RunOnceAsync(
        MailSynchronizationOptions runSettings,
        MailSynchronizationAccountOptions account,
        CancellationToken schedulingToken,
        CancellationToken workUnitToken)
    {
        var scheduledFolders = account.EffectiveFolders;
        var failedFolderCount = 0;

        await this.accountRunSlots.WaitAsync(schedulingToken);

        var startedAt = this.timeProvider.GetTimestamp();

        try
        {
            await Parallel.ForEachAsync(
                scheduledFolders,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = runSettings.MaxConcurrentFoldersPerAccount,
                    CancellationToken = workUnitToken,
                },
                async (configuredFolder, folderToken) =>
                {
                    if (!await this.SynchronizeFolderAsync(runSettings, configuredFolder, folderToken))
                    {
                        Interlocked.Increment(ref failedFolderCount);
                    }
                });
        }
        finally
        {
            this.accountRunSlots.Release();
        }

        var runDuration = this.timeProvider.GetElapsedTime(startedAt);

        this.LogAccountRunFinished(
            this.accountId.Value,
            scheduledFolders.Count,
            failedFolderCount,
            runDuration);

        return failedFolderCount > 0;
    }

    /// <summary>Synchronizes one folder in a scope of its own and reports whether it completed.</summary>
    /// <remarks>
    /// The configured folder is turned into a mapping inside the guarded body rather than while the run is scheduled,
    /// so a folder whose configuration reached the supervisor unusable fails that folder and not the account.
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The supervisor isolates an unexpected per-folder failure so the account's remaining folders and its later runs can continue.")]
    private async Task<bool> SynchronizeFolderAsync(
        MailSynchronizationOptions runSettings,
        MailFolderMappingOptions configuredFolder,
        CancellationToken cancellationToken)
    {
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
            var result = await synchronizer.SynchronizeAsync(this.accountId, folderMapping, cancellationToken);

            this.ReportFolderOutcome(folderAlias, result);

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (PersistenceConcurrencyConflictException exception)
        {
            this.LogFolderSynchronizationDeferredAfterConcurrencyConflict(exception, this.accountId.Value, folderAlias);

            return false;
        }
        catch (MailboxUnavailableException exception)
        {
            this.LogFolderSynchronizationDeferredAfterMailServerUnavailable(exception, this.accountId.Value, folderAlias);

            return false;
        }
        catch (Exception exception)
        {
            this.LogFolderSynchronizationFailed(exception, this.accountId.Value, folderAlias);

            return false;
        }
    }

    /// <summary>Reports one folder's run, keeping an alias that named no single folder separate from a failure.</summary>
    private void ReportFolderOutcome(string folderAlias, MailboxSynchronizationResult result)
    {
        if (result.Outcome == MailboxSynchronizationOutcome.FolderAliasUnresolved)
        {
            this.LogFolderAliasUnresolved(this.accountId.Value, folderAlias);

            return;
        }

        if (result.Outcome == MailboxSynchronizationOutcome.FolderAliasAmbiguous)
        {
            this.LogFolderAliasAmbiguous(this.accountId.Value, folderAlias);

            return;
        }

        this.LogFolderSynchronized(
            this.accountId.Value,
            folderAlias,
            result.StoredEmailCount,
            result.SkippedOversizedEmailCount,
            result.UnreadableMimeEmailCount,
            result.HasMoreEmails);
    }

    /// <summary>Reports one account's run in counts and duration only; no subject, address, or fragment of a body may reach a log.</summary>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Synchronization run for account {AccountId} finished in {RunDuration}; {FolderCount} folders were scheduled and {FailedFolderCount} of them failed.")]
    private partial void LogAccountRunFinished(
        string accountId,
        int folderCount,
        int failedFolderCount,
        TimeSpan runDuration);

    /// <summary>States the wait as well as the failure count, because the wait is what an operator watching a recovering server needs to expect.</summary>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Account {AccountId} has failed {ConsecutiveFailureCount} runs in a row; the next run is deferred by {DelayBeforeNextRun} and the configured interval returns after a run succeeds.")]
    private partial void LogNextRunBackedOff(
        string accountId,
        int consecutiveFailureCount,
        TimeSpan delayBeforeNextRun);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Account {AccountId} is no longer configured, so its supervision ended; the mail already stored for it stays readable.")]
    private partial void LogAccountNoLongerConfigured(string accountId);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Supervision of account {AccountId} stopped because the host is shutting down.")]
    private partial void LogSupervisionStopped(string accountId);

    /// <summary>Separates a supervisor that ended unexpectedly from one that was asked to stop, because only the first is a defect.</summary>
    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Supervision of account {AccountId} ended unexpectedly; it is started again on the next supervision interval and no other account is affected.")]
    private partial void LogSupervisionFailed(Exception exception, string accountId);

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
        Message = "IMAP synchronization failed for {AccountId}/{FolderAlias}; the account's remaining folders continue and its next run is backed off.")]
    private partial void LogFolderSynchronizationFailed(
        Exception exception,
        string accountId,
        string folderAlias);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Deferred IMAP folder synchronization for {AccountId}/{FolderAlias} after an unresolved optimistic concurrency conflict; the next run will retry from the persisted checkpoint.")]
    private partial void LogFolderSynchronizationDeferredAfterConcurrencyConflict(
        Exception exception,
        string accountId,
        string folderAlias);

    /// <summary>Separates a mail server that is refusing work from a host that is shutting down, which cancellation already reports.</summary>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Deferred IMAP folder synchronization for {AccountId}/{FolderAlias} because the mail server did not serve it within its configured resilience budget; the next run will retry from the persisted checkpoint.")]
    private partial void LogFolderSynchronizationDeferredAfterMailServerUnavailable(
        Exception exception,
        string accountId,
        string folderAlias);
}
