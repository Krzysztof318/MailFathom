// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Persistence;
using MailFathom.Application.Signals;
using MailFathom.Application.Synchronization.Sessions;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;

namespace MailFathom.Application.Synchronization.Reconciliation;

/// <summary>Brings the local copy of one folder back in line with what the mail server still holds.</summary>
/// <remarks>
/// <para>
/// This is the backward half of a synchronization run. The forward half only ever moves past the checkpoint, so it can
/// discover a new email and can never notice that an old one is gone or that its flags have changed; this pass walks a
/// bounded window of what is already stored and asks the server about it.
/// </para>
/// <para>
/// The window is chosen by how long ago each occurrence was last observed and is bounded per run, so a large mailbox is
/// reconciled over many runs rather than scanned in one. Writing an observation is what moves an occurrence to the back
/// of that queue, which is why the pass needs no cursor of its own and why an interrupted run resumes rather than
/// restarts.
/// </para>
/// <para>
/// Everything it does against the server is read-only. It asks for flags and for nothing that could set the remote
/// <c>\Seen</c> flag, and it holds no port that could write one back — which is the structural form of the invariant
/// this pass is the riskiest place in the system for.
/// </para>
/// <para>
/// A disappearance is not by itself somebody else's act. MailFathom relocates and deletes mail on the server too, and
/// the occurrence leaving its folder is those changes completing rather than a remote deletion to react to. The durable
/// mutation record is what tells the two apart, so the pass reads it before the disposition is reached.
/// </para>
/// <para>
/// The same holds of a <c>\Seen</c> flag that has moved, and there it is the whole difficulty. A flag change arrives as
/// a changed modification sequence, which is the identical signal a person marking mail read in their own client
/// produces, so a rule conditioned on unread mail that marks mail read would re-evaluate everything it had just acted
/// on. The record answers it, and both halves of that answer are recorded in the window's own transaction so a change
/// can never be marked accounted for by a window whose reading of it was rolled back.
/// </para>
/// </remarks>
public sealed class MailboxReconciler
{
    private readonly IStoredEmailReconciliationStore reconciliationStore;
    private readonly IMailboxMutationReconciliationStore mutationStore;
    private readonly IRemotelyDeletedEmailDispositionReader dispositionReader;
    private readonly OptimisticConcurrencyRetryPolicy concurrencyRetryPolicy;
    private readonly ClientSignals signals;
    private readonly TimeProvider timeProvider;
    private readonly MailboxSynchronizationOptions options;

    /// <summary>Initializes a new mailbox reconciler.</summary>
    /// <param name="reconciliationStore">Chooses the window and records what the server answered.</param>
    /// <param name="mutationStore">Says which of the disappearances are changes MailFathom itself made.</param>
    /// <param name="dispositionReader">Answers what the account being reconciled does with an email its server no longer holds.</param>
    /// <param name="concurrencyRetryPolicy">Commits one window's outcome, retrying a conflict with a competing writer.</param>
    /// <param name="signals">Tells an open client which stored mail this window moved, so what is on the screen catches up.</param>
    /// <param name="timeProvider">Stamps the observation, which is what advances the window across runs.</param>
    /// <param name="options">Bounds one window.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public MailboxReconciler(
        IStoredEmailReconciliationStore reconciliationStore,
        IMailboxMutationReconciliationStore mutationStore,
        IRemotelyDeletedEmailDispositionReader dispositionReader,
        OptimisticConcurrencyRetryPolicy concurrencyRetryPolicy,
        ClientSignals signals,
        TimeProvider timeProvider,
        MailboxSynchronizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(reconciliationStore);
        ArgumentNullException.ThrowIfNull(mutationStore);
        ArgumentNullException.ThrowIfNull(dispositionReader);
        ArgumentNullException.ThrowIfNull(concurrencyRetryPolicy);
        ArgumentNullException.ThrowIfNull(signals);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(options);

