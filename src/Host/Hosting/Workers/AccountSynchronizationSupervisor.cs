// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Application.Synchronization.Reconciliation;
using MailFathom.Application.Synchronization.Sessions;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Mail;

namespace MailFathom.Host.Hosting.Workers;

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
    private readonly AccountPushNotificationWatch pushNotifications;
    private readonly ILogger<AccountSynchronizationSupervisor> logger;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes a supervisor for one configured account.</summary>
    /// <param name="accountId">The account this supervisor synchronizes and names in every line it logs.</param>
    /// <param name="scopeFactory">Creates the scope each folder work unit runs in.</param>
    /// <param name="settings">Supplies the snapshot every run is scheduled from.</param>
    /// <param name="accountRunSlots">Bounds how many accounts run at once; owned by the coordinator and never released beyond what this supervisor took.</param>
    /// <param name="pushNotifications">Ends the wait between runs early when a watched folder changes; owned by this supervisor and disposed with it.</param>
    /// <param name="logger">Records run outcomes, which carry account and folder aliases and no message-level data.</param>
    /// <param name="timeProvider">Measures run duration and the wait between runs.</param>
    public AccountSynchronizationSupervisor(
        MailAccountId accountId,
        IServiceScopeFactory scopeFactory,
        ISettingsSnapshot<MailSynchronizationOptions> settings,
        SemaphoreSlim accountRunSlots,
        AccountPushNotificationWatch pushNotifications,
        ILogger<AccountSynchronizationSupervisor> logger,
        TimeProvider timeProvider)
    {
        this.accountId = accountId;
        this.scopeFactory = scopeFactory;
        this.settings = settings;
        this.accountRunSlots = accountRunSlots;
        this.pushNotifications = pushNotifications;
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
        finally
        {
            // The push sessions are this supervisor's, not the account's: the coordinator answers a supervisor that
            // ended by starting a new one, and a connection left open by the previous one would be a connection nothing
            // is left to close.
            await this.pushNotifications.DisposeAsync();
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

            var run = await this.RunOnceAsync(runSettings, account, schedulingToken, workUnitToken);

            consecutiveFailureCount = run.Failed ? consecutiveFailureCount + 1 : 0;

            var delayBeforeNextRun = SynchronizationRunBackoff.DelayBeforeNextRun(
                runSettings.Interval,
                runSettings.MaxFailureBackoff,
                consecutiveFailureCount);

            if (consecutiveFailureCount > 0)
            {
                this.LogNextRunBackedOff(this.accountId.Value, consecutiveFailureCount, delayBeforeNextRun);
            }

            // Push changes what ends the wait and nothing about how long it would otherwise be, so backoff is computed
            // first and then handed over. An account that is backing off a failing server keeps that whole delay unless
            // the server itself reports a change, which is the one event that proves it is answering again.
            await this.pushNotifications.WatchResolvedFoldersAsync(
                runSettings,
                account,
                run.ResolvedFolders,
                schedulingToken);

            await this.pushNotifications.WaitForNextPassAsync(runSettings, delayBeforeNextRun, schedulingToken);
        }
    }

    /// <summary>Runs every configured folder of the account once, bounded by the configured folder concurrency.</summary>
    /// <returns>Whether the run failed, and the bindings its folders resolved to.</returns>
    /// <remarks>
    /// <para>
    /// An alias that matched no advertised folder, or several, is not counted as a failure. It is a configuration
    /// mistake whose remedy is an edit rather than a wait, and backing the account off for it would slow every folder
    /// that is working.
    /// </para>
    /// <para>
    /// The folder bound admits folders one at a time, so most of a run's folders are still queued when it starts. A
    /// folder that has not begun when shutdown does is therefore skipped rather than started: the drain exists to let
    /// work already in flight finish, and opening a new mailbox session inside it would create exactly the work the
    /// drain then has to cut off. The two tokens divide that decision — the scheduling token stops admitting folders,
    /// the work-unit token is what a folder already running is eventually cancelled by.
    /// </para>
    /// </remarks>
    private async Task<AccountRunOutcome> RunOnceAsync(
        MailSynchronizationOptions runSettings,
        MailSynchronizationAccountOptions account,
        CancellationToken schedulingToken,
        CancellationToken workUnitToken)
    {
        var scheduledFolders = account.EffectiveFolders;
        var resolvedFolders = new ConcurrentBag<MailFolderResolution>();
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
                    if (schedulingToken.IsCancellationRequested)
                    {
                        return;
                    }

                    var folderRun = await this.SynchronizeFolderAsync(
                        runSettings,
                        account.RemotelyDeletedEmailDisposition,
                        configuredFolder,
                        folderToken);

                    if (folderRun.ResolvedFolder is { } resolvedFolder)
                    {
                        resolvedFolders.Add(resolvedFolder);
                    }

                    if (!folderRun.Succeeded)
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

        return new AccountRunOutcome(failedFolderCount > 0, [.. resolvedFolders]);
    }

    /// <summary>Synchronizes one folder in a scope of its own and reports whether it completed and what it resolved to.</summary>
    /// <remarks>
    /// The configured folder is turned into a mapping inside the guarded body rather than while the run is scheduled,
    /// so a folder whose configuration reached the supervisor unusable fails that folder and not the account.
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The supervisor isolates an unexpected per-folder failure so the account's remaining folders and its later runs can continue.")]
    private async Task<FolderRunOutcome> SynchronizeFolderAsync(
        MailSynchronizationOptions runSettings,
        RemotelyDeletedEmailDisposition remotelyDeletedEmailDisposition,
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

            this.ReportFolderOutcome(folderAlias, remotelyDeletedEmailDisposition, result);

            return new FolderRunOutcome(Succeeded: true, result.Folder);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (PersistenceConcurrencyConflictException exception)
        {
            this.LogFolderSynchronizationDeferredAfterConcurrencyConflict(exception, this.accountId.Value, folderAlias);

            return FolderRunOutcome.Failed;
        }
        catch (MailboxUnavailableException exception)
        {
            this.LogFolderSynchronizationDeferredAfterMailServerUnavailable(exception, this.accountId.Value, folderAlias);

            return FolderRunOutcome.Failed;
        }
        catch (Exception exception)
        {
            this.LogFolderSynchronizationFailed(exception, this.accountId.Value, folderAlias);

            return FolderRunOutcome.Failed;
        }
    }

    /// <summary>Reports one folder's run, keeping an alias that named no single folder separate from a failure.</summary>
    private void ReportFolderOutcome(
        string folderAlias,
        RemotelyDeletedEmailDisposition remotelyDeletedEmailDisposition,
        MailboxSynchronizationResult result)
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

        if (result.RelocatedEmailCount > 0 || result.Reconciliation.OwnMutationCompletedEmailCount > 0)
        {
            this.LogOwnMutationsRecognized(
                this.accountId.Value,
                folderAlias,
                result.RelocatedEmailCount,
                result.Reconciliation.OwnMutationCompletedEmailCount);
        }

        this.ReportSuppressedChanges(folderAlias, result.SuppressedChanges);
        this.ReportReconciliation(folderAlias, remotelyDeletedEmailDisposition, result.Reconciliation);
    }

    /// <summary>Emits what the run withheld from rule evaluation, so a rule that appears not to have fired can be explained.</summary>
    /// <remarks>
    /// The count is stated once at information level and each withheld change once at debug, because the two answer
    /// different questions: whether MailFathom is acting on its own mailbox at all, and why one particular message did
    /// not set a rule off. Only identities MailFathom owns appear — a local email identifier, a record identifier, and
    /// the mutation's own name — never anything derived from the message.
    /// </remarks>
    private void ReportSuppressedChanges(string folderAlias, IReadOnlyList<SuppressedMailboxChange> suppressedChanges)
    {
        if (suppressedChanges.Count == 0)
        {
            return;
        }

        this.LogChangesSuppressed(this.accountId.Value, folderAlias, suppressedChanges.Count);

        foreach (var suppressed in suppressedChanges)
        {
            this.LogChangeSuppressed(
                this.accountId.Value,
                folderAlias,
                suppressed.Kind,
                suppressed.Mutation.Name,
                suppressed.StoredEmailId.Value,
                suppressed.MutationRecordId.Value);
        }
    }

    /// <summary>Emits the audit line for the backward pass, and emits it only when the pass found something to record.</summary>
    /// <remarks>
    /// A window that observed nothing says nothing, so a folder whose emails are all up to date does not repeat a line
    /// per interval forever. Local deletion is reported at warning level whichever disposition produced it, because an
    /// operator watching this is watching for mail leaving the local copy — and a misconfigured folder alias or a
    /// server rebuilding a mailbox shows up here first.
    /// </remarks>
    private void ReportReconciliation(
        string folderAlias,
        RemotelyDeletedEmailDisposition remotelyDeletedEmailDisposition,
        MailboxReconciliationResult reconciliation)
    {
        if (reconciliation.RemotelyDeletedEmailCount > 0)
        {
            this.LogRemotelyDeletedEmailsRecorded(
                this.accountId.Value,
                folderAlias,
                reconciliation.RemotelyDeletedEmailCount,
                remotelyDeletedEmailDisposition);
        }

        if (reconciliation.SeenStateChangedEmailCount > 0)
        {
            this.LogSeenStateChangesObserved(
                this.accountId.Value,
                folderAlias,
                reconciliation.SeenStateChangedEmailCount);
        }

        if (reconciliation.ObservedEmailCount > 0)
        {
            this.LogFolderReconciled(
                this.accountId.Value,
                folderAlias,
                reconciliation.ObservedEmailCount,
                reconciliation.EmailsRemain);
        }
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

    /// <summary>Reports the backward pass in counts only, and says whether the folder still has emails awaiting a check.</summary>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Reconciled IMAP folder {AccountId}/{FolderAlias}; refreshed the remote flags of {ObservedEmailCount} stored messages and has more to reconcile: {EmailsRemain}.")]
    private partial void LogFolderReconciled(
        string accountId,
        string folderAlias,
        int observedEmailCount,
        bool emailsRemain);

    /// <summary>Reports the changes MailFathom made to the mailbox arriving back through an ordinary run.</summary>
    /// <remarks>
    /// It is the line that says a message which moved is the same message. Without it an operator reading the counts
    /// would see mail arriving in one folder and vanishing from another, which is exactly what the join exists to stop
    /// the system itself from concluding. Counts and MailFathom's own configured names only, as everywhere else here.
    /// </remarks>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Recognized MailFathom's own changes in {AccountId}/{FolderAlias}; {RelocatedEmailCount} discovered messages kept the local email they were relocated from, and {OwnMutationCompletedEmailCount} source occurrences left the folder because MailFathom moved or deleted them rather than because somebody else did.")]
    private partial void LogOwnMutationsRecognized(
        string accountId,
        string folderAlias,
        int relocatedEmailCount,
        int ownMutationCompletedEmailCount);

    /// <summary>States how much of what the run discovered was MailFathom's own and was therefore not raised as a change to react to.</summary>
    /// <remarks>
    /// A folder that MailFathom never writes to never emits this line. Where it does appear, it is the difference
    /// between a rule engine that files a message once and one that files it every interval for as long as the mailbox
    /// is watched, so the count is worth an operator's attention rather than only a debugging session's.
    /// </remarks>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Withheld {SuppressedChangeCount} changes in {AccountId}/{FolderAlias} from rule evaluation, because a durable mutation record says MailFathom itself made them.")]
    private partial void LogChangesSuppressed(string accountId, string folderAlias, int suppressedChangeCount);

    /// <summary>Names one withheld change and the record that accounted for it, which is what a support question about a rule that did not fire is answered from.</summary>
    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Withheld a {MailboxChangeKind} in {AccountId}/{FolderAlias} for stored email {StoredEmailId}; the {Mutation} recorded as {MutationRecordId} accounts for it.")]
    private partial void LogChangeSuppressed(
        string accountId,
        string folderAlias,
        MailboxChangeKind mailboxChangeKind,
        string mutation,
        Guid storedEmailId,
        Guid mutationRecordId);

    /// <summary>Reports the flag changes the mailbox owner made, which stay changes to react to however many of MailFathom's own were withheld beside them.</summary>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Mail server reports a moved \\Seen flag on {SeenStateChangedEmailCount} messages stored for {AccountId}/{FolderAlias} that no change of MailFathom's accounts for.")]
    private partial void LogSeenStateChangesObserved(
        string accountId,
        string folderAlias,
        int seenStateChangedEmailCount);

    /// <summary>Records mail leaving the local copy, which is the one reconciliation outcome an operator has to be able to find afterwards.</summary>
    /// <remarks>
    /// The disposition is part of the line rather than left to the reader to look up in configuration, because the two
    /// outcomes it names are not comparable: one hides a row, the other destroys it along with everything derived from
    /// it. Only counts and MailFathom's own configured names appear here; nothing derived from a message may.
    /// </remarks>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Mail server no longer holds {RemotelyDeletedEmailCount} messages stored for {AccountId}/{FolderAlias}; their local copies were handled as {RemotelyDeletedEmailDisposition}.")]
    private partial void LogRemotelyDeletedEmailsRecorded(
        string accountId,
        string folderAlias,
        int remotelyDeletedEmailCount,
        RemotelyDeletedEmailDisposition remotelyDeletedEmailDisposition);

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

    /// <summary>States what one account run produced for the two decisions that follow it.</summary>
    /// <param name="Failed">Whether at least one folder failed, which is what puts the account into backoff.</param>
    /// <param name="ResolvedFolders">
    /// The bindings the run's folders resolved to, which are the remote folders a push session may watch until the next
    /// run resolves them again.
    /// </param>
    private readonly record struct AccountRunOutcome(bool Failed, IReadOnlyList<MailFolderResolution> ResolvedFolders);

    /// <summary>States what one folder's turn through a run produced.</summary>
    /// <param name="Succeeded">Whether the folder completed, which excludes a deferral and an unexpected failure alike.</param>
    /// <param name="ResolvedFolder">The binding the folder ran under, or <see langword="null" /> when the alias resolved to none.</param>
    private readonly record struct FolderRunOutcome(bool Succeeded, MailFolderResolution? ResolvedFolder)
    {
        /// <summary>Gets the outcome of a folder that neither completed nor left a binding worth watching.</summary>
        internal static FolderRunOutcome Failed => new(Succeeded: false, ResolvedFolder: null);
    }
}
