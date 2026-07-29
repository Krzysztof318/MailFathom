// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.EmailContent;
using MailMcp.Domain.Emails;

namespace MailMcp.Application.Synchronization;

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
    /// <param name="cancellationToken">Cancels the read and every remaining attempt.</param>
    /// <returns>The page, with the UID it inspected through and whether more work remains.</returns>
    /// <exception cref="MailboxUnavailableException">Thrown when the mail server did not serve the read within its configured resilience budget.</exception>
    /// <exception cref="MailboxFolderRecreatedException">Thrown when a recovered connection reselected the folder with a different UIDVALIDITY.</exception>
    Task<RemoteEmailMetadataBatch> GetEmailBatchAfterAsync(
        ImapUid? lastSeenUid,
        int maxEmailCount,
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
