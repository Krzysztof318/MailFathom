// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Folders;
using MailFathom.Application.Mail.Mutations.Audit;
using MailFathom.Application.Mail.Mutations.Convergence;
using MailFathom.Application.Persistence;
using MailFathom.Application.Retrieval.AskMail.Audit;
using MailFathom.Application.Rules.Evaluation;
using MailFathom.Application.Rules.History;
using MailFathom.Application.Synchronization;
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
        // A folder the operator stopped mirroring is not scheduled at all, which is what makes "no connection is opened
        // for it" true rather than merely quiet: the mapping goes on naming the folder for anything that writes into it,
        // and this run neither discovers nor selects it.
        var scheduledFolders = account.EffectiveFolders
            .Where(static folder => folder.Participation.IsSynchronized)
            .ToArray();
        var unmirroredFolders = account.EffectiveFolders
            .Where(static folder => !folder.Participation.IsSynchronized)
            .ToArray();
        var resolvedFolders = new ConcurrentBag<MailFolderResolution>();
        var failedFolderCount = 0;
        var convergenceFailed = false;

        await this.accountRunSlots.WaitAsync(schedulingToken);

        var startedAt = this.timeProvider.GetTimestamp();

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

                    if (!folderRun.Succeeded)
                    {
                        Interlocked.Increment(ref failedFolderCount);
                    }
                });

            if (!schedulingToken.IsCancellationRequested)
            {
                await this.EraseUnmirroredFolderContentAsync(runSettings, unmirroredFolders, workUnitToken);
                await this.EraseExpiredAuditEntriesAsync(runSettings, workUnitToken);
                await this.EvaluateMailRulesAsync(runSettings, workUnitToken);
            }
        }
        finally
        {
            this.accountRunSlots.Release();
        }

        var runDuration = this.timeProvider.GetElapsedTime(startedAt);

        this.LogAccountRunFinished(
            this.accountId.Value,
            scheduledFolders.Length,
            failedFolderCount,
            runDuration);

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
            var report = await converger.ConvergeAsync(this.accountId, cancellationToken);

            scope.ServiceProvider.GetRequiredService<MailboxConvergenceTelemetry>().Report(this.accountId, report);

            return report.FailedCount > 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            this.LogMutationConvergenceFailed(exception, this.accountId.Value);

            return true;
        }
    }

    /// <summary>Erases what is still stored for the folders this account has stopped mirroring.</summary>
    /// <remarks>
    /// <para>
    /// It rides the account's own run for the reason audit retention does, and is bounded the same way: an operator who
    /// turns a mirrored folder off gets it emptied over as many runs as its size needs rather than in one transaction.
    /// A folder that was never mirrored costs one bounded query that finds nothing, which is what every run of every
    /// account with such a folder does from then on.
    /// </para>
    /// <para>
    /// A failure never fails the run. The rows are stale rather than wrong — no tool may read them, because a folder
    /// nothing mirrors is a folder nothing shows — so putting the account into backoff over them would fetch its mail
    /// less often to fix something that is not about the mail server at all.
    /// </para>
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Erasing a folder nothing mirrors is not a mail operation; a pass that failed is logged and repeated by the next run rather than putting the account into backoff.")]
    private async Task EraseUnmirroredFolderContentAsync(
        MailSynchronizationOptions runSettings,
        MailFolderMappingOptions[] unmirroredFolders,
        CancellationToken cancellationToken)
    {
        if (unmirroredFolders.Length == 0)
        {
            return;
        }

        try
        {
            using var scope = this.scopeFactory.CreateScope();

            scope.ServiceProvider.GetRequiredService<ScopedMailSynchronizationSettings>().UseRunSnapshot(runSettings);

            var eraser = scope.ServiceProvider.GetRequiredService<UnmirroredMailFolderEraser>();

            foreach (var folder in unmirroredFolders)
            {
                var alias = MailFolderAlias.Create(folder.Alias);
                var erasure = await eraser.EraseAsync(this.accountId, alias, cancellationToken);

                if (erasure.ErasedEmailCount > 0)
                {
                    this.LogUnmirroredFolderContentErased(
                        this.accountId.Value,
                        alias.Value,
                        erasure.ErasedEmailCount,
                        erasure.EmailsRemain);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            this.LogUnmirroredFolderErasureFailed(exception, this.accountId.Value);
        }
    }

    /// <summary>Erases whatever in this account's three records has outlived the window it was configured for.</summary>
    /// <remarks>
    /// <para>
    /// All three age out here — the trail of the changes MailFathom made to the mailbox, the record of the questions
    /// answered from it, and the history of what the rules concluded about its mail. They are separate operator
    /// decisions with separate windows, and one pass because the pass is what the account's own loop already provides: a
    /// second schedule would be another thing to configure and watch for work that is three bounded deletes.
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
    private async Task EraseExpiredAuditEntriesAsync(
        MailSynchronizationOptions runSettings,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = this.scopeFactory.CreateScope();

            scope.ServiceProvider.GetRequiredService<ScopedMailSynchronizationSettings>().UseRunSnapshot(runSettings);

            var erasedCount = await scope.ServiceProvider
                .GetRequiredService<MailboxMutationAuditTrailRetention>()
                .EraseExpiredAsync(this.accountId, cancellationToken);

            if (erasedCount > 0)
            {
                this.LogAuditEntriesErased(this.accountId.Value, erasedCount);
            }

            var erasedAnsweringCount = await scope.ServiceProvider
                .GetRequiredService<MailAnsweringAuditTrailRetention>()
                .EraseExpiredAsync(this.accountId, cancellationToken);

            if (erasedAnsweringCount > 0)
            {
                this.LogAnsweringAuditEntriesErased(this.accountId.Value, erasedAnsweringCount);
            }

            var erasedExecutionCount = await scope.ServiceProvider
                .GetRequiredService<MailRuleHistoryRetention>()
                .EraseExpiredAsync(this.accountId, cancellationToken);

            if (erasedExecutionCount > 0)
            {
                this.LogRuleExecutionsErased(this.accountId.Value, erasedExecutionCount);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            this.LogAuditRetentionFailed(exception, this.accountId.Value);
        }
    }

    /// <summary>Runs this account's rules over the mail that has arrived, and over its whole mailbox where one was asked for.</summary>
    /// <remarks>
    /// <para>
    /// Last of the run's local steps, and after the folders rather than beside them, which is what makes "evaluation
    /// never runs inside the synchronization transaction" true rather than merely intended: every message it can reach
    /// was committed by a folder that has already finished, in a scope and a transaction of its own. It is also after
    /// the unmirrored erasure, so a folder the operator has stopped mirroring has already given up its rows instead of
    /// offering them to a rule on the way out.
    /// </para>
    /// <para>
    /// A failure never fails the run. Evaluation reaches no mail server — everything it reads was already stored — so
    /// backing the account off, which is to say fetching its mail less often, would answer a local problem by slowing
    /// the remote work that had nothing to do with it. What a pass did not finish, the next run resumes from the
    /// batches this one committed.
    /// </para>
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Rule evaluation is a local pass rather than a mail operation; one that failed is logged and resumed by the next run rather than putting the account into backoff.")]
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
                .RunAsync(this.accountId, cancellationToken);

            this.ReportRuleEvaluation(report);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            this.LogMailRuleEvaluationFailed(exception, this.accountId.Value);
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
                this.accountId.Value,
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
                    this.accountId.Value,
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
                this.LogRequestedRunCompleted(this.accountId.Value, report.Revision.Value);

                break;
            case MailRuleEvaluationRunEnding.Superseded:
                this.LogRequestedRunSuperseded(this.accountId.Value);

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
            this.accountId.Value,
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
            this.LogRuleActionsRequested(this.accountId.Value, walk.RequestedActionCount);
        }

        if (walk.WithheldActionCount == 0 && walk.FailedActionCount == 0)
        {
            return;
        }

        this.LogRuleActionsUnapplied(
            this.accountId.Value,
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

            this.ReportFolderOutcome(
                folderAlias,
                remotelyDeletedEmailDisposition,
                result,
                scope.ServiceProvider.GetRequiredService<MailboxContentVolumeTelemetry>());

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
        MailboxSynchronizationResult result,
        MailboxContentVolumeTelemetry contentVolumeTelemetry)
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

        // Published only for a folder the run actually reached, because the level it carries is a measurement rather
        // than a count: an alias that resolved to nothing measured nothing, and publishing its empty volume would move
        // the deployment's stored-content gauge to zero.
        contentVolumeTelemetry.Report(this.accountId, folderAlias, result.ContentVolume);

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
        Message = "Erasing the expired audit entries of account {AccountId} ended unexpectedly; the account is not backed off for it and its next run erases what this one did not.")]
    private partial void LogAuditRetentionFailed(Exception exception, string accountId);

    /// <summary>Reported at warning level because it is local mail going away, which an operator must be able to find afterwards.</summary>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Account {AccountId} erased {ErasedCount} stored emails of folder {FolderAlias}, which its configuration no longer mirrors; emails remaining for a later run: {EmailsRemain}.")]
    private partial void LogUnmirroredFolderContentErased(
        string accountId,
        string folderAlias,
        int erasedCount,
        bool emailsRemain);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Erasing the stored mail of the folders account {AccountId} no longer mirrors ended unexpectedly; the account is not backed off for it and its next run erases what this one did not.")]
    private partial void LogUnmirroredFolderErasureFailed(Exception exception, string accountId);

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