        this.reconciliationStore = reconciliationStore;
        this.mutationStore = mutationStore;
        this.dispositionReader = dispositionReader;
        this.concurrencyRetryPolicy = concurrencyRetryPolicy;
        this.signals = signals;
        this.timeProvider = timeProvider;
        this.options = options;
    }

    /// <summary>Reconciles one bounded window of the folder the supplied session has open.</summary>
    /// <param name="mailboxSession">The open read-only session, whose folder and UIDVALIDITY the window is chosen for.</param>
    /// <param name="account">The account being reconciled.</param>
    /// <param name="folder">The alias binding being reconciled.</param>
    /// <param name="uidValidity">The UIDVALIDITY the session reports for the open folder.</param>
    /// <param name="reconciledThroughModSeq">
    /// The modification sequence a previous pass covered the whole folder through, or <see langword="null" /> when
    /// there is none to reconcile from.
    /// </param>
    /// <param name="cancellationToken">Cancels the pass between the remote read and the local write.</param>
    /// <returns>What this window found, and whether occurrences remain to be reconciled in a later run.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="mailboxSession" /> or <paramref name="folder" /> is <see langword="null" />.</exception>
    /// <exception cref="MailboxUnavailableException">Thrown when the mail server did not serve the read within its configured resilience budget.</exception>
    /// <exception cref="MailboxFolderRecreatedException">Thrown when a recovered connection reselected the folder with a different UIDVALIDITY.</exception>
    /// <exception cref="PersistenceConcurrencyConflictException">
    /// Thrown when a competing writer wins a race the bounded retries could not resolve. Nothing this window found is
    /// then committed, and the next run selects the same occurrences because none of them was observed.
    /// </exception>
    /// <remarks>
    /// <para>
    /// A folder whose UIDVALIDITY has changed reconciles nothing rather than deleting anything. The window is selected
    /// under the UIDVALIDITY the session reports, so occurrences stored under the previous one are simply not in it:
    /// the renumbering is left to the existing invalidation rule, and no mass local deletion can follow from it.
    /// </para>
    /// <para>
    /// A modification sequence is reported back only when this window emptied the folder's queue. Until then the pass
    /// is partway through the folder, and a sequence recorded now would assert that everything older than it has been
    /// accounted for — including the occurrences this window never reached, which would then never be asked about.
    /// </para>
    /// </remarks>
    public async Task<MailboxReconciliationResult> ReconcileAsync(
        IMailboxSession mailboxSession,
        MailAccountIdentity account,
        MailFolderResolution folder,
        ImapUidValidity uidValidity,
        ulong? reconciledThroughModSeq,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mailboxSession);
        ArgumentNullException.ThrowIfNull(folder);

        var window = await this.reconciliationStore.GetReconciliationWindowAsync(
            account,
            folder.Id,
            uidValidity,
            this.options.MaxReconciledEmailsPerRun,
            cancellationToken);

        if (window.Count == 0)
        {
            return MailboxReconciliationResult.NothingToReconcile;
        }

        var observation = await mailboxSession.ObserveWindowWithoutSettingSeenAsync(
            [.. window.Select(candidate => candidate.Uid)],
            reconciledThroughModSeq,
            cancellationToken);

        var classification = ClassifyWindow(window, observation);
        var outcome = await this.AttributeDisappearancesAsync(
            classification,
            account,
            folder.Id,
            uidValidity,
            cancellationToken);
        var flagChanges = await this.AttributeFlagChangesAsync(
            classification,
            account,
            folder.Id,
            uidValidity,
            cancellationToken);

        await this.concurrencyRetryPolicy.CommitAsync(
            async (persistenceSession, attemptCancellationToken) =>
            {
                await this.reconciliationStore.ApplyReconciliationOutcomeAsync(
                    persistenceSession,
                    outcome,
                    attemptCancellationToken);

                // Written in the window's own transaction, so a record can never say a change was accounted for by a
                // window whose observation of it was rolled back.
                foreach (var attributed in outcome.RemovedByOwnMutation)
                {
                    await this.mutationStore.RecordSourceRemovalObservedAsync(
                        persistenceSession,
                        attributed.MutationRecordId,
                        outcome.ObservedAt,
                        attemptCancellationToken);
                }

            },
            cancellationToken);

        var emailsRemain = window.Count == this.options.MaxReconciledEmailsPerRun;

        this.AnnounceWhatMoved(account, folder.Alias, outcome);

        return new MailboxReconciliationResult(
            outcome.StillPresent.Count + outcome.ConfirmedUnchanged.Count,
            outcome.Disappeared.Count,
            outcome.RemovedByOwnMutation.Count,
            flagChanges.ExternalSeenStateCount,
            flagChanges.ExternalFlaggedStateCount,
            flagChanges.ExternalKeywordsCount,
            emailsRemain,
            emailsRemain ? null : observation.FolderHighestModSeq,
            [
                .. outcome.RemovedByOwnMutation.Select(static attributed => new SuppressedMailboxChange(
                    MailboxChangeKind.EmailLeftFolder,
                    attributed.Mutation,
                    attributed.StoredEmailId,
                    attributed.MutationRecordId)),
                .. flagChanges.Suppressed,
            ]);
    }

    /// <summary>Tells an open client which stored mail this window moved, once the window is committed.</summary>
    /// <remarks>
    /// After the commit rather than inside it, because a signal is an instruction to re-read and a client that acted on
    /// one before the transaction landed would read the state the window was replacing. A window that moved nothing
    /// says nothing, which is every window on a mailbox nobody is changing.
    /// <para>
    /// What crosses is the account, the folder, and the stored identities — no flag, no count of which kind of change,
    /// and nothing derived from a message. Which of them changed is what the client re-reads to find out.
    /// </para>
    /// </remarks>
    private void AnnounceWhatMoved(
        MailAccountIdentity account,
        MailFolderAlias folder,
        ReconciledFolderOutcome outcome)
    {
        if (!this.signals.Reaches)
        {
            return;
        }

        var moved = outcome.StillPresent
            .Select(static observed => observed.StoredEmailId)
            .Concat(outcome.Disappeared)
            .Concat(outcome.RemovedByOwnMutation.Select(static attributed => attributed.StoredEmailId))
            .ToArray();

        if (moved.Length == 0)
        {
            return;
        }

        this.signals.Publish(ClientSignal.MailChanged(account, folder, moved));
    }

    /// <summary>Separates the disappearances MailFathom caused from the ones the disposition answers for.</summary>
    /// <remarks>
    /// <para>
    /// The whole window's gone UIDs are asked about in one read rather than one per email, and a window that found none
    /// asks nothing at all — which is every window on a mailbox nobody is changing through MailFathom.
    /// </para>
    /// <para>
    /// An occurrence matched by more than one record is attributed once, to the oldest, because the read is ordered and
    /// the first match is taken. Which record is credited changes nothing about the local row; what matters is that the
    /// disappearance does not reach the remote-deletion path.
    /// </para>
    /// </remarks>
    private async Task<ReconciledFolderOutcome> AttributeDisappearancesAsync(
        ReconciledWindowClassification classification,
        MailAccountIdentity account,
        MailFolderResolutionId folderResolutionId,
        ImapUidValidity uidValidity,
        CancellationToken cancellationToken)
    {
        var disposition = this.dispositionReader.GetDisposition(account.Id);
        var observedAt = this.timeProvider.GetUtcNow();

        if (classification.Disappeared.Count == 0)
        {
            return new ReconciledFolderOutcome(
                classification.ObservedFlags,
                classification.ConfirmedUnchanged,
                [],
                [],
                disposition,
                observedAt);
        }

        var records = await this.mutationStore.ReadMutationsRemovingAsync(
            account,
            folderResolutionId,
            uidValidity,
            [.. classification.Disappeared.Select(static candidate => candidate.Uid)],
            cancellationToken);

        var attributions = classification.Disappeared
            .Select(candidate => new
            {
                candidate.StoredEmailId,
                Record = FindRecordRemoving(records, account, folderResolutionId, uidValidity, candidate.Uid),
            })
            .ToArray();

        return new ReconciledFolderOutcome(
            classification.ObservedFlags,
            classification.ConfirmedUnchanged,
            [.. attributions.Where(static attribution => attribution.Record is null).Select(static attribution => attribution.StoredEmailId)],
            [
                .. attributions
                    .Where(static attribution => attribution.Record is not null)
                    .Select(static attribution => new MutationAttributedDisappearance(
                        attribution.StoredEmailId,
                        attribution.Record!.Id,
                        attribution.Record.Request.Mutation,
                        attribution.Record.Request.LocalDisposition)),
            ],
            disposition,
            observedAt);
    }

    /// <summary>Separates the flag and keyword changes MailFathom made from the ones the mailbox owner made.</summary>
    /// <remarks>
    /// <para>
    /// Only an occurrence where one of the three values actually stands somewhere new is asked about, so a window over a
    /// mailbox nobody has touched reads no records at all. An occurrence nobody had observed before has no previous
    /// value to differ from, so its first reading is the initial observation rather than a change — and treating it as
    /// one would raise a trigger for every message the forward pass had just stored.
    /// </para>
    /// <para>
    /// The three are attributed together against one read rather than three, because they arrive together: one
    /// <c>FLAGS</c> response carries all of them, so an occurrence whose star and whose label both moved would otherwise
    /// be asked about twice. The read is widened to every mutation a <c>STORE</c> carries and each value is then
    /// answered by the record that could account for it, which keeps the number of queries a window costs the same as
    /// it was when only <c>\Seen</c> was written.
    /// </para>
    /// <para>
    /// A change with no record is the mailbox owner's own act and is counted rather than suppressed, which is what keeps
    /// this about provenance instead of about which field moved. The counts are kept apart per value because they answer
    /// different questions about a mailbox: mail somebody read, mail somebody starred, and mail somebody labelled are
    /// three readings rather than one total.
    /// </para>
    /// <para>
    /// Nothing is written back to the record here, and it needs nothing: a store answers only for a reading taken before
    /// the occurrence was next observed, and applying this window's outcome is what moves that observation forward. The
    /// window's own transaction therefore ends the record's answer whether or not anything matched, which is the case a
    /// mark on the row would miss — an owner who reverted the flag before the first reading would leave such a mark
    /// unwritten and have their own later change silenced by it.
    /// </para>
    /// </remarks>
    private async Task<ReconciledFlagChanges> AttributeFlagChangesAsync(
        ReconciledWindowClassification classification,
        MailAccountIdentity account,
        MailFolderResolutionId folderResolutionId,
        ImapUidValidity uidValidity,
        CancellationToken cancellationToken)
    {
        var changed = classification.StillPresent
            .Where(static observed => observed.SeenStateMoved || observed.FlaggedStateMoved || observed.KeywordsMoved)
            .ToArray();

        if (changed.Length == 0)
        {
            return ReconciledFlagChanges.None;
        }

        // The earliest reading any of these occurrences carries is the point before which no record can account for
        // anything, because every comparison below requires a store whose stage moved after the value was last read.
        // Nothing is left with no reading at all, since a value can only have moved against one.
        if (changed.Select(static observed => observed.Candidate.LastObservation?.ObservedAt).Min() is not { } issuedAfter)
        {
            return ReconciledFlagChanges.None;
        }

        var records = await this.mutationStore.ReadFlagChangesOnAsync(
            account,
            folderResolutionId,
            uidValidity,
            [.. changed.Select(static observed => observed.Candidate.Uid)],
            issuedAfter,
            cancellationToken);

        var attributions = changed
            .SelectMany(observed => AttributedValuesOf(
                records,
                observed,
                account,
                folderResolutionId,
                uidValidity))
            .ToArray();

        IReadOnlyList<SuppressedMailboxChange> suppressed =
        [
            .. attributions
                .Where(static attribution => attribution.Record is not null)
                .Select(static attribution => new SuppressedMailboxChange(
                    attribution.Kind,
                    attribution.Record!.Request.Mutation,
                    attribution.StoredEmailId,
                    attribution.Record.Id)),
        ];

        return new ReconciledFlagChanges(
            ExternalCountOf(attributions, MailboxChangeKind.SeenStateChanged),
            ExternalCountOf(attributions, MailboxChangeKind.FlaggedStateChanged),
            ExternalCountOf(attributions, MailboxChangeKind.KeywordsChanged),
            suppressed);
    }

    /// <summary>Asks, for each value one occurrence moved, which record accounts for it — and reports none where nothing does.</summary>
    /// <remarks>
    /// A value that did not move produces no entry at all, so an occurrence whose star moved while its label did not is
    /// one question rather than three. That is also what keeps the counts honest: an unchanged value must reach neither
    /// the external tally nor the suppressed list.
    /// </remarks>
    private static IEnumerable<AttributedFlagChange> AttributedValuesOf(
        IReadOnlyList<MailboxMutationRecord> records,
        ObservedWindowCandidate observed,
        MailAccountIdentity account,
        MailFolderResolutionId folderResolutionId,
        ImapUidValidity uidValidity)
    {
        // A moved value means the occurrence was observed before, so the window entry carries that reading and the
        // filter above never admits an entry without one.
        if (observed.Candidate.LastObservation is not { } lastObservation)
        {
            yield break;
        }

        var occurrence = EmailOccurrenceId.Create(
            account.Id,
            folderResolutionId,
            uidValidity,
            observed.Candidate.Uid);

        if (observed.SeenStateMoved)
        {
            yield return new AttributedFlagChange(
                MailboxChangeKind.SeenStateChanged,
                observed.Candidate.StoredEmailId,
                FirstRecord(records, record => record.AccountsForSeenStateOf(
                    occurrence,
                    observed.Snapshot.IsSeen,
                    lastObservation.ObservedAt)));
        }

        if (observed.FlaggedStateMoved)
        {
            yield return new AttributedFlagChange(
                MailboxChangeKind.FlaggedStateChanged,
                observed.Candidate.StoredEmailId,
                FirstRecord(records, record => record.AccountsForFlaggedStateOf(
                    occurrence,
                    observed.Snapshot.IsFlagged,
                    lastObservation.ObservedAt)));
        }

        if (observed.KeywordsMoved)
        {
            yield return new AttributedFlagChange(
                MailboxChangeKind.KeywordsChanged,
                observed.Candidate.StoredEmailId,
                FirstRecord(records, record => record.AccountsForKeywordsOf(
                    occurrence,
                    lastObservation.Keywords,
                    observed.Snapshot.Keywords,
                    lastObservation.ObservedAt)));
        }
    }

    /// <summary>Counts the changes of one kind that no record accounted for, which are the mailbox owner's own.</summary>
    private static int ExternalCountOf(
        IReadOnlyList<AttributedFlagChange> attributions,
        MailboxChangeKind kind) =>
        attributions.Count(attribution => attribution.Kind == kind && attribution.Record is null);

    /// <summary>Finds the store that put a value where the server has just reported it, if one did.</summary>
    /// <remarks>
    /// The first match is taken, as it is for a disappearance: one occurrence can carry a record per requester, and
    /// which of them is credited changes nothing about the local row — what matters is that the change does not reach
    /// evaluation as somebody else's.
    /// </remarks>
    private static MailboxMutationRecord? FirstRecord(
        IReadOnlyList<MailboxMutationRecord> records,
        Func<MailboxMutationRecord, bool> accountsForChange) =>
        records.FirstOrDefault(accountsForChange);

    private static MailboxMutationRecord? FindRecordRemoving(
        IReadOnlyList<MailboxMutationRecord> records,
        MailAccountIdentity account,
        MailFolderResolutionId folderResolutionId,
        ImapUidValidity uidValidity,
        ImapUid uid)
    {
        var occurrence = EmailOccurrenceId.Create(account.Id, folderResolutionId, uidValidity, uid);

        return records.FirstOrDefault(record => record.AccountsForRemovalOf(occurrence));
    }

    /// <summary>Sorts the window into the occurrences the server described, the ones it confirmed, and the ones it accounted for in neither.</summary>
    /// <remarks>
    /// <para>
    /// Silence is the finding. An occurrence the server neither described nor confirmed is one the folder no longer
    /// holds, so the split is made against what came back rather than against any flag inside it — the <c>\Deleted</c>
    /// flag marks a message still present in the folder and is a different statement entirely.
    /// </para>
    /// <para>
    /// A confirmation and a description are the same statement about existence and differ only in what is written: a
    /// described occurrence gets the flags the server reported, a confirmed one keeps the flags already stored because
    /// the server said they have not moved. Both then leave the queue, which is what lets the next window reach further
    /// into the folder.
    /// </para>
    /// <para>
    /// A UID the answer names twice keeps its first observation rather than failing the run, and one named as both
    /// described and confirmed is taken as described. Nothing in the protocol promises one entry per UID, and a server
    /// that repeats one has said the message exists, which is the only thing this classification reads out of it.
    /// </para>
    /// </remarks>
    private static ReconciledWindowClassification ClassifyWindow(
        IReadOnlyList<StoredEmailAwaitingReconciliation> window,
        RemoteFolderWindowObservation observation)
    {
        var snapshotsByUid = observation.Observations
            .DistinctBy(static describedOccurrence => describedOccurrence.Uid)
            .ToDictionary(
                static describedOccurrence => describedOccurrence.Uid,
                static describedOccurrence => describedOccurrence.Snapshot);
        var unchangedUids = observation.UnchangedUids.ToHashSet();

        var stillPresent = window
            .Where(candidate => snapshotsByUid.ContainsKey(candidate.Uid))
            .Select(candidate => new ObservedWindowCandidate(candidate, snapshotsByUid[candidate.Uid]))
            .ToArray();
        var confirmedUnchanged = window
            .Where(candidate => !snapshotsByUid.ContainsKey(candidate.Uid) && unchangedUids.Contains(candidate.Uid))
            .Select(static candidate => candidate.StoredEmailId)
            .ToArray();
        var disappeared = window
            .Where(candidate => !snapshotsByUid.ContainsKey(candidate.Uid) && !unchangedUids.Contains(candidate.Uid))
            .ToArray();

        return new ReconciledWindowClassification(stillPresent, confirmedUnchanged, disappeared);
    }

    /// <summary>What the server's answer alone says about one window, before anything MailFathom did is taken into account.</summary>
    /// <param name="StillPresent">The occurrences the server described, each beside the stored state it is compared against.</param>
    /// <param name="ConfirmedUnchanged">The occurrences the server confirmed without describing.</param>
    /// <param name="Disappeared">
    /// The occurrences the folder no longer holds, still carrying their UIDs because that is what a mutation record is
    /// matched against.
    /// </param>
    private sealed record ReconciledWindowClassification(
        IReadOnlyList<ObservedWindowCandidate> StillPresent,
        IReadOnlyList<StoredEmailId> ConfirmedUnchanged,
        IReadOnlyList<StoredEmailAwaitingReconciliation> Disappeared)
    {
        /// <summary>Gets the flags to write, which is all the store applying the outcome needs of a described occurrence.</summary>
        internal IReadOnlyList<ObservedEmailFlags> ObservedFlags =>
        [
            .. this.StillPresent.Select(static observed =>
                new ObservedEmailFlags(observed.Candidate.StoredEmailId, observed.Snapshot)),
        ];
    }

    /// <summary>One occurrence the server described, paired with what the last observation of it had recorded.</summary>
    /// <param name="Candidate">The window entry, which carries the local identity and the previously observed flag.</param>
    /// <param name="Snapshot">What the server has now reported.</param>
    private sealed record ObservedWindowCandidate(
        StoredEmailAwaitingReconciliation Candidate,
        RemoteEmailFlagSnapshot Snapshot)
    {
        /// <summary>Gets whether the remote <c>\Seen</c> flag stands somewhere other than where it was last seen.</summary>
        /// <remarks>
        /// An occurrence with no previous reading reports no movement. Nothing changed for it — this is the first time
        /// anybody looked — and calling that a change would raise a trigger for every message a backfill stores.
        /// </remarks>
        internal bool SeenStateMoved =>
            this.Candidate.LastObservation is { } lastObservation && lastObservation.IsSeen != this.Snapshot.IsSeen;

        /// <summary>Gets whether the remote <c>\Flagged</c> flag stands somewhere other than where it was last seen.</summary>
        /// <remarks>Absent a previous reading it reports no movement, for the reason the <c>\Seen</c> question gives.</remarks>
        internal bool FlaggedStateMoved =>
            this.Candidate.LastObservation is { } lastObservation
            && lastObservation.IsFlagged != this.Snapshot.IsFlagged;

        /// <summary>Gets whether the occurrence carries keywords other than the ones it last carried.</summary>
        /// <remarks>
        /// The whole set is compared, folded, by the equality <see cref="RemoteEmailKeywords" /> defines: a keyword
        /// gained and a keyword lost are the same finding here, because that is the granularity a server reports and the
        /// granularity a replacement writes. Absent a previous reading it reports no movement, for the reason the
        /// <c>\Seen</c> question gives.
        /// </remarks>
        internal bool KeywordsMoved =>
            this.Candidate.LastObservation is { } lastObservation
            && lastObservation.Keywords != this.Snapshot.Keywords;
    }

    /// <summary>One value of one occurrence that moved, and the record that accounts for the move where one does.</summary>
    /// <param name="Kind">Which value moved.</param>
    /// <param name="StoredEmailId">The local email it moved on.</param>
    /// <param name="Record">The mutation that put it where the server now reports it, or <see langword="null" /> when nothing MailFathom did explains it.</param>
    private sealed record AttributedFlagChange(
        MailboxChangeKind Kind,
        StoredEmailId StoredEmailId,
        MailboxMutationRecord? Record);

    /// <summary>What one window's moved flags and keywords split into once the mutation record has answered for them.</summary>
    /// <param name="ExternalSeenStateCount">How many moved <c>\Seen</c> flags no record accounts for, which are the mailbox owner's own.</param>
    /// <param name="ExternalFlaggedStateCount">How many moved <c>\Flagged</c> flags no record accounts for.</param>
    /// <param name="ExternalKeywordsCount">How many changed keyword sets no record accounts for.</param>
    /// <param name="Suppressed">The ones MailFathom wrote itself, each named with the record that says so.</param>
    private sealed record ReconciledFlagChanges(
        int ExternalSeenStateCount,
        int ExternalFlaggedStateCount,
        int ExternalKeywordsCount,
        IReadOnlyList<SuppressedMailboxChange> Suppressed)
    {
        /// <summary>Gets the split of a window in which nothing a <c>STORE</c> writes moved at all.</summary>
        internal static ReconciledFlagChanges None { get; } = new(
            ExternalSeenStateCount: 0,
            ExternalFlaggedStateCount: 0,
            ExternalKeywordsCount: 0,
            Suppressed: []);
    }
}

