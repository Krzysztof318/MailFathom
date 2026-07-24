// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.MessageContent;
using MailMcp.Domain.Messages;

namespace MailMcp.Application.Synchronization;

/// <summary>Represents a read-only mailbox folder session.</summary>
public interface IMailboxSession : IAsyncDisposable
{
    /// <summary>Gets the selected folder UIDVALIDITY value.</summary>
    Task<ImapUidValidity> GetUidValidityAsync(CancellationToken cancellationToken);

    /// <summary>Gets a bounded remote message metadata page after the supplied checkpoint UID.</summary>
    Task<RemoteMessageMetadataBatch> GetMessageBatchAfterAsync(
        ImapUid? lastSeenUid,
        int maxMessageCount,
        CancellationToken cancellationToken);

    /// <summary>Fetches raw MIME content with a BODY.PEEK-style operation that preserves the remote Seen flag.</summary>
    Task<RemoteMessageContent> FetchMessageContentWithoutSettingSeenAsync(
        MessageOccurrenceId occurrenceId,
        long maxRawMimeBytes,
        CancellationToken cancellationToken);
}
