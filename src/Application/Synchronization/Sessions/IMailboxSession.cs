// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Synchronization.Reconciliation;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Synchronization;

namespace MailFathom.Application.Synchronization.Sessions;

/// <summary>Represents a read-only mailbox folder session.</summary>
/// <remarks>
/// An implementation is free to re-establish its connection between operations, so a session outlives one dropped
/// socket. What it may never do is change what the folder is: a reselected folder that reports a new UIDVALIDITY is a
/// different folder, and the session reports <see cref="MailboxFolderRecreatedException" /> instead of serving it.
/// </remarks>
public interface IMailboxSession : IAsyncDisposable
{
    /// <summary>Gets the selected folder UIDVALIDITY value.</summary>
    /// <param name="cancellationToken">Cancels re-establishing the session when the connection was lost.</param>
    /// <returns>The UIDVALIDITY the folder is selected with.</returns>
    /// <exception cref="MailboxUnavailableException">Thrown when the mail server did not serve the session within its configured resilience budget.</exception>
    Task<ImapUidValidity> GetUidValidityAsync(CancellationToken cancellationToken);

    /// <summary>Gets a bounded remote email metadata page after the supplied checkpoint UID.</summary>
    /// <param name="lastSeenUid">The checkpoint to read past, or <see langword="null" /> to start at the first assigned UID.</param>
    /// <param name="maxEmailCount">The maximum number of emails the returned page may describe.</param>
    /// <param name="synchronizationWindow">How far back the page may reach, which the implementation must apply on the server rather than to the answer.</param>
    /// <param name="cancellationToken">Cancels the read and every remaining attempt.</param>
    /// <returns>The page, with the UID it inspected through and whether more work remains.</returns>
    /// <exception cref="MailboxUnavailableException">Thrown when the mail server did not serve the read within its configured resilience budget.</exception>
    /// <exception cref="MailboxFolderRecreatedException">Thrown when a recovered connection reselected the folder with a different UIDVALIDITY.</exception>
    /// <remarks>
    /// An email the window excludes is not described by the page, and the returned cursor still covers it: the page
    /// reports the UID the search inspected through rather than the UID it last described, so a caller advancing its
    /// checkpoint by that cursor steps over an excluded range once instead of rescanning it on every run. That is also
    /// why the window belongs on the server's side of the call — an excluded UID must cost no fetch.
    /// </remarks>
    Task<RemoteEmailMetadataBatch> GetEmailBatchAfterAsync(
        ImapUid? lastSeenUid,
        int maxEmailCount,
        MailSynchronizationWindow synchronizationWindow,
        CancellationToken cancellationToken);

    /// <summary>Reports which of the supplied occurrences the folder still holds, with the flags the server shows for them.</summary>
    /// <param name="uids">The UIDs to ask about, which must belong to this session's folder and UIDVALIDITY.</param>
    /// <param name="reconciledThroughModSeq">
    /// The modification sequence the whole folder was last reconciled through, or <see langword="null" /> to ask about
    /// every supplied UID without regard to what has changed since.
    /// </param>
    /// <param name="cancellationToken">Cancels the read and every remaining attempt.</param>
    /// <returns>What the folder still holds out of <paramref name="uids" />, described or merely confirmed, and the folder's modification sequence where it reports one.</returns>
    /// <exception cref="MailboxUnavailableException">Thrown when the mail server did not serve the read within its configured resilience budget.</exception>
    /// <exception cref="MailboxFolderRecreatedException">Thrown when a recovered connection reselected the folder with a different UIDVALIDITY.</exception>
    /// <exception cref="MailboxAnswerIncompleteException">Thrown when the server answered for an email without the flags this operation requested.</exception>
    /// <remarks>
    /// <para>
    /// A UID the answer accounts for in neither list is one the folder no longer holds. That is the whole detection
    /// mechanism for a message deleted on the server, so an implementation must never invent an entry for a UID the
    /// server said nothing about, and must never omit one it did answer for.
    /// </para>
    /// <para>
    /// The supplied sequence is a permission rather than an instruction. An implementation may use it to ask the server
    /// only about what changed since — which is the point of accepting it — but only where the server also tells it
    /// which of the remaining UIDs still exist, because a sequence-limited answer alone cannot tell an unchanged
    /// message from a deleted one. Where the server offers no such mechanism, the implementation asks about the whole
    /// window and reports nothing as unchanged; the end state is identical either way, and only the work differs.
    /// </para>
    /// <para>
    /// An answer that names a UID without its flags is refused rather than dropped, because dropping it would turn a
    /// message the server proved exists into the silence a deleted message produces. An implementation reports that as
    /// a failure and lets the caller's next run ask again; nothing local may be derived from a partial answer.
    /// </para>
    /// <para>
    /// The operation reads flags and nothing else. It must not request a message body, a header, or any other item whose
    /// retrieval sets the remote <c>\Seen</c> flag, and it must not write a flag back: this is the path that inspects
    /// mail somebody has already stored, and a careless fetch here would mark a whole mailbox as read.
    /// </para>
    /// </remarks>
    Task<RemoteFolderWindowObservation> ObserveWindowWithoutSettingSeenAsync(
        IReadOnlyList<ImapUid> uids,
        ulong? reconciledThroughModSeq,
        CancellationToken cancellationToken);

    /// <summary>Fetches raw MIME content with a BODY.PEEK-style operation that preserves the remote Seen flag.</summary>
    /// <param name="occurrenceId">The occurrence to fetch, which must belong to this session's account, folder, and UIDVALIDITY.</param>
    /// <param name="maxRawMimeBytes">The size beyond which the payload is abandoned rather than buffered.</param>
    /// <param name="cancellationToken">Cancels the fetch and every remaining attempt.</param>
    /// <returns>The raw MIME content of the occurrence, or the statement that it exceeded <paramref name="maxRawMimeBytes" />.</returns>
    /// <exception cref="MailboxUnavailableException">Thrown when the mail server did not serve the fetch within its configured resilience budget.</exception>
    /// <exception cref="MailboxFolderRecreatedException">Thrown when a recovered connection reselected the folder with a different UIDVALIDITY.</exception>
    /// <remarks>
    /// An oversized payload is a result rather than a failure, because a caller records the occurrence and steps over it
    /// instead of stopping the run. The preservation guarantee holds on every attempt: an implementation that recovers a
    /// lost connection must reselect the folder read-only before it fetches again.
    /// </remarks>
    Task<RemoteEmailContentFetchResult> FetchEmailContentWithoutSettingSeenAsync(
        EmailOccurrenceId occurrenceId,
        long maxRawMimeBytes,
        CancellationToken cancellationToken);
}