/// <summary>Summarizes one bounded reconciliation window.</summary>
/// <param name="ObservedEmailCount">How many stored occurrences the server still holds, whether it described them or only confirmed them.</param>
/// <param name="RemotelyDeletedEmailCount">How many stored occurrences the folder no longer holds and nothing MailFathom did accounts for.</param>
/// <param name="OwnMutationCompletedEmailCount">
/// How many stored occurrences left the folder because MailFathom relocated or deleted them. They are counted apart from
/// the remotely deleted ones because they are the opposite finding: a change of the owner's own that has come back
/// through synchronization, rather than one to react to.
/// </param>
/// <param name="SeenStateChangedEmailCount">
/// How many stored emails the server reported a moved <c>\Seen</c> flag for that no mutation of MailFathom's accounts
/// for. Those are the mailbox owner's own act — read in their client, or marked read there — and they stay a change to
/// react to, which is what keeps the suppression beside them about provenance rather than about the flag.
/// </param>
/// <param name="FlaggedStateChangedEmailCount">
/// How many stored emails the server reported a moved <c>\Flagged</c> flag for that no mutation of MailFathom's
/// accounts for, which is the owner starring or unstarring mail in their own client.
/// </param>
/// <param name="KeywordsChangedEmailCount">
/// How many stored emails the server reported different keywords for that no mutation of MailFathom's accounts for,
/// which is the owner labelling mail in their own client. It counts emails rather than keywords, because a set is what
/// a server reports and what a comparison decides.
/// </param>
/// <param name="EmailsRemain">Whether occurrences still await reconciliation after this window.</param>
/// <param name="ReconciledThroughModSeq">
/// The folder modification sequence this pass covered the whole folder through, or <see langword="null" /> when the
/// pass was partial or the server reports no sequence. A caller records it on the folder's checkpoint.
/// </param>
/// <param name="SuppressedChanges">
/// The changes this window found and did not raise, because MailFathom itself had made them. The list is bounded by the
/// mutations MailFathom has in flight rather than by the size of the window, so it is a handful on the runs that have
/// any and empty on every run of a mailbox nobody writes to.
/// </param>
/// <remarks>
/// Every field is a count, a sequence number, or an identity MailFathom owns. Nothing derived from a message belongs in
/// a result a worker logs, which is what makes the audit line this becomes safe to emit for a mailbox nobody may read.
/// </remarks>
public sealed record MailboxReconciliationResult(
    int ObservedEmailCount,
    int RemotelyDeletedEmailCount,
    int OwnMutationCompletedEmailCount,
    int SeenStateChangedEmailCount,
    int FlaggedStateChangedEmailCount,
    int KeywordsChangedEmailCount,
    bool EmailsRemain,
    ulong? ReconciledThroughModSeq,
    IReadOnlyList<SuppressedMailboxChange> SuppressedChanges)
{
    /// <summary>Gets the result of a run whose folder had nothing awaiting reconciliation.</summary>
    /// <remarks>
    /// It carries no modification sequence even though an empty queue is a folder with nothing left to reconcile. The
    /// window was never read, so no sequence was observed to record, and inventing one from a later read would claim
    /// coverage of changes nobody looked at.
    /// </remarks>
    public static MailboxReconciliationResult NothingToReconcile { get; } = new(
        ObservedEmailCount: 0,
        RemotelyDeletedEmailCount: 0,
        OwnMutationCompletedEmailCount: 0,
        SeenStateChangedEmailCount: 0,
        FlaggedStateChangedEmailCount: 0,
        KeywordsChangedEmailCount: 0,
        EmailsRemain: false,
        ReconciledThroughModSeq: null,
        SuppressedChanges: []);
}
