// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.Persistence;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Folders;
using MailMcp.Domain.Synchronization;

namespace MailMcp.Application.Synchronization;

/// <summary>Persists synchronization checkpoints for mailbox folders.</summary>
public interface ISynchronizationCheckpointStore
{
    /// <summary>Gets the last durable checkpoint for one alias binding.</summary>
    /// <param name="accountId">The account owning the folder.</param>
    /// <param name="folderResolutionId">The alias binding whose progress is read.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The durable progress, or <see langword="null" /> when the binding has never been synchronized.</returns>
    /// <remarks>
    /// Progress belongs to a binding rather than to an alias, so a repointed alias reads no checkpoint at all and its
    /// new remote folder is synchronized from its first UID.
    /// </remarks>
    Task<SynchronizationCheckpoint?> GetCheckpointAsync(
        MailAccountId accountId,
        MailFolderResolutionId folderResolutionId,
        CancellationToken cancellationToken);

    /// <summary>Stages a checkpoint update only when the durable state still matches the state previously read.</summary>
    /// <param name="session">The open session whose transaction the staged update joins.</param>
    /// <param name="accountId">The account owning the folder.</param>
    /// <param name="folderResolutionId">The alias binding whose progress advances.</param>
    /// <param name="expectedCheckpoint">The durable progress the caller decided from, or <see langword="null" /> when it expects no checkpoint yet.</param>
    /// <param name="checkpoint">The progress to stage.</param>
    /// <param name="cancellationToken">Cancels the lookup before anything is staged.</param>
    /// <returns>A task that completes once the update is staged in the caller's session.</returns>
    /// <exception cref="PersistenceConcurrencyConflictException">
    /// Thrown when durable progress no longer matches <paramref name="expectedCheckpoint" />. Nothing is staged, because
    /// progress that moved must be reread before a new advance is decided rather than overwritten from stale state.
    /// </exception>
    Task SaveCheckpointAsync(
        IPersistenceSession session,
        MailAccountId accountId,
        MailFolderResolutionId folderResolutionId,
        SynchronizationCheckpoint? expectedCheckpoint,
        SynchronizationCheckpoint checkpoint,
        CancellationToken cancellationToken);
}
