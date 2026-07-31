// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.Persistence;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Emails;
using MailMcp.Domain.Folders;

namespace MailMcp.Application.Synchronization;

/// <summary>The local state reconciliation reads to choose a window and writes once the server has answered.</summary>
/// <remarks>
/// <para>
/// The three operations describe one bounded backward pass: which stored occurrences have gone longest without being
/// checked, what the server said about the ones it still holds, and what becomes of the ones it no longer holds. They
/// belong to one port because a caller holding only some of them could not make the pass terminate.
/// </para>
/// <para>
/// There is no cursor to keep. The window is chosen by how long ago each occurrence was last observed, so writing an
/// observation is itself what advances the pass, and a run interrupted between two windows resumes where it stopped
/// rather than restarting or skipping.
/// </para>
/// </remarks>
public interface IStoredEmailReconciliationStore
{
    /// <summary>Reads the stored occurrences of one folder binding that have gone longest without a flag observation.</summary>
    /// <param name="accountId">The account the folder belongs to.</param>
    /// <param name="folderResolutionId">The alias binding whose occurrences are reconciled.</param>
    /// <param name="uidValidity">The UIDVALIDITY the open session reports, which the returned occurrences must have been stored under.</param>
    /// <param name="maxEmailCount">The greatest number of occurrences to return.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The occurrences to ask about, never more than <paramref name="maxEmailCount" />.</returns>
    /// <remarks>
    /// <para>
    /// The UIDVALIDITY is a filter rather than a formality, and it is what keeps a server-side renumbering from
    /// emptying the local mailbox. Occurrences stored under a UIDVALIDITY the folder no longer reports name messages
    /// the current UID space says nothing about, so they are outside every window instead of being reported missing.
    /// </para>
    /// <para>
    /// An occurrence already recorded as remotely deleted is likewise outside every window. The server has nothing left
    /// to say about it, and asking again would spend the window on work whose answer is already durable.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<StoredEmailAwaitingReconciliation>> GetLeastRecentlyObservedAsync(
        MailAccountId accountId,
        MailFolderResolutionId folderResolutionId,
        ImapUidValidity uidValidity,
        int maxEmailCount,
        CancellationToken cancellationToken);

    /// <summary>Writes the flags the server reported for one occurrence it still holds.</summary>
    /// <param name="session">The explicit persistence session this write participates in.</param>
    /// <param name="storedEmailId">The occurrence the flags were read for.</param>
    /// <param name="snapshot">What the server reported, and when it was read.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>A task that completes when the write has been staged.</returns>
    /// <remarks>
    /// The observation timestamp is written with the flags rather than beside them, because it is what places the
    /// occurrence at the back of the reconciliation queue. Writing the flags without it would leave the same window
    /// selected on every run.
    /// </remarks>
    Task RecordFlagObservationAsync(
        IPersistenceSession session,
        StoredEmailId storedEmailId,
        RemoteEmailFlagSnapshot snapshot,
        CancellationToken cancellationToken);

    /// <summary>Records that the server no longer holds one occurrence, in the form the configured disposition names.</summary>
    /// <param name="session">The explicit persistence session this write participates in.</param>
    /// <param name="storedEmailId">The occurrence the folder no longer holds.</param>
    /// <param name="disposition">Whether the local row survives as a tombstone or is removed with everything derived from it.</param>
    /// <param name="observedAt">When the disappearance was observed.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>A task that completes when the write has been staged.</returns>
    /// <remarks>
    /// The write is idempotent for both dispositions: a tombstone keeps the timestamp of the run that first observed
    /// the disappearance rather than being restamped, and an occurrence already removed is not an error to remove
    /// again. An implementation removing the row removes the raw MIME, the derived text and index entry, and every
    /// other artifact derived from the message with it, because a deletion that leaves derived data behind is not one.
    /// </remarks>
    Task RecordRemoteDeletionAsync(
        IPersistenceSession session,
        StoredEmailId storedEmailId,
        RemotelyDeletedEmailDisposition disposition,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken);
}
