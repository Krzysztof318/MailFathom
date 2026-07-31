// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.Persistence;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Emails;
using MailMcp.Domain.Folders;

namespace MailMcp.Application.Synchronization;

/// <summary>The local state reconciliation reads to choose a window and writes once the server has answered.</summary>
/// <remarks>
/// <para>
/// The two operations describe one bounded backward pass: which stored occurrences this run should ask about, and what
/// the server said about them. They belong to one port because a caller holding only one of them could not make the
/// pass terminate.
/// </para>
/// <para>
/// There is no cursor to keep. A window is chosen by how long ago each occurrence was last observed, so writing the
/// outcome is itself what advances the pass, and a run interrupted between two windows resumes where it stopped rather
/// than restarting or skipping.
/// </para>
/// </remarks>
public interface IStoredEmailReconciliationStore
{
    /// <summary>Reads the occurrences of one folder binding that this run should ask the server about.</summary>
    /// <param name="accountId">The account the folder belongs to.</param>
    /// <param name="folderResolutionId">The alias binding whose occurrences are reconciled.</param>
    /// <param name="uidValidity">The UIDVALIDITY the open session reports, which the returned occurrences must have been stored under.</param>
    /// <param name="maxEmailCount">The greatest number of occurrences to return.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The occurrences to ask about, never more than <paramref name="maxEmailCount" />.</returns>
    /// <remarks>
    /// <para>
    /// The window leads with the occurrences observed longest ago and the never-observed ones before them, but it is
    /// not simply the first <paramref name="maxEmailCount" /> of that order. Part of it is reserved for occurrences
    /// that have been observed before, because the forward pass can store more new mail per run than one window holds:
    /// an order that took never-observed rows first would then spend every window on newly arrived mail, and a deletion
    /// or a flag change in the mail stored last month would never be noticed again. An implementation returns as much
    /// of each group as exists, so a folder with only one of them still fills the window.
    /// </para>
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
    Task<IReadOnlyList<StoredEmailAwaitingReconciliation>> GetReconciliationWindowAsync(
        MailAccountId accountId,
        MailFolderResolutionId folderResolutionId,
        ImapUidValidity uidValidity,
        int maxEmailCount,
        CancellationToken cancellationToken);

    /// <summary>Applies everything one window learned, as one bounded set of writes.</summary>
    /// <param name="session">The explicit persistence session the whole outcome participates in.</param>
    /// <param name="outcome">What the window found, and what becomes of the emails the folder no longer holds.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>A task that completes when the writes have been staged.</returns>
    /// <remarks>
    /// <para>
    /// The whole window is applied at once so the work is bounded by the window rather than by a query per email held
    /// inside an open write transaction. An implementation reads the rows it needs in one bounded query keyed by the
    /// identities the outcome names.
    /// </para>
    /// <para>
    /// Every write is idempotent and none of them moves state backwards, which is what makes replaying a window after a
    /// commit conflict safe. An occurrence whose stored observation is newer than this window's is left alone: another
    /// writer has since asked the same server and its answer supersedes this one, including when this window would have
    /// deleted the email. A tombstone keeps the timestamp of the run that first observed the disappearance, and a row
    /// another writer already removed is not an error to remove again.
    /// </para>
    /// <para>
    /// Removing a row removes the raw MIME, the derived text and index entry, and every other artifact derived from the
    /// message with it, because a deletion that leaves derived data behind is not one.
    /// </para>
    /// </remarks>
    Task ApplyReconciliationOutcomeAsync(
        IPersistenceSession session,
        ReconciledFolderOutcome outcome,
        CancellationToken cancellationToken);
}
