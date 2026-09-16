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

/// <summary>Follows the occurrences a delete MailFathom authored only flagged <c>\Deleted</c>, for one folder.</summary>
/// <remarks>
/// <para>
/// Such a delete never makes its occurrence disappear, so the backward pass, which reads a delete as complete when the
/// occurrence is gone, would never settle it. This pass does instead. The first reading that still sees the flag applies
/// the local disposition the delete was authored under and starts following the occurrence; every later reading asks
/// whether the server still holds it flagged, expunged it, or has had the flag removed — which undoes the delete, so the
/// message comes back as live mail, stored again where its local copy had been erased.
/// </para>
/// <para>
/// It runs before the backward pass of the same folder, so a row it disposes of is already out of that pass's window.
/// A delete whose occurrence is gone before any reading saw the flag is left to the backward pass, which attributes the
/// disappearance to the delete and applies its disposition exactly as it does for a delete that expunged.
/// </para>
/// <para>
/// Everything it asks the server is read-only: flags, for occurrences the folder was already asked about, and nothing
/// that could set <c>\Seen</c>. Both reads are bounded per run by the same limit as the backward window.
/// </para>
/// </remarks>
public sealed class FlaggedDeleteFollower
{
    private readonly IStoredEmailReconciliationStore reconciliationStore;
    private readonly IMailboxMutationReconciliationStore mutationStore;
    private readonly OptimisticConcurrencyRetryPolicy concurrencyRetryPolicy;
    private readonly ClientSignals signals;
    private readonly TimeProvider timeProvider;
    private readonly MailboxSynchronizationOptions options;

    /// <summary>Initializes a new instance of the <see cref="FlaggedDeleteFollower" /> class.</summary>
    /// <param name="reconciliationStore">Reads the followed occurrences and applies what the server said about them.</param>
    /// <param name="mutationStore">Reads the deletes whose flag has not been seen yet, and records that it has.</param>
    /// <param name="concurrencyRetryPolicy">Commits one follow-up, retrying a conflict with a competing writer.</param>
    /// <param name="signals">Tells an open client which stored mail left or came back.</param>
    /// <param name="timeProvider">Stamps the reading.</param>
    /// <param name="options">Bounds one follow-up.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public FlaggedDeleteFollower(
        IStoredEmailReconciliationStore reconciliationStore,
        IMailboxMutationReconciliationStore mutationStore,
        OptimisticConcurrencyRetryPolicy concurrencyRetryPolicy,
        ClientSignals signals,
        TimeProvider timeProvider,
        MailboxSynchronizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(reconciliationStore);
        ArgumentNullException.ThrowIfNull(mutationStore);
        ArgumentNullException.ThrowIfNull(concurrencyRetryPolicy);
        ArgumentNullException.ThrowIfNull(signals);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(options);

