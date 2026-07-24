// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.MessageContent;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Folders;
using MailMcp.Domain.Messages;
using MailMcp.Domain.Synchronization;

namespace MailMcp.Application.Synchronization;

/// <summary>Persists synchronization checkpoints for mailbox folders.</summary>
public interface ISynchronizationCheckpointStore
{
    /// <summary>Gets the last durable checkpoint for a folder.</summary>
    Task<SynchronizationCheckpoint?> GetCheckpointAsync(MailAccountId accountId, MailFolderName folderName, CancellationToken cancellationToken);

    /// <summary>Saves the durable checkpoint for a folder.</summary>
    Task SaveCheckpointAsync(IMailSynchronizationUnitOfWorkSession session, MailAccountId accountId, MailFolderName folderName, SynchronizationCheckpoint checkpoint, CancellationToken cancellationToken);
}

/// <summary>Creates explicit unit-of-work sessions for synchronization writes that span content, metadata, and checkpoints.</summary>
public interface IMailSynchronizationUnitOfWorkFactory
{
    /// <summary>Begins a short-lived provider-neutral transaction session for one local synchronization write batch.</summary>
    Task<IMailSynchronizationUnitOfWorkSession> BeginSynchronizationWriteAsync(CancellationToken cancellationToken);
}

/// <summary>Represents an explicit synchronization persistence session shared by repositories participating in one transaction.</summary>
public interface IMailSynchronizationUnitOfWorkSession : IAsyncDisposable
{
    /// <summary>Commits all repository writes joined to this session. The session is invalid for further use after commit.</summary>
    Task CommitAsync(CancellationToken cancellationToken);
}

/// <summary>Persists message metadata independently from raw MIME content.</summary>
public interface IMessageMetadataRepository
{
    /// <summary>Inserts or updates metadata for one remote occurrence idempotently.</summary>
    Task UpsertMetadataAsync(IMailSynchronizationUnitOfWorkSession session, RemoteMessageMetadata metadata, CancellationToken cancellationToken);
}

/// <summary>Creates IMAP sessions exposed only through application-owned mail operations.</summary>
public interface IImapMailboxSessionFactory
{
    /// <summary>Opens a folder read-only so synchronization cannot mutate remote mailbox state.</summary>
    Task<IImapMailboxSession> OpenReadOnlyAsync(MailAccountId accountId, MailFolderName folderName, CancellationToken cancellationToken);
}

/// <summary>Represents a read-only IMAP folder session.</summary>
public interface IImapMailboxSession : IAsyncDisposable
{
    /// <summary>Gets the selected folder UIDVALIDITY value.</summary>
    Task<ImapUidValidity> GetUidValidityAsync(CancellationToken cancellationToken);

    /// <summary>Gets a bounded remote message metadata page after the supplied checkpoint UID.</summary>
    Task<RemoteMessageMetadataBatch> GetMessageBatchAfterAsync(ImapUid? lastSeenUid, int maxMessageCount, CancellationToken cancellationToken);

    /// <summary>Fetches raw MIME content with a BODY.PEEK-style operation that preserves the remote Seen flag.</summary>
    Task<RemoteMessageContent> FetchMessageContentWithoutSettingSeenAsync(MessageOccurrenceId occurrenceId, long maxRawMimeBytes, CancellationToken cancellationToken);
}
