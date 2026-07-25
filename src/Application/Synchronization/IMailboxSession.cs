// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.EmailContent;
using MailMcp.Domain.Emails;

namespace MailMcp.Application.Synchronization;

/// <summary>Represents a read-only mailbox folder session.</summary>
public interface IMailboxSession : IAsyncDisposable
{
    /// <summary>Gets the selected folder UIDVALIDITY value.</summary>
    Task<ImapUidValidity> GetUidValidityAsync(CancellationToken cancellationToken);

    /// <summary>Gets a bounded remote email metadata page after the supplied checkpoint UID.</summary>
    Task<RemoteEmailMetadataBatch> GetEmailBatchAfterAsync(
        ImapUid? lastSeenUid,
        int maxEmailCount,
        CancellationToken cancellationToken);

    /// <summary>Fetches raw MIME content with a BODY.PEEK-style operation that preserves the remote Seen flag.</summary>
    Task<RemoteEmailContent> FetchEmailContentWithoutSettingSeenAsync(
        EmailOccurrenceId occurrenceId,
        long maxRawMimeBytes,
        CancellationToken cancellationToken);
}
