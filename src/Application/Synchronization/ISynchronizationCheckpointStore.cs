// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Domain.Accounts;
using MailMcp.Domain.Folders;
using MailMcp.Domain.Synchronization;

namespace MailMcp.Application.Synchronization;

/// <summary>Persists synchronization checkpoints for mailbox folders.</summary>
public interface ISynchronizationCheckpointStore
{
    /// <summary>Gets the last durable checkpoint for a folder.</summary>
    Task<SynchronizationCheckpoint?> GetCheckpointAsync(
        MailAccountId accountId,
        MailFolderName folderName,
        CancellationToken cancellationToken);

    /// <summary>Saves the durable checkpoint for a folder within the supplied persistence session.</summary>
    Task SaveCheckpointAsync(
        ISession session,
        MailAccountId accountId,
        MailFolderName folderName,
        SynchronizationCheckpoint checkpoint,
        CancellationToken cancellationToken);
}
