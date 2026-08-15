// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Synchronization.Administration;

/// <summary>Holds what this process's synchronization runs are doing and how their last ones ended.</summary>
/// <remarks>
/// <para>
/// One instance per process, written by the supervisors as they run and read by the administrative surface. It is
/// deliberately not durable, and that is the decision rather than a shortcut: what it carries is the state that governs
/// scheduling right now — which phase a supervisor is in, the delay it is waiting out, and the consecutive failure count
/// that delay grew from — and every one of those is reset by a restart. A count written across one would name a backoff
/// nothing is applying, which is worse than reporting that this process has not run the account yet.
/// </para>
/// <para>
/// What does survive a restart is the half that should: a folder's checkpoint is a durable row, so how far
/// synchronization has come and when it last moved are read from the database rather than from here.
/// <see cref="MailSynchronizationStatusReader" /> is where the two halves meet, and it is what makes a folder that is
/// repeating one batch distinguishable from one with nothing left to do even in a process that has only just started.
/// </para>
/// <para>
/// Nothing here is mail or derived from it. An account identifier, a folder alias, a phase, counts, and instants are the
/// whole of what it holds.
/// </para>
/// </remarks>
public sealed class MailSynchronizationRunLedger
{
    private readonly ConcurrentDictionary<MailAccountId, AccountLedgerEntry> accounts = new();
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes a ledger that has observed no run.</summary>
    /// <param name="timeProvider">Stamps every run this ledger records, so nothing that writes to it has to carry a clock.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="timeProvider" /> is <see langword="null" />.</exception>
    public MailSynchronizationRunLedger(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.timeProvider = timeProvider;
    }

    /// <summary>Records that an account is ready to run and is waiting for one of the slots that bound simultaneity.</summary>
    /// <param name="accountId">The account whose run is queued.</param>
    public void RecordRunQueued(MailAccountId accountId) =>
        this.Update(accountId, state => state with
        {
            Phase = MailAccountRunPhase.WaitingForRunSlot,
            NextRunDueAt = null,
        });

    /// <summary>Records that an account has taken a slot and its run has begun.</summary>
    /// <param name="accountId">The account that is now running.</param>
    public void RecordRunStarted(MailAccountId accountId) =>
        this.Update(accountId, state => state with
        {
            Phase = MailAccountRunPhase.Running,
            NextRunDueAt = null,
        });

    /// <summary>Records how one account's run ended.</summary>
    /// <param name="accountId">The account whose run finished.</param>
    /// <param name="scheduledFolderCount">How many folders the run scheduled.</param>
    /// <param name="failedFolderCount">How many of them did not complete.</param>
    /// <param name="mutationConvergenceFailed">Whether carrying the account's outstanding mailbox changes failed.</param>
    /// <remarks>
    /// The phase is left where the run put it, because what follows a finished run is the supervisor deciding a delay and
    /// reporting it through <see cref="RecordNextRunDue" />. Moving to a waiting phase here would claim a wait whose
    /// length nothing had computed yet.
    /// </remarks>
    public void RecordRunEnded(
        MailAccountId accountId,
        int scheduledFolderCount,
        int failedFolderCount,
        bool mutationConvergenceFailed)
    {
        var report = new MailAccountRunReport(
            this.timeProvider.GetUtcNow(),
            scheduledFolderCount,
            failedFolderCount,
            mutationConvergenceFailed);

        this.Update(accountId, state => state with { LastRun = report });
    }

    /// <summary>Records the wait one account is about to take, and the failure count that wait was grown from.</summary>
    /// <param name="accountId">The account that is waiting.</param>
    /// <param name="delayBeforeNextRun">How long it waits before its next run.</param>
    /// <param name="consecutiveFailureCount">How many of its runs failed in a row; zero once one succeeds.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="consecutiveFailureCount" /> is negative.</exception>
    /// <remarks>
    /// The instant is recorded rather than the delay, because a status surface is read at a moment the supervisor knows
    /// nothing about: a delay would have to be aged by whoever read it, and an instant is already the answer.
    /// </remarks>
    public void RecordNextRunDue(
        MailAccountId accountId,
        TimeSpan delayBeforeNextRun,
        int consecutiveFailureCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(consecutiveFailureCount);

        var dueAt = this.timeProvider.GetUtcNow() + delayBeforeNextRun;

        this.Update(accountId, state => state with
        {
            Phase = MailAccountRunPhase.WaitingForNextRun,
            NextRunDueAt = dueAt,
            ConsecutiveFailureCount = consecutiveFailureCount,
        });
    }

