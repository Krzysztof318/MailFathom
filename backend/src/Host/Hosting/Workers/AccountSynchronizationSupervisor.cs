// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Application.Mail.Mutations.Audit;
using MailFathom.Application.Mail.Mutations.Convergence;
using MailFathom.Application.Notifications;
using MailFathom.Application.Persistence;
using MailFathom.Application.Retrieval.AskMail.Audit;
using MailFathom.Application.Rules.Evaluation;
using MailFathom.Application.Rules.History;
using MailFathom.Application.Spam.Runs;
using MailFathom.Application.Synchronization;
using MailFathom.Application.Synchronization.Administration;
using MailFathom.Application.Synchronization.Reconciliation;
using MailFathom.Application.Synchronization.Sessions;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Infrastructure.Observability;

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
    private readonly MailAccountIdentity account;
    private readonly IServiceScopeFactory scopeFactory;
    private readonly ISettingsSnapshot<MailSynchronizationOptions> settings;
    private readonly SemaphoreSlim accountRunSlots;
    private readonly AccountPushNotificationWatch pushNotifications;
    private readonly MailSynchronizationTelemetry telemetry;
    private readonly MailSynchronizationRunLedger runLedger;
    private readonly ILogger<AccountSynchronizationSupervisor> logger;

    /// <summary>Initializes a supervisor for one configured account.</summary>
    /// <param name="account">The account this supervisor synchronizes, named by its owner and its identifier together; the identifier is what it logs.</param>
    /// <param name="scopeFactory">Creates the scope each folder work unit runs in.</param>
    /// <param name="settings">Supplies the snapshot every run is scheduled from.</param>
    /// <param name="accountRunSlots">Bounds how many accounts run at once; owned by the coordinator and never released beyond what this supervisor took.</param>
    /// <param name="pushNotifications">Ends the wait between runs early when a watched folder changes; owned by this supervisor and disposed with it.</param>
    /// <param name="telemetry">Publishes the run as a span with its folders beneath it, and the counts and waits an operator reads without opening a log; it also measures how long a run took.</param>
    /// <param name="runLedger">Holds what this supervisor is doing for the administrative surface, which is what an operator without a metrics stack reads it from.</param>
    /// <param name="logger">Records run outcomes, which carry account and folder aliases and no message-level data.</param>
    public AccountSynchronizationSupervisor(
        MailAccountIdentity account,
        IServiceScopeFactory scopeFactory,
        ISettingsSnapshot<MailSynchronizationOptions> settings,
        SemaphoreSlim accountRunSlots,
        AccountPushNotificationWatch pushNotifications,
        MailSynchronizationTelemetry telemetry,
        MailSynchronizationRunLedger runLedger,
        ILogger<AccountSynchronizationSupervisor> logger)
    {
        this.account = account;
        this.scopeFactory = scopeFactory;
        this.settings = settings;
        this.accountRunSlots = accountRunSlots;
        this.pushNotifications = pushNotifications;
        this.telemetry = telemetry;
        this.runLedger = runLedger;
        this.logger = logger;
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
            this.LogSupervisionStopped(this.account.Id.Value);
        }
        catch (Exception exception)
        {
            this.LogSupervisionFailed(exception, this.account.Id.Value);
        }
        finally
        {
            // The push sessions are this supervisor's, not the account's: the coordinator answers a supervisor that
            // ended by starting a new one, and a connection left open by the previous one would be a connection nothing
            // is left to close.
            await this.pushNotifications.DisposeAsync();

            // The schedule goes with them, for the same reason: an account nothing is scheduling any more would
            // otherwise publish the wait it was last scheduled behind for the life of the process.
            this.telemetry.RecordSupervisionEnded(this.account.Id);
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
            var account = runSettings.FindConfiguredAccount(this.account.Id);

            if (account is null)
            {
                this.LogAccountNoLongerConfigured(this.account.Id.Value);

                return;
            }

            var run = await this.RunOnceAsync(runSettings, account, schedulingToken, workUnitToken);

            consecutiveFailureCount = run.Failed ? consecutiveFailureCount + 1 : 0;

            var delayBeforeNextRun = SynchronizationRunBackoff.DelayBeforeNextRun(
                runSettings.Interval,
                runSettings.MaxFailureBackoff,
                consecutiveFailureCount);

            // Published on every pass rather than only on a backed-off one, because a gauge that stops being written
            // holds its last value: an account that recovered would go on reporting the wait it was backing off by.
            this.telemetry.RecordScheduledDelay(this.account.Id, delayBeforeNextRun, consecutiveFailureCount);
            this.runLedger.RecordNextRunDue(this.account.Id, delayBeforeNextRun, consecutiveFailureCount);

            if (consecutiveFailureCount > 0)
            {
                this.LogNextRunBackedOff(this.account.Id.Value, consecutiveFailureCount, delayBeforeNextRun);
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
        // A folder the operator stopped mirroring is not scheduled at all, which is what makes "no connection is opened
        // for it" true rather than merely quiet: the mapping goes on naming the folder for anything that writes into it,
        // and this run neither discovers nor selects it. It is also the whole of what the run does about such a folder —
        // what it already stored stays where it is, and erasing that is a command an operator runs.
        var scheduledFolders = account.EffectiveFolders
            .Where(static folder => folder.Participation.IsSynchronized)
            .ToArray();
        var resolvedFolders = new ConcurrentBag<MailFolderResolution>();
        var failedFolderCount = 0;
        var arrivedEmailCount = 0;
        var credentialRefused = false;
        var convergenceFailed = false;

        // The wait for a slot is counted and the run itself is spanned, which is the split that keeps a cycle's
        // duration the cycle rather than the cycle plus however long the accounts in front of it took.
        using (this.telemetry.EnterRunQueue())
        {
            this.runLedger.RecordRunQueued(this.account.Id);

            await this.accountRunSlots.WaitAsync(schedulingToken);
        }

        using var run = this.telemetry.BeginAccountRun(this.account.Id);

        this.runLedger.RecordRunStarted(this.account.Id);

        try
        {
            // Convergence comes before the folders, and inside the account's slot, for two reasons that point the same
            // way. A change the previous process left half-made is finished before this run reads the mailbox, so the
            // run observes a mailbox that has stopped moving; and the account's one write connection is taken and given
            // back before the folder connections are opened, rather than beside them.
            if (!schedulingToken.IsCancellationRequested)
            {
                convergenceFailed = await this.ConvergeOutstandingMutationsAsync(runSettings, workUnitToken);
            }

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

                    if (folderRun.StoredEmailCount > 0)
                    {
                        Interlocked.Add(ref arrivedEmailCount, folderRun.StoredEmailCount);
                    }

                    // A plain write rather than an interlocked one: every writer writes the same value, and awaiting
                    // the whole loop is what publishes it to the read below.
                    if (folderRun.CredentialRefused)
                    {
                        credentialRefused = true;
                    }

                    if (!folderRun.Succeeded)
                    {
                        Interlocked.Increment(ref failedFolderCount);
                    }
                });

            if (!schedulingToken.IsCancellationRequested)
            {
                await this.DeliverOutstandingMailAsync(workUnitToken);
                await this.EraseExpiredDerivedRecordsAsync(runSettings, workUnitToken);
                await this.ClassifyRequestedMailAsync(runSettings, workUnitToken);
                await this.EvaluateMailRulesAsync(runSettings, workUnitToken);
                await this.CutPassagesOfEvaluatedMailAsync(runSettings, workUnitToken);
                await this.ReportRunToItsOwnerAsync(
                    runSettings,
                    scheduledFolders.Length,
                    failedFolderCount,
                    arrivedEmailCount,
                    credentialRefused,
                    workUnitToken);
            }
        }
        finally
        {
            this.accountRunSlots.Release();
        }

        // A cycle the host stopped scheduling did not finish, and its failure count says nothing about that: a folder
        // still queued behind the folder bound returns without being started, so it raises no count and would leave
        // a cycle that skipped most of its work reporting a clean run. Leaving the scope unreported publishes it as
        // interrupted, which is what a folder cut off by the same shutdown already reports, and the line is withheld
        // for the same reason — the supervisor logs the stop itself, and there is no finished run to announce.
        if (schedulingToken.IsCancellationRequested)
        {
            return new AccountRunOutcome(failedFolderCount > 0 || convergenceFailed, [.. resolvedFolders]);
        }

        run.Completed(scheduledFolders.Length, failedFolderCount, convergenceFailed);

        // Recorded on the same condition the span is, and for the same reason: a cycle shutdown stopped scheduling did
        // not finish, so publishing its counts would leave an operator reading a run that skipped most of its folders as
        // the account's last word on itself. The previous finished run stays the one reported instead.
        this.runLedger.RecordRunEnded(
            this.account.Id,
            scheduledFolders.Length,
            failedFolderCount,
            convergenceFailed);

        this.LogAccountRunFinished(
            this.account.Id.Value,
            scheduledFolders.Length,
            failedFolderCount,
            run.Elapsed);

        return new AccountRunOutcome(failedFolderCount > 0 || convergenceFailed, [.. resolvedFolders]);
    }

    /// <summary>Finishes or gives up on the changes this account asked a mail server for and has not seen completed.</summary>
    /// <returns><see langword="true" /> when at least one change failed, which puts the account into backoff.</returns>
    /// <remarks>
    /// <para>
    /// This is what makes a change survive a restart without anybody noticing it had to. The record was written before
    /// the first IMAP command, so a process that stopped halfway through a filing left a statement of what it was doing
    /// and how far it got, and the first run after the restart reads that statement and carries it the rest of the way.
    /// Nothing here has to be scheduled separately, because an account already has a loop that runs it.
    /// </para>
    /// <para>
    /// A failed pass fails the run rather than being logged and passed over, which is the whole of the backoff story: an
    /// unreachable mail server defers the account's next run through the same jittered delay a failed folder does, so a
    /// change waiting on that server is approached less often instead of once per interval forever. What bounds the
    /// change itself is its own attempt count, which is written before each attempt and therefore survives a crash.
    /// </para>
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A convergence pass that ended unexpectedly defers this account's next run and leaves its folders to synchronize; every mutation's own record already carries how far it got.")]
    private async Task<bool> ConvergeOutstandingMutationsAsync(
        MailSynchronizationOptions runSettings,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = this.scopeFactory.CreateScope();

            scope.ServiceProvider.GetRequiredService<ScopedMailSynchronizationSettings>().UseRunSnapshot(runSettings);

            var converger = scope.ServiceProvider.GetRequiredService<MailboxMutationConverger>();
            var report = await converger.ConvergeAsync(this.account, cancellationToken);

            scope.ServiceProvider.GetRequiredService<MailboxConvergenceTelemetry>().Report(this.account.Id, report);

            return report.FailedCount > 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            this.LogMutationConvergenceFailed(exception, this.account.Id.Value);

            return true;
        }
    }

    /// <summary>Delivers whatever this account has been asked to send and has not seen leave.</summary>
    /// <remarks>
    /// <para>
    /// This is what makes the outbox correct rather than merely quick. A message written down is signalled to the
    /// delivery loop as soon as it is durable, but a signal is an in-process message: an instance that stopped between
    /// the write and the pass, a queue that was full, or a loop that failed all leave a send nobody is coming back for.
    /// The account already has a loop that comes back, so this is where it is picked up — the same pass, reached a
    /// second way.
    /// </para>
    /// <para>
    /// A failure never fails the run, and neither does a send that will be attempted again. The submission endpoint is
    /// a different server from the one the folders are read over, so a provider that will not accept mail says nothing
    /// about whether this account's mail can be fetched — and putting the account into backoff over it would answer an
    /// unreachable SMTP server by reading IMAP less often. What paces the retry instead is the send's own backoff,
    /// which is written onto its record.
    /// </para>
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A delivery pass that ended unexpectedly must not stop the account's synchronization: each send's own record already carries how far it got, and the next run claims again.")]
    private async Task DeliverOutstandingMailAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = this.scopeFactory.CreateScope();

            var pass = scope.ServiceProvider.GetRequiredService<MailOutboxPass>();
            var report = await pass.RunAsync(this.account, cancellationToken);

            scope.ServiceProvider.GetRequiredService<MailDeliveryTelemetry>().Report(this.account.Id, report);

            if (report.Results.Count > 0 || report.MarkedUnknownCount > 0)
            {
                this.LogOutboxDrained(
                    this.account.Id.Value,
                    report.SentCount,
                    report.RefusedCount,
                    report.DeferredCount,
                    report.UnknownOutcomeCount + report.MarkedUnknownCount);
            }

            this.ReportOutcomesNeedingAPerson(report);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            this.LogOutboxDrainFailed(exception, this.account.Id.Value);
        }
    }

    /// <summary>Says at error level what a pass produced that waits for somebody rather than for another attempt.</summary>
    /// <remarks>
    /// The summary line above is one line about a pass, and an operator alerts on a level rather than on a count inside
    /// a sentence. This path settles a pass as often as the signalled worker does, so an ending that reaches a person
    /// there has to reach them here too; anything else would make which of the two happened to claim the send decide
    /// whether anybody hears about it.
    /// </remarks>
    private void ReportOutcomesNeedingAPerson(MailOutboxPassReport report)
    {
        var unknownCount = report.UnknownOutcomeCount + report.MarkedUnknownCount;
        if (unknownCount > 0)
        {
            this.LogOutboxOutcomesUnknown(this.account.Id.Value, unknownCount);
        }

        if (report.RefusedCount > 0)
        {
            this.LogOutboxSendsRefused(this.account.Id.Value, report.RefusedCount);
        }

        if (report.NotRecordedCount > 0)
        {
            this.LogOutboxOutcomesNotRecorded(this.account.Id.Value, report.NotRecordedCount);
        }

        // A copy that is not where it should be is a warning rather than an error, because nobody is missing a message
        // over it: the mail was delivered, and what is lost is the owner seeing it in their own client. Nothing sends
        // anything again over one, and nothing files it again either — a settled send is claimed by nothing.
        if (report.NotFiledCount > 0)
        {
            this.LogOutboxCopiesNotFiled(this.account.Id.Value, report.NotFiledCount);
        }
    }

    /// <summary>Erases whatever in this account's four derived records has outlived the window it is held to.</summary>
    /// <remarks>
    /// <para>
    /// All four age out here — the trail of the changes MailFathom made to the mailbox, the record of the questions
    /// answered from it, the history of what the rules concluded about its mail, and what its owner has been told
    /// about any of it. The first three are separate operator decisions with separate windows and the last is the
    /// record's own bound, and they are one pass because the pass is what the account's own loop already provides: a
    /// second schedule would be another thing to configure and watch for work that is four bounded deletes.
    /// </para>
    /// <para>
    /// The notifications are the owner's rather than the account's, so an owner holding several accounts is swept once
    /// per account. That costs a query that erases nothing rather than a mechanism of its own, which is the cheaper of
    /// the two.
    /// </para>
    /// <para>
    /// It rides the account's own run for the reason convergence does, and runs after the folders rather than before
    /// them, because holding data a day longer than the window is a smaller wrong than delaying the mail this run
    /// exists to fetch.
    /// </para>
    /// <para>
    /// A failure never fails the run. Retention is a storage-limitation obligation rather than a mail operation, and
    /// putting an account into backoff — which is to say fetching its mail less often — because a delete did not run
    /// would answer the wrong problem with the wrong remedy. The next run erases what this one did not, including the
    /// second record where a failure in the first stopped this pass from reaching it.
    /// </para>
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Retention is not a mail operation; an erasure that failed is logged and repeated by the next run rather than putting the account into backoff.")]
    private async Task EraseExpiredDerivedRecordsAsync(
        MailSynchronizationOptions runSettings,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = this.scopeFactory.CreateScope();

            scope.ServiceProvider.GetRequiredService<ScopedMailSynchronizationSettings>().UseRunSnapshot(runSettings);

            var erasedCount = await scope.ServiceProvider
                .GetRequiredService<MailboxMutationAuditTrailRetention>()
                .EraseExpiredAsync(this.account, cancellationToken);

            if (erasedCount > 0)
            {
                this.LogAuditEntriesErased(this.account.Id.Value, erasedCount);
            }

            var erasedAnsweringCount = await scope.ServiceProvider
                .GetRequiredService<MailAnsweringAuditTrailRetention>()
                .EraseExpiredAsync(this.account, cancellationToken);

            if (erasedAnsweringCount > 0)
            {
                this.LogAnsweringAuditEntriesErased(this.account.Id.Value, erasedAnsweringCount);
            }

            var erasedExecutionCount = await scope.ServiceProvider
                .GetRequiredService<MailRuleHistoryRetention>()
                .EraseExpiredAsync(this.account, cancellationToken);

            if (erasedExecutionCount > 0)
            {
                this.LogRuleExecutionsErased(this.account.Id.Value, erasedExecutionCount);
            }

            var erasedNotificationCount = await scope.ServiceProvider
                .GetRequiredService<NotificationRetention>()
                .EraseExpiredAsync(this.account.Owner, cancellationToken);

            if (erasedNotificationCount > 0)
            {
                this.LogNotificationsErased(this.account.Id.Value, erasedNotificationCount);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            this.LogDerivedRecordRetentionFailed(exception, this.account.Id.Value);
        }
    }

    /// <summary>Tells this account's owner what the run observed that they were not at the screen for.</summary>
    /// <remarks>
    /// <para>
    /// Last, and after every pass that can still commit mail, so the count reported is the run's whole arrival rather
    /// than whatever had landed when the report was composed. It is one notification for the run and never one per
    /// message, which is the record's own rule rather than this worker's choice: a run that commits forty messages is
    /// one arrival to somebody who was away.
    /// </para>
    /// <para>
    /// A failure never fails the run. A notification is a report about work that is already committed, so backing the
    /// account off — which is to say fetching its mail less often — because a report could not be written would answer
    /// the wrong problem with the wrong remedy. What the next run says is what that run observed; nothing here is
    /// replayed, because a stale count is worse than a missing one.
    /// </para>
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Reporting a run to its owner is not a mail operation; a report that failed is logged rather than putting the account into backoff.")]
    private async Task ReportRunToItsOwnerAsync(
        MailSynchronizationOptions runSettings,
        int scheduledFolderCount,
        int failedFolderCount,
        int arrivedEmailCount,
        bool credentialRefused,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = this.scopeFactory.CreateScope();

            scope.ServiceProvider.GetRequiredService<ScopedMailSynchronizationSettings>().UseRunSnapshot(runSettings);

            var notifications = scope.ServiceProvider.GetRequiredService<SynchronizationNotifications>();

            await notifications.ReportArrivedMailAsync(this.account, arrivedEmailCount, cancellationToken);

            // The two system conditions are reported separately rather than as one worst outcome, because they ask the
            // person for different things: a refused credential is theirs to repair, and an incomplete run is theirs
            // to know about while MailFathom keeps trying.
            if (credentialRefused)
            {
                await notifications.ReportRefusedCredentialAsync(this.account, cancellationToken);
            }

            await notifications.ReportIncompleteRunAsync(
                this.account,
                failedFolderCount,
                scheduledFolderCount,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            this.LogOwnerNotificationFailed(exception, this.account.Id.Value);
        }
    }

    /// <summary>Carries this account's whole-mailbox classification run, where somebody asked for one.</summary>
    /// <remarks>
    /// <para>
    /// Before the rules and after everything else, which is the order the feature's own framing gives: junk ends a
    /// message's journey through automation, so a message this pass files as junk is one the rule pass beside it should
    /// not also be filing somewhere else. It runs after the folders for the reason the rule pass does — every message it
    /// can reach was committed by a folder that has already finished, in a scope and a transaction of its own.
    /// </para>
    /// <para>
    /// A failure never fails the run. What the pass reads about the mail is already stored, and the two things it can
    /// reach the network for — a scanner sidecar and, where the run acts, the folder a filing names — are both bounded by
    /// their own callers. An unreachable mail server has already put the account into backoff by the time this step
    /// begins wherever the run synchronized or converged anything, so backing it off again here would slow the remote
    /// work over a local problem or over the same remote one twice. What a pass did not finish, the next run resumes from
    /// the batches this one committed.
    /// </para>
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A pass that failed is logged and resumed by the next run rather than putting the account into backoff; the remarks hold why that stays right for the remote steps it can take.")]
    private async Task ClassifyRequestedMailAsync(
        MailSynchronizationOptions runSettings,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = this.scopeFactory.CreateScope();

            scope.ServiceProvider.GetRequiredService<ScopedMailSynchronizationSettings>().UseRunSnapshot(runSettings);

            var report = await scope.ServiceProvider
                .GetRequiredService<SpamClassificationPass>()
                .RunAsync(this.account, cancellationToken);

            this.ReportSpamClassification(report);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            this.LogSpamClassificationFailed(exception, this.account.Id.Value);
        }
    }

    /// <summary>Emits what the classification run did, in counts alone; nothing derived from a message may appear here.</summary>
    /// <remarks>
    /// An account with no run outstanding says nothing, which is what keeps a deployment that has never asked for one
    /// from repeating a line per interval forever. The posture is part of the line because the same counts mean two
    /// different things under it: a dry run reporting mail it acted on is reporting mail it would have acted on.
    /// </remarks>
    private void ReportSpamClassification(SpamClassificationRunReport report)
    {
        if (report.Walk is not { } walk)
        {
            return;
        }

        var profile = report.Profile.ToString();

        if (!walk.IsEmpty)
        {
            this.LogSpamClassificationProgressed(
                this.account.Id.Value,
                profile,
                walk.ClassifiedEmailCount,
                walk.SkippedEmailCount,
                walk.SpamEmailCount,
                walk.UnclassifiableEmailCount,
                walk.ActedEmailCount,
                walk.EmailsRemain);
        }

        switch (report.Ending)
        {
            case SpamClassificationRunEnding.Completed:
                this.LogSpamClassificationCompleted(this.account.Id.Value, profile);

                break;
            case SpamClassificationRunEnding.Superseded:
                this.LogSpamClassificationSuperseded(this.account.Id.Value);

                break;
            case SpamClassificationRunEnding.Disabled:
                this.LogSpamClassificationDisabled(this.account.Id.Value);

                break;
            default:
                break;
        }
    }

    /// <summary>Runs this account's rules over the mail that has arrived, and over its whole mailbox where one was asked for.</summary>
    /// <remarks>
    /// <para>
    /// After the folders rather than beside them, and in front of the cut, which is what makes "evaluation
    /// never runs inside the synchronization transaction" true rather than merely intended: every message it can reach
    /// was committed by a folder that has already finished, in a scope and a transaction of its own. Mail a folder the
    /// operator stopped mirroring still holds is out of reach here whichever order the steps ran in, because the pass
    /// reads no candidate from such a folder.
    /// </para>
    /// <para>
    /// A failure never fails the run. Everything a pass reads about the mail itself was already stored, and the one
    /// thing it does ask a mail server — where a folder the account maps and does not mirror currently is — it asks
    /// only where a rule files into such a folder. Wherever this run has already converged a change or synchronized a
    /// folder, that server has been reached, so an unreachable one put the account into backoff before this step
    /// began, and backing it off again here would slow the remote work over a local problem or over the same remote
    /// one twice. An account that mirrors nothing and had nothing to converge reaches it here first instead, and a
    /// lookup that fails there leaves the account on its ordinary interval: that lookup is the whole of such a run's
    /// remote work, so backoff would slow nothing else, and the interval already spaces what is left to retry. What a
    /// pass did not finish, the next run resumes from the batches this one committed.
    /// </para>
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A pass that failed is logged and resumed by the next run rather than putting the account into backoff; the remarks hold why that stays right for the one remote step it takes.")]
    private async Task EvaluateMailRulesAsync(
        MailSynchronizationOptions runSettings,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = this.scopeFactory.CreateScope();

            scope.ServiceProvider.GetRequiredService<ScopedMailSynchronizationSettings>().UseRunSnapshot(runSettings);

            var report = await scope.ServiceProvider
                .GetRequiredService<MailRuleEvaluationPass>()
                .RunAsync(this.account, cancellationToken);

            this.ReportRuleEvaluation(report);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            this.LogMailRuleEvaluationFailed(exception, this.account.Id.Value);
        }
    }

    /// <summary>Cuts the passages of the mail the stages in front of this one have finished with, and offers each message for embedding.</summary>
    /// <remarks>
    /// <para>
    /// Last of the run's local passes, which is the ordering the arrival pipeline is built on rather than an arbitrary
    /// place to put it. A message may be withheld by the classification pass above and may be moved by the rule pass
    /// above that, and passages cut before either had its turn are passages of a placement and a verdict that had not
    /// been settled — so the cut waits for both, and the message is offered to the embedding worker only once its
    /// passages are durable.
    /// </para>
    /// <para>
    /// A failure never fails the run, for the reason the two passes above it do not: nothing here reaches a mail server,
    /// what it reads is already stored, and what a pass did not cut stays outstanding for the next run and for the
    /// embedding sweep, which selects on exactly the same condition.
    /// </para>
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A local cut that failed is logged and repeated by the next run rather than putting the account into backoff; the embedding sweep selects on the same outstanding condition.")]
    private async Task CutPassagesOfEvaluatedMailAsync(
        MailSynchronizationOptions runSettings,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = this.scopeFactory.CreateScope();

            scope.ServiceProvider.GetRequiredService<ScopedMailSynchronizationSettings>().UseRunSnapshot(runSettings);

            var report = await scope.ServiceProvider
                .GetRequiredService<MailChunkingPass>()
                .RunAsync(this.account, cancellationToken);

            if (!report.IsEmpty)
            {
                this.LogPassagesCut(
                    this.account.Id.Value,
                    report.ChunkedEmailCount,
                    report.RefusedOfferCount,
                    report.EmailsRemain);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            this.LogPassageCutFailed(exception, this.account.Id.Value);
        }
    }

    /// <summary>Emits what the rules did, in counts and rule names; nothing derived from a message may appear here.</summary>
    /// <remarks>
    /// A pass that found nothing says nothing, so an account whose mail is all evaluated does not repeat a line per
    /// interval forever. A rule's name is the one part of a rule that can be promised to carry no address somebody
    /// typed, which is what makes naming the rules that matched safe as well as useful.
    /// </remarks>
    private void ReportRuleEvaluation(MailRuleEvaluationReport report)
    {
        if (!report.Arrivals.IsEmpty)
        {
            var matchedRuleNames = NameList(report.Arrivals.MatchedRuleNames);

            this.LogArrivedMailEvaluated(
                this.account.Id.Value,
                report.Revision.Value,
                report.Arrivals.EvaluatedEmailCount,
                report.Arrivals.MatchedEmailCount,
                report.Arrivals.SkippedEmailCount,
                matchedRuleNames,
                report.Arrivals.EmailsRemain);
        }

        this.ReportFailedRules(report.Arrivals);
        this.ReportRuleActions(report.Arrivals);

        if (report.RequestedRun is { } requestedRun)
        {
            if (!requestedRun.IsEmpty)
            {
                var matchedRuleNames = NameList(requestedRun.MatchedRuleNames);

                this.LogRequestedRunProgressed(
                    this.account.Id.Value,
                    requestedRun.EvaluatedEmailCount,
                    requestedRun.MatchedEmailCount,
                    requestedRun.SkippedEmailCount,
                    matchedRuleNames,
                    requestedRun.EmailsRemain);
            }

            this.ReportFailedRules(requestedRun);
            this.ReportRuleActions(requestedRun);
        }

        switch (report.RequestedRunEnding)
        {
            case MailRuleEvaluationRunEnding.Completed:
                this.LogRequestedRunCompleted(this.account.Id.Value, report.Revision.Value);

                break;
            case MailRuleEvaluationRunEnding.Superseded:
                this.LogRequestedRunSuperseded(this.account.Id.Value);

                break;
            default:
                break;
        }
    }

    private void ReportFailedRules(MailRuleEvaluationWalk walk)
    {
        if (walk.FailedRuleCount == 0)
        {
            return;
        }

        var failedRuleNames = NameList(walk.FailedRuleNames);

        this.LogRuleEvaluationsFailed(
            this.account.Id.Value,
            walk.FailedRuleCount,
            walk.TimedOutRuleCount,
            failedRuleNames);
    }

    /// <summary>States what the walk asked the mailbox to change, and separately what it declined to ask for.</summary>
    /// <remarks>
    /// A change is a request written down rather than an IMAP operation, so this line says what the account's next
    /// convergence pass has to carry. What was not asked for is a second line at warning level, because an action a
    /// rule declared and the mailbox never received is the case an operator has to act on.
    /// </remarks>
    private void ReportRuleActions(MailRuleEvaluationWalk walk)
    {
        if (walk.RequestedActionCount > 0)
        {
            this.LogRuleActionsRequested(this.account.Id.Value, walk.RequestedActionCount);
        }

        if (walk.WithheldActionCount == 0 && walk.FailedActionCount == 0)
        {
            return;
        }

        this.LogRuleActionsUnapplied(
            this.account.Id.Value,
            walk.WithheldActionCount,
            walk.FailedActionCount,
            NameList(walk.UnappliedActionRuleNames));
    }

    /// <summary>Renders a set of rule names for one log line, which is safe because a rule name carries nothing personal.</summary>
    private static string NameList(IReadOnlyList<string> ruleNames) => string.Join(", ", ruleNames);

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

        // Null until the mapping is built, which is what decides whether this folder's turn reaches the ledger at all:
        // an alias configuration wrote unusably names no folder the status surface lists, so a report filed under the
        // configured spelling of it would be one nothing ever reads.
        MailFolderIdentity? folder = null;

        // Opened before the mapping is built, so a folder whose configuration reached the run unusable is a span with
        // a failure on it rather than a gap under the cycle. The alias is carried by the outcome for the same reason:
        // until the mapping exists there is only the configured spelling of it.
        using var folderRun = this.telemetry.BeginFolderRun(this.account.Id);

        try
        {
            var folderMapping = configuredFolder.CreateMapping();
            folderAlias = folderMapping.Alias.Value;
            folder = new MailFolderIdentity(this.account.Id, folderMapping.Alias);

            using var scope = this.scopeFactory.CreateScope();

            // The folder was scheduled from this run's snapshot, so the scope must connect with that snapshot too.
            // Letting the scope read the published one would pair an account list from before a reload with an
            // endpoint, policy, and credential from after it.
            scope.ServiceProvider.GetRequiredService<ScopedMailSynchronizationSettings>().UseRunSnapshot(runSettings);

            var synchronizer = scope.ServiceProvider.GetRequiredService<MailboxSynchronizer>();
            var result = await synchronizer.SynchronizeAsync(this.account, folderMapping, cancellationToken);

            this.ReportFolderOutcome(
                folderAlias,
                remotelyDeletedEmailDisposition,
                result,
                scope.ServiceProvider.GetRequiredService<MailboxContentVolumeTelemetry>(),
                folderRun);

            this.RecordFolderRun(folder, result);

            return new FolderRunOutcome(Succeeded: true, result.Folder, result.StoredEmailCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown rather than a defect, so the folder is recorded as interrupted and counted under no failure:
            // an account backed off for every restart would be an account approached less often for being stopped.
            folderRun.Interrupted(folderAlias);
            this.RecordFolderRun(folder, MailFolderRunOutcome.InterruptedByShutdown);

            throw;
        }
        catch (PersistenceConcurrencyConflictException exception)
        {
            this.LogFolderSynchronizationDeferredAfterConcurrencyConflict(exception, this.account.Id.Value, folderAlias);
            folderRun.ConcurrencyConflict(folderAlias);
            this.RecordFolderRun(folder, MailFolderRunOutcome.DeferredAfterConcurrencyConflict);

            return FolderRunOutcome.Failed;
        }
        catch (MailboxUnavailableException exception)
        {
            this.LogFolderSynchronizationDeferredAfterMailServerUnavailable(exception, this.account.Id.Value, folderAlias);
            folderRun.MailServerUnavailable(folderAlias);
            this.RecordFolderRun(folder, MailFolderRunOutcome.DeferredAfterMailServerUnavailable);

            return FolderRunOutcome.Failed;
        }
        catch (MailboxCredentialRefusedException exception)
        {
            // Separated from the unexpected failure below because the two ask for opposite things. Every other failure
            // here is waited out by the account's own backoff; a refused credential is refused identically on every
            // run until a person replaces it, so what the run owes is to say so rather than to keep trying quietly.
            this.LogFolderSynchronizationStoppedByRefusedCredential(exception, this.account.Id.Value, folderAlias);
            folderRun.CredentialRefused(folderAlias);
            this.RecordFolderRun(folder, MailFolderRunOutcome.CredentialRefused);

            return FolderRunOutcome.CredentialWasRefused;
        }
        catch (Exception exception)
        {
            this.LogFolderSynchronizationFailed(exception, this.account.Id.Value, folderAlias);
            folderRun.UnexpectedFailure(folderAlias);
            this.RecordFolderRun(folder, MailFolderRunOutcome.UnexpectedFailure);

            return FolderRunOutcome.Failed;
        }
    }

    /// <summary>Files what a completed folder turn did, translating the run's own outcome into the one an operator reads.</summary>
    /// <remarks>
    /// The counts are those of a folder the run reached; an alias that named no single advertised folder measured
    /// nothing, so it is filed as the outcome alone. That distinction is the reason for the translation at all — the
    /// synchronizer's outcome separates two configuration mistakes and nothing else, while the status surface has to
    /// place them beside the failures that never reach the synchronizer.
    /// </remarks>
    private void RecordFolderRun(MailFolderIdentity? folder, MailboxSynchronizationResult result)
    {
        if (folder is null)
        {
            return;
        }

        switch (result.Outcome)
        {
            case MailboxSynchronizationOutcome.FolderAliasUnresolved:
                this.runLedger.RecordFolderUnsynchronized(folder, MailFolderRunOutcome.AliasUnresolved);

                break;
            case MailboxSynchronizationOutcome.FolderAliasAmbiguous:
                this.runLedger.RecordFolderUnsynchronized(folder, MailFolderRunOutcome.AliasAmbiguous);

                break;
            default:
                this.runLedger.RecordFolderSynchronized(
                    folder,
                    result.StoredEmailCount,
                    result.SkippedOversizedEmailCount,
                    result.UnreadableMimeEmailCount,
                    result.HasMoreEmails);

                break;
        }
    }

    /// <summary>Files a folder turn that never reached the folder, where the alias was usable enough to file it under.</summary>
    private void RecordFolderRun(MailFolderIdentity? folder, MailFolderRunOutcome outcome)
    {
        if (folder is null)
        {
            return;
        }

        this.runLedger.RecordFolderUnsynchronized(folder, outcome);
    }

    /// <summary>Reports one folder's run, keeping an alias that named no single folder separate from a failure.</summary>
    private void ReportFolderOutcome(
        string folderAlias,
        RemotelyDeletedEmailDisposition remotelyDeletedEmailDisposition,
        MailboxSynchronizationResult result,
        MailboxContentVolumeTelemetry contentVolumeTelemetry,
        MailSynchronizationTelemetry.FolderRunScope folderRun)
    {
        if (result.Outcome == MailboxSynchronizationOutcome.FolderAliasUnresolved)
        {
            this.LogFolderAliasUnresolved(this.account.Id.Value, folderAlias);
            folderRun.AliasUnresolved(folderAlias);

            return;
        }

        if (result.Outcome == MailboxSynchronizationOutcome.FolderAliasAmbiguous)
        {
            this.LogFolderAliasAmbiguous(this.account.Id.Value, folderAlias);
            folderRun.AliasAmbiguous(folderAlias);

            return;
        }

        this.LogFolderSynchronized(
            this.account.Id.Value,
            folderAlias,
            result.StoredEmailCount,
            result.SkippedOversizedEmailCount,
            result.UnreadableMimeEmailCount,
            result.HasMoreEmails);

        if (result.RelocatedEmailCount > 0 || result.Reconciliation.OwnMutationCompletedEmailCount > 0)
        {
            this.LogOwnMutationsRecognized(
                this.account.Id.Value,
                folderAlias,
                result.RelocatedEmailCount,
                result.Reconciliation.OwnMutationCompletedEmailCount);
        }

        folderRun.Synchronized(folderAlias, result.StoredEmailCount, result.SkippedOversizedEmailCount);

        // Published only for a folder the run actually reached, because the level it carries is a measurement rather
        // than a count: an alias that resolved to nothing measured nothing, and publishing its empty volume would move
        // the deployment's stored-content gauge to zero.
        contentVolumeTelemetry.Report(this.account.Id, folderAlias, result.ContentVolume);

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

        this.LogChangesSuppressed(this.account.Id.Value, folderAlias, suppressedChanges.Count);

        foreach (var suppressed in suppressedChanges)
        {
            this.LogChangeSuppressed(
                this.account.Id.Value,
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
    /// per interval forever. Local deletion is reported at information level whichever disposition produced it, because
    /// mail leaving a mailbox is what synchronization is for rather than a fault an operator has to act on.
    /// </remarks>
    private void ReportReconciliation(
        string folderAlias,
        RemotelyDeletedEmailDisposition remotelyDeletedEmailDisposition,
        MailboxReconciliationResult reconciliation)
    {
        if (reconciliation.RemotelyDeletedEmailCount > 0)
        {
            this.LogRemotelyDeletedEmailsRecorded(
                this.account.Id.Value,
                folderAlias,
                reconciliation.RemotelyDeletedEmailCount,
                remotelyDeletedEmailDisposition);
        }

        if (reconciliation.SeenStateChangedEmailCount > 0)
        {
            this.LogSeenStateChangesObserved(
                this.account.Id.Value,
                folderAlias,
                reconciliation.SeenStateChangedEmailCount);
        }

        // Reported apart from the \Seen line rather than added to it, because the two say different things about a
        // mailbox: one is mail somebody read, the other is mail somebody singled out, and an operator watching for
        // either would have to subtract to get it back out of a total.
        if (reconciliation.FlaggedStateChangedEmailCount > 0)
        {
            this.LogFlaggedStateChangesObserved(
                this.account.Id.Value,
                folderAlias,
                reconciliation.FlaggedStateChangedEmailCount);
        }

        if (reconciliation.KeywordsChangedEmailCount > 0)
        {
            this.LogKeywordChangesObserved(
                this.account.Id.Value,
                folderAlias,
                reconciliation.KeywordsChangedEmailCount);
        }

        if (reconciliation.ObservedEmailCount > 0)
        {
            this.LogFolderReconciled(
                this.account.Id.Value,
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

    /// <summary>Reports the stars the mailbox owner set or cleared themselves, which no change of MailFathom's explains.</summary>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Mail server reports a moved \\Flagged flag on {FlaggedStateChangedEmailCount} messages stored for {AccountId}/{FolderAlias} that no change of MailFathom's accounts for.")]
    private partial void LogFlaggedStateChangesObserved(
        string accountId,
        string folderAlias,
        int flaggedStateChangedEmailCount);

    /// <summary>Reports the labels the mailbox owner put on or took off themselves, counted per message rather than per keyword.</summary>
    /// <remarks>The keywords themselves never reach the line. A label is text the owner or their client chose and can name a person, a case, or a diagnosis, so it is treated as derived from the message like any other part of it.</remarks>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Mail server reports different keywords on {KeywordsChangedEmailCount} messages stored for {AccountId}/{FolderAlias} that no change of MailFathom's accounts for.")]
    private partial void LogKeywordChangesObserved(
        string accountId,
        string folderAlias,
        int keywordsChangedEmailCount);

    /// <summary>Records mail leaving the local copy, which is the one reconciliation outcome an operator has to be able to find afterwards.</summary>
    /// <remarks>
    /// The disposition is part of the line rather than left to the reader to look up in configuration, because the two
    /// outcomes it names are not comparable: one hides a row, the other destroys it along with everything derived from
    /// it. Only counts and MailFathom's own configured names appear here; nothing derived from a message may.
    /// </remarks>
    [LoggerMessage(
        Level = LogLevel.Information,
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

    /// <summary>Reports the ending that waits for a person; a recipient names a person and never reaches a log.</summary>
    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "{UnknownCount} message(s) queued for account {AccountId} went out with their submission server never answering, so whether the recipients received them is unknown. None is transmitted again, and each stays visible in the outbox until somebody decides what to do with it.")]
    private partial void LogOutboxOutcomesUnknown(string accountId, int unknownCount);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "{RefusedCount} message(s) queued for account {AccountId} will not be offered again. What each recipient was told is on the send's own record.")]
    private partial void LogOutboxSendsRefused(string accountId, int refusedCount);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The outcome of {NotRecordedCount} message(s) queued for account {AccountId} could not be written down, so each record stands where the failed write left it and its lease is what frees it for another attempt.")]
    private partial void LogOutboxOutcomesNotRecorded(string accountId, int notRecordedCount);

    /// <summary>Reports the copies of delivered mail that are not in the folder the account asked for.</summary>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "{NotFiledCount} copy/copies of mail sent for account {AccountId} could not be put into the folder it files them in, so the owner will not see them in their own client. The messages were delivered, none is sent again, and nothing files the copies again on its own.")]
    private partial void LogOutboxCopiesNotFiled(string accountId, int notFiledCount);

    /// <summary>Reports what the account's own run found waiting in its outbox; a recipient names a person and never reaches a log.</summary>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The synchronization run for account {AccountId} drained its outbox: {SentCount} sent, {RefusedCount} refused, {DeferredCount} waiting for another attempt, {UnknownCount} with an outcome nobody can establish.")]
    private partial void LogOutboxDrained(
        string accountId,
        int sentCount,
        int refusedCount,
        int deferredCount,
        int unknownCount);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The outbox pass in the synchronization run for account {AccountId} failed; the account keeps synchronizing and the next run claims again.")]
    private partial void LogOutboxDrainFailed(Exception exception, string accountId);

    /// <summary>Separates a convergence pass that ended unexpectedly from the ordinary case of one change failing, which the pass itself absorbs.</summary>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Converging the outstanding mailbox mutations of account {AccountId} ended unexpectedly; its folders still synchronized and its next run is backed off, and every change keeps the record of how far it got.")]
    private partial void LogMutationConvergenceFailed(Exception exception, string accountId);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Account {AccountId} erased {ErasedCount} audit entries that had outlived its configured retention.")]
    private partial void LogAuditEntriesErased(string accountId, int erasedCount);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Account {AccountId} erased {ErasedCount} answering audit entries that had outlived its configured retention.")]
    private partial void LogAnsweringAuditEntriesErased(string accountId, int erasedCount);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Account {AccountId} erased {ErasedCount} recorded rule executions that had outlived the configured history retention.")]
    private partial void LogRuleExecutionsErased(string accountId, int erasedCount);

    /// <summary>Separates a retention pass that did not run from the ordinary case of it having nothing to erase.</summary>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Erasing the expired derived records of account {AccountId} ended unexpectedly; the account is not backed off for it and its next run erases what this one did not.")]
    private partial void LogDerivedRecordRetentionFailed(Exception exception, string accountId);

    /// <summary>States what the rules did to mail that has just arrived, naming the revision so an edit is visible in the record.</summary>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Evaluated rule set {RuleSetRevision} over {EvaluatedEmailCount} newly stored messages of account {AccountId}; {MatchedEmailCount} matched, {SkippedEmailCount} are waiting for their text to be extracted, rules that matched: [{MatchedRuleNames}], and more remain: {EmailsRemain}.")]
    private partial void LogArrivedMailEvaluated(
        string accountId,
        string ruleSetRevision,
        int evaluatedEmailCount,
        int matchedEmailCount,
        int skippedEmailCount,
        string matchedRuleNames,
        bool emailsRemain);

    /// <summary>Reports one account run's share of a whole-mailbox run, which spans as many runs as its batch budget needs.</summary>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Carried the whole-mailbox rule run of account {AccountId} over {EvaluatedEmailCount} messages; {MatchedEmailCount} matched, {SkippedEmailCount} are waiting for their text to be extracted, rules that matched: [{MatchedRuleNames}], and more remain: {EmailsRemain}.")]
    private partial void LogRequestedRunProgressed(
        string accountId,
        int evaluatedEmailCount,
        int matchedEmailCount,
        int skippedEmailCount,
        string matchedRuleNames,
        bool emailsRemain);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The whole-mailbox rule run of account {AccountId} reached the end of its mail under rule set {RuleSetRevision}.")]
    private partial void LogRequestedRunCompleted(string accountId, string ruleSetRevision);

    /// <summary>Names the remedy, because a superseded run is answered by asking for another rather than by waiting.</summary>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The rule set changed while the whole-mailbox rule run of account {AccountId} was outstanding, so the run ended without reaching the end of its mail; ask for it again to re-evaluate under the rules now in force.")]
    private partial void LogRequestedRunSuperseded(string accountId);

    /// <summary>Separates a rule that could not answer for one message from a pass that did not run, which are different faults.</summary>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "{FailedRuleCount} rule evaluations produced no answer for account {AccountId}, {TimedOutRuleCount} of them by outlasting the condition timeout; rules: [{FailedRuleNames}]. The messages are recorded as evaluated and the pass continued.")]
    private partial void LogRuleEvaluationsFailed(
        string accountId,
        int failedRuleCount,
        int timedOutRuleCount,
        string failedRuleNames);

    /// <summary>States what the rules asked the mailbox for, which the account's convergence pass carries rather than this one.</summary>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The rules of account {AccountId} asked for {RequestedActionCount} changes to its mailbox; the account's next convergence pass carries them to the mail server.")]
    private partial void LogRuleActionsRequested(string accountId, int requestedActionCount);

    /// <summary>Separates an action another rule had already settled from one the account or its folders no longer admit.</summary>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "{WithheldActionCount} actions declared for account {AccountId} were withheld because an earlier rule had already settled the same message, and {FailedActionCount} named something the account no longer resolves or no longer permits; rules: [{UnappliedActionRuleNames}].")]
    private partial void LogRuleActionsUnapplied(
        string accountId,
        int withheldActionCount,
        int failedActionCount,
        string unappliedActionRuleNames);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Evaluating the rules of account {AccountId} ended unexpectedly; the account is not backed off for it and its next run resumes from the batches this one committed.")]
    private partial void LogMailRuleEvaluationFailed(Exception exception, string accountId);

    /// <summary>Reports one account's cut in counts alone; no subject, address, or passage may reach a log.</summary>
    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Cut the passages of {ChunkedEmailCount} messages of account {AccountId}; the embedding backlog refused {RefusedOfferCount} of them, and messages remain: {EmailsRemain}.")]
    private partial void LogPassagesCut(
        string accountId,
        int chunkedEmailCount,
        int refusedOfferCount,
        bool emailsRemain);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Cutting the passages of the evaluated mail of account {AccountId} ended unexpectedly; the account is not backed off for it, and what was not cut stays outstanding for the next run and for the embedding sweep.")]
    private partial void LogPassageCutFailed(Exception exception, string accountId);

    /// <summary>Reports one account run's share of a whole-mailbox classification run, in counts and the profile alone.</summary>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Carried the whole-mailbox classification run of account {AccountId} under profile {Profile}; scored {ClassifiedEmailCount} messages, passed over {SkippedEmailCount} already decided under it, found {SpamEmailCount} junk, could reach no verdict about {UnclassifiableEmailCount}, acted on or would act on {ActedEmailCount}, and more remain: {EmailsRemain}.")]
    private partial void LogSpamClassificationProgressed(
        string accountId,
        string profile,
        int classifiedEmailCount,
        int skippedEmailCount,
        int spamEmailCount,
        int unclassifiableEmailCount,
        int actedEmailCount,
        bool emailsRemain);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The whole-mailbox classification run of account {AccountId} reached the end of its mail under profile {Profile}.")]
    private partial void LogSpamClassificationCompleted(string accountId, string profile);

    /// <summary>Names the remedy, because a superseded run is answered by asking for another rather than by waiting.</summary>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The classification settings changed while the whole-mailbox run of account {AccountId} was outstanding, so the run ended without reaching the end of its mail; ask for it again to classify under the settings now in force.")]
    private partial void LogSpamClassificationSuperseded(string accountId);

    /// <summary>Separates a run nobody switched classification on for from one that walked a mailbox and found it clean.</summary>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Classification was switched off while the whole-mailbox run of account {AccountId} was outstanding, so the run ended without reading its mail; switch classification on and ask for the run again.")]
    private partial void LogSpamClassificationDisabled(string accountId);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Carrying the whole-mailbox classification run of account {AccountId} ended unexpectedly; the account is not backed off for it and its next run resumes from the batches this one committed.")]
    private partial void LogSpamClassificationFailed(Exception exception, string accountId);

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

    /// <summary>Reports at error level the one folder failure no later run clears on its own.</summary>
    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The mail server refused the credential held for {AccountId} while synchronizing {FolderAlias}; the account is not fetched until the credential is replaced.")]
    private partial void LogFolderSynchronizationStoppedByRefusedCredential(
        Exception exception,
        string accountId,
        string folderAlias);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Reporting the run of account {AccountId} to its owner ended unexpectedly; the account is not backed off for it and its next run reports what this one observed.")]
    private partial void LogOwnerNotificationFailed(Exception exception, string accountId);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Erased {ErasedCount} expired notifications of the owner of account {AccountId}.")]
    private partial void LogNotificationsErased(string accountId, int erasedCount);

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
    /// <param name="StoredEmailCount">How many occurrences the folder committed with their content, which is what the run reports as arrived mail.</param>
    /// <param name="CredentialRefused">
    /// Whether the mail server refused the account's credential, which is the one failure here that a later run cannot
    /// clear on its own and is therefore reported to the person rather than only waited out.
    /// </param>
    private readonly record struct FolderRunOutcome(
        bool Succeeded,
        MailFolderResolution? ResolvedFolder,
        int StoredEmailCount = 0,
        bool CredentialRefused = false)
    {
        /// <summary>Gets the outcome of a folder that neither completed nor left a binding worth watching.</summary>
        internal static FolderRunOutcome Failed => new(Succeeded: false, ResolvedFolder: null);

        /// <summary>Gets the outcome of a folder the mail server would not let this account reach at all.</summary>
        internal static FolderRunOutcome CredentialWasRefused =>
            new(Succeeded: false, ResolvedFolder: null, StoredEmailCount: 0, CredentialRefused: true);
    }
}
