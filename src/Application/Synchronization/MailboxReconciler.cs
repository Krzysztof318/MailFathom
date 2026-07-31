// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Synchronization;

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
/// </remarks>
public sealed class MailboxReconciler
{
    private readonly IStoredEmailReconciliationStore reconciliationStore;
    private readonly IRemotelyDeletedEmailDispositionReader dispositionReader;
    private readonly OptimisticConcurrencyRetryPolicy concurrencyRetryPolicy;
    private readonly TimeProvider timeProvider;
    private readonly MailboxSynchronizationOptions options;

    /// <summary>Initializes a new mailbox reconciler.</summary>
    /// <param name="reconciliationStore">Chooses the window and records what the server answered.</param>
    /// <param name="dispositionReader">Answers what the account being reconciled does with an email its server no longer holds.</param>
    /// <param name="concurrencyRetryPolicy">Commits one window's outcome, retrying a conflict with a competing writer.</param>
    /// <param name="timeProvider">Stamps the observation, which is what advances the window across runs.</param>
    /// <param name="options">Bounds one window.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public MailboxReconciler(
        IStoredEmailReconciliationStore reconciliationStore,
        IRemotelyDeletedEmailDispositionReader dispositionReader,
        OptimisticConcurrencyRetryPolicy concurrencyRetryPolicy,
        TimeProvider timeProvider,
        MailboxSynchronizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(reconciliationStore);
        ArgumentNullException.ThrowIfNull(dispositionReader);
        ArgumentNullException.ThrowIfNull(concurrencyRetryPolicy);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(options);

        this.reconciliationStore = reconciliationStore;
        this.dispositionReader = dispositionReader;
        this.concurrencyRetryPolicy = concurrencyRetryPolicy;
        this.timeProvider = timeProvider;
        this.options = options;
    }

    /// <summary>Reconciles one bounded window of the folder the supplied session has open.</summary>
    /// <param name="mailboxSession">The open read-only session, whose folder and UIDVALIDITY the window is chosen for.</param>
    /// <param name="accountId">The account being reconciled.</param>
    /// <param name="folder">The alias binding being reconciled.</param>
    /// <param name="uidValidity">The UIDVALIDITY the session reports for the open folder.</param>
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
    /// A folder whose UIDVALIDITY has changed reconciles nothing rather than deleting anything. The window is selected
    /// under the UIDVALIDITY the session reports, so occurrences stored under the previous one are simply not in it:
    /// the renumbering is left to the existing invalidation rule, and no mass local deletion can follow from it.
    /// </remarks>
    public async Task<MailboxReconciliationResult> ReconcileAsync(
        IMailboxSession mailboxSession,
        MailAccountId accountId,
        MailFolderResolution folder,
        ImapUidValidity uidValidity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mailboxSession);
        ArgumentNullException.ThrowIfNull(folder);

        var window = await this.reconciliationStore.GetReconciliationWindowAsync(
            accountId,
            folder.Id,
            uidValidity,
            this.options.MaxReconciledEmailsPerRun,
            cancellationToken);

        if (window.Count == 0)
        {
            return MailboxReconciliationResult.NothingToReconcile;
        }

        var observations = await mailboxSession.GetRemoteFlagsWithoutSettingSeenAsync(
            [.. window.Select(candidate => candidate.Uid)],
            cancellationToken);

        var outcome = ClassifyWindow(
            window,
            observations,
            this.dispositionReader.GetDisposition(accountId),
            this.timeProvider.GetUtcNow());

        await this.concurrencyRetryPolicy.CommitAsync(
            (persistenceSession, attemptCancellationToken) =>
                this.reconciliationStore.ApplyReconciliationOutcomeAsync(
                    persistenceSession,
                    outcome,
                    attemptCancellationToken),
            cancellationToken);

        return new MailboxReconciliationResult(
            outcome.StillPresent.Count,
            outcome.Disappeared.Count,
            EmailsRemain: window.Count == this.options.MaxReconciledEmailsPerRun);
    }

    /// <summary>Sorts the window into the occurrences the server answered for and the ones it said nothing about.</summary>
    /// <remarks>
    /// <para>
    /// Silence is the finding. An occurrence the server did not report is one the folder no longer holds, so the split
    /// is made against what came back rather than against any flag inside it — the <c>\Deleted</c> flag marks a message
    /// still present in the folder and is a different statement entirely.
    /// </para>
    /// <para>
    /// A UID the answer names twice keeps its first observation rather than failing the run. Nothing in the protocol
    /// promises one entry per UID, and a server that repeats one has said the message exists, which is the only thing
    /// this classification reads out of it.
    /// </para>
    /// </remarks>
    private static ReconciledFolderOutcome ClassifyWindow(
        IReadOnlyList<StoredEmailAwaitingReconciliation> window,
        IReadOnlyList<RemoteEmailFlagObservation> observations,
        RemotelyDeletedEmailDisposition disposition,
        DateTimeOffset observedAt)
    {
        var snapshotsByUid = observations
            .DistinctBy(static observation => observation.Uid)
            .ToDictionary(
                static observation => observation.Uid,
                static observation => observation.Snapshot);

        var stillPresent = window
            .Where(candidate => snapshotsByUid.ContainsKey(candidate.Uid))
            .Select(candidate => new ObservedEmailFlags(candidate.StoredEmailId, snapshotsByUid[candidate.Uid]))
            .ToArray();
        var disappeared = window
            .Where(candidate => !snapshotsByUid.ContainsKey(candidate.Uid))
            .Select(static candidate => candidate.StoredEmailId)
            .ToArray();

        return new ReconciledFolderOutcome(stillPresent, disappeared, disposition, observedAt);
    }
}

/// <summary>Summarizes one bounded reconciliation window.</summary>
/// <param name="ObservedEmailCount">How many stored occurrences the server still holds and had their flag snapshot refreshed.</param>
/// <param name="RemotelyDeletedEmailCount">How many stored occurrences the folder no longer holds.</param>
/// <param name="EmailsRemain">Whether occurrences still await reconciliation after this window.</param>
/// <remarks>
/// Every field is a count. Nothing derived from a message belongs in a result a worker logs, which is what makes the
/// audit line this becomes safe to emit for a mailbox nobody may read.
/// </remarks>
public sealed record MailboxReconciliationResult(
    int ObservedEmailCount,
    int RemotelyDeletedEmailCount,
    bool EmailsRemain)
{
    /// <summary>Gets the result of a run whose folder had nothing awaiting reconciliation.</summary>
    public static MailboxReconciliationResult NothingToReconcile { get; } = new(
        ObservedEmailCount: 0,
        RemotelyDeletedEmailCount: 0,
        EmailsRemain: false);
}