    /// <summary>Records a folder's turn that reached the folder its alias is bound to.</summary>
    /// <param name="folder">The account and alias the turn was taken for.</param>
    /// <param name="storedEmailCount">How many occurrences the turn stored with their content.</param>
    /// <param name="skippedOversizedEmailCount">How many it stored as metadata only.</param>
    /// <param name="unreadableMimeEmailCount">How many stored occurrences carried unreadable MIME.</param>
    /// <param name="hasMoreEmails">Whether the folder still held unprocessed mail when the turn's batch budget ran out.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="folder" /> is <see langword="null" />.</exception>
    public void RecordFolderSynchronized(
        MailFolderIdentity folder,
        int storedEmailCount,
        int skippedOversizedEmailCount,
        int unreadableMimeEmailCount,
        bool hasMoreEmails) =>
        this.RecordFolderRun(
            folder,
            endedAt => MailFolderRunReport.Synchronized(
                endedAt,
                storedEmailCount,
                skippedOversizedEmailCount,
                unreadableMimeEmailCount,
                hasMoreEmails));

    /// <summary>Records a folder's turn that did not synchronize the folder, whatever kept it from doing so.</summary>
    /// <param name="folder">The account and alias the turn was taken for.</param>
    /// <param name="outcome">Which of the unsynchronized outcomes ended the turn.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="folder" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="outcome" /> reports a folder that was synchronized.</exception>
    public void RecordFolderUnsynchronized(MailFolderIdentity folder, MailFolderRunOutcome outcome) =>
        this.RecordFolderRun(folder, endedAt => MailFolderRunReport.Unsynchronized(outcome, endedAt));

    /// <summary>Stores one folder's report, stamped with the moment its turn ended.</summary>
    /// <remarks>
    /// Keyed by alias rather than by the remote folder it resolved to, because an alias is what an operator configured
    /// and what they read a status by; a run that resolved the alias differently from the last one still describes the
    /// same configured folder.
    /// </remarks>
    private void RecordFolderRun(MailFolderIdentity folder, Func<DateTimeOffset, MailFolderRunReport> report)
    {
        ArgumentNullException.ThrowIfNull(folder);

        this.EntryOf(folder.AccountId).Folders[folder.Alias] = report(this.timeProvider.GetUtcNow());
    }

    /// <summary>Reads where one account's synchronization stands.</summary>
    /// <param name="accountId">The account to read.</param>
    /// <returns>The state, or <see cref="MailAccountRunState.NotStarted" /> for an account no run of this process has reached.</returns>
    /// <remarks>
    /// An account nothing has recorded reads as not started rather than as an absence, so no caller has to decide what a
    /// missing entry means — an account configured while the process was already running is exactly that case.
    /// </remarks>
    public MailAccountRunState ReadAccount(MailAccountId accountId) =>
        this.accounts.TryGetValue(accountId, out var entry) ? entry.State : MailAccountRunState.NotStarted;

    /// <summary>Reads how one folder's most recent turn through a run ended.</summary>
    /// <param name="folder">The account and alias to read.</param>
    /// <returns>The report, or <see langword="null" /> when no run of this process has taken a turn for that folder.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="folder" /> is <see langword="null" />.</exception>
    public MailFolderRunReport? ReadFolder(MailFolderIdentity folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        return this.accounts.TryGetValue(folder.AccountId, out var entry)
            && entry.Folders.TryGetValue(folder.Alias, out var report)
                ? report
                : null;
    }

    /// <summary>Replaces one account's state, reading the value the replacement is derived from under the entry's lock.</summary>
    /// <remarks>
    /// The state is one immutable value rather than several fields, which is what makes a read of it safe without taking
    /// anything: a reader observes whichever complete value was last published and never a half-written one. The lock
    /// covers the read-modify-write instead, so a transition that keeps part of the previous state cannot lose a
    /// concurrent one. It is per account, so two accounts never contend.
    /// </remarks>
    private void Update(MailAccountId accountId, Func<MailAccountRunState, MailAccountRunState> transition)
    {
        var entry = this.EntryOf(accountId);

        lock (entry.Gate)
        {
            entry.State = transition(entry.State);
        }
    }

    private AccountLedgerEntry EntryOf(MailAccountId accountId) =>
        this.accounts.GetOrAdd(accountId, static _ => new AccountLedgerEntry());

    /// <summary>One account's entry: the state its supervisor writes, and the last report of each of its folders.</summary>
    private sealed class AccountLedgerEntry
    {
        /// <summary>Gets the lock the state is replaced under.</summary>
        public Lock Gate { get; } = new();

        /// <summary>Gets the last report of each folder, which the run's folders write concurrently.</summary>
        public ConcurrentDictionary<MailFolderAlias, MailFolderRunReport> Folders { get; } = new();

        /// <summary>Gets or sets where the account stands, replaced whole under <see cref="Gate" />.</summary>
        public MailAccountRunState State { get; set; } = MailAccountRunState.NotStarted;
    }
}