        this.reconciliationStore = reconciliationStore;
        this.mutationStore = mutationStore;
        this.concurrencyRetryPolicy = concurrencyRetryPolicy;
        this.signals = signals;
        this.timeProvider = timeProvider;
        this.options = options;
    }

    /// <summary>Settles and follows one bounded batch of the folder's flag-only deletes.</summary>
    /// <param name="mailboxSession">The open read-only session of the folder.</param>
    /// <param name="account">The account the folder belongs to.</param>
    /// <param name="folder">The alias binding being synchronized.</param>
    /// <param name="uidValidity">The UIDVALIDITY the session reports, which every occurrence asked about was recorded under.</param>
    /// <param name="cancellationToken">Cancels the pass between the remote read and the local write.</param>
    /// <returns>What the caller still owes: the erased messages to store again, and the changes it must not raise.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="mailboxSession" /> or <paramref name="folder" /> is <see langword="null" />.</exception>
    /// <exception cref="MailboxUnavailableException">Thrown when the mail server did not serve the read within its configured resilience budget.</exception>
    /// <exception cref="PersistenceConcurrencyConflictException">Thrown when a competing writer wins a race the bounded retries could not resolve.</exception>
    public async Task<FlaggedDeleteFollowUp> FollowAsync(
        IMailboxSession mailboxSession,
        MailAccountId account,
        MailFolderResolution folder,
        ImapUidValidity uidValidity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mailboxSession);
        ArgumentNullException.ThrowIfNull(folder);

        var unsettled = await this.mutationStore.ReadUnsettledFlaggedDeletesAsync(
            account,
            folder.Id,
            uidValidity,
            this.options.MaxReconciledEmailsPerRun,
            cancellationToken);
        var followed = await this.reconciliationStore.GetDeletesLeftFlaggedAsync(
            account,
            folder.Id,
            uidValidity,
            this.options.MaxReconciledEmailsPerRun,
            cancellationToken);

        if (unsettled.Count == 0 && followed.Count == 0)
        {
            return FlaggedDeleteFollowUp.Nothing;
        }

        var observation = await mailboxSession.ObserveWindowWithoutSettingSeenAsync(
            [.. unsettled.Select(static record => record.Request.Occurrence.Uid).Concat(followed.Select(static entry => entry.Uid)).Distinct()],
            reconciledThroughModSeq: null,
            cancellationToken);
        var readings = new FlagReadings(observation);
        var observedAt = this.timeProvider.GetUtcNow();

        // A delete seen already unflagged was undone on the server before anything local happened, so it is settled with
        // nothing to dispose of. One whose occurrence was not described at all is left alone: the backward pass reads a
        // gone occurrence as this delete completing, and an unchanged one said nothing about the flag.
        var answered = unsettled.Where(record => readings.IsDescribed(record.Request.Occurrence.Uid)).ToArray();
        var settlement = new FlaggedDeleteSettlement(
            [
                .. answered
                    .Where(record => readings.IsFlagged(record.Request.Occurrence.Uid))
                    .Select(static record => new SettledFlaggedDelete(
                        record.Request.StoredEmailId,
                        record.Request.LocalDisposition!.Value)),
            ],
            [.. followed.Where(entry => readings.IsFlagged(entry.Uid))],
            [.. followed.Where(entry => readings.IsGone(entry.Uid))],
            [.. followed.Where(entry => entry.KeptEmail is not null && readings.IsUnflagged(entry.Uid))],
            observedAt);

        await this.concurrencyRetryPolicy.CommitAsync(
            async (persistenceSession, attemptCancellationToken) =>
            {
                foreach (var record in answered)
                {
                    await this.mutationStore.RecordDeleteFlagSettledAsync(
                        persistenceSession,
                        record.Id,
                        observedAt,
                        attemptCancellationToken);
                }

                await this.reconciliationStore.ApplyFlaggedDeleteSettlementAsync(
                    persistenceSession,
                    settlement,
                    attemptCancellationToken);
            },
            cancellationToken);

        this.AnnounceWhatMoved(account, folder.Alias, settlement);

        return new FlaggedDeleteFollowUp(
            [.. followed.Where(entry => entry.KeptEmail is null && readings.IsUnflagged(entry.Uid))],
            [
                .. answered
                    .Where(record => readings.IsFlagged(record.Request.Occurrence.Uid))
                    .Select(static record => new SuppressedMailboxChange(
                        MailboxChangeKind.EmailLeftFolder,
                        record.Request.Mutation,
                        record.Request.StoredEmailId,
                        record.Id)),
            ]);
    }

    /// <summary>Stops following an occurrence whose erased message the run has stored again, or found it could not.</summary>
    /// <param name="undeleted">One of the occurrences a follow-up returned as awaiting restore.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the record is gone.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="undeleted" /> is <see langword="null" />.</exception>
    public Task RetireAsync(DeleteLeftFlagged undeleted, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(undeleted);

        return this.concurrencyRetryPolicy.CommitAsync(
            (persistenceSession, attemptCancellationToken) => this.reconciliationStore.RetireDeletesLeftFlaggedAsync(
                persistenceSession,
                [undeleted.Id],
                attemptCancellationToken),
            cancellationToken);
    }

    /// <summary>Tells an open client which stored mail left its folder or came back, once the follow-up is committed.</summary>
    private void AnnounceWhatMoved(MailAccountId account, MailFolderAlias folder, FlaggedDeleteSettlement settlement)
    {
        StoredEmailId[] moved =
        [
            .. settlement.Settled.Select(static settled => settled.StoredEmailId),
            .. settlement.Restored.Select(static entry => entry.KeptEmail!.Value),
        ];

        if (moved.Length == 0 || !this.signals.Reaches)
        {
            return;
        }

        this.signals.Publish(ClientSignal.MailChanged(account, folder, moved));
    }

    /// <summary>What the server said about each UID a follow-up asked about.</summary>
    /// <remarks>
    /// A UID the server confirmed without describing still exists, but its flag was not reported, so it is neither
    /// flagged, unflagged, nor gone here and the next run asks again.
    /// </remarks>
    private sealed class FlagReadings(RemoteFolderWindowObservation observation)
    {
        private readonly Dictionary<ImapUid, bool> deletedByUid = observation.Observations
            .DistinctBy(static described => described.Uid)
            .ToDictionary(static described => described.Uid, static described => described.Snapshot.IsDeleted);

        private readonly HashSet<ImapUid> unchanged = [.. observation.UnchangedUids];

        internal bool IsDescribed(ImapUid uid) => this.deletedByUid.ContainsKey(uid);

        internal bool IsFlagged(ImapUid uid) => this.deletedByUid.TryGetValue(uid, out var deleted) && deleted;

        internal bool IsUnflagged(ImapUid uid) => this.deletedByUid.TryGetValue(uid, out var deleted) && !deleted;

        internal bool IsGone(ImapUid uid) => !this.deletedByUid.ContainsKey(uid) && !this.unchanged.Contains(uid);
    }
}

/// <summary>What a follow-up of a folder's flag-only deletes leaves for the synchronization run to finish.</summary>
/// <param name="AwaitingRestore">
/// The occurrences whose flag was removed on the server after their local copy was erased. The run stores each message
/// again and then retires its record; a run that cannot leaves the record for the next one.
/// </param>
/// <param name="SuppressedChanges">The emails this follow-up took out of their folder, which MailFathom's own delete accounts for.</param>
public sealed record FlaggedDeleteFollowUp(
    IReadOnlyList<DeleteLeftFlagged> AwaitingRestore,
    IReadOnlyList<SuppressedMailboxChange> SuppressedChanges)
{
    /// <summary>Gets the follow-up of a folder with no flag-only delete to settle or follow.</summary>
    public static FlaggedDeleteFollowUp Nothing { get; } = new([], []);
}
