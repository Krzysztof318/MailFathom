// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.Persistence;
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

    /// <summary>Stages a checkpoint update only when the durable state still matches the state previously read.</summary>
    Task<SynchronizationCheckpointSaveResult> SaveCheckpointAsync(
        IPersistenceSession session,
        MailAccountId accountId,
        MailFolderName folderName,
        SynchronizationCheckpoint? expectedCheckpoint,
        SynchronizationCheckpoint checkpoint,
        CancellationToken cancellationToken);
}

/// <summary>Describes whether a synchronization checkpoint update was staged.</summary>
public enum SynchronizationCheckpointSaveResult
{
    /// <summary>The durable state matched the expected state and the update was staged.</summary>
    Staged = 0,

    /// <summary>The durable state changed after it was read, so no update was staged.</summary>
    ConcurrencyConflict = 1,
}
