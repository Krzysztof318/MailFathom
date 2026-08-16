// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Synchronization;

namespace MailFathom.Application.Synchronization.Checkpoints;

/// <summary>Persists synchronization checkpoints for mailbox folders.</summary>
/// <remarks>
/// The port carries behavior of its own rather than a row's storage: staging an advance is a compare against the
/// progress the caller decided from, so a run that resumed from stale state is refused instead of overwriting a run
/// that already moved the binding forward. No persistence library publishes that contract, and expressing it here is
/// what lets the compare be asserted without a database.
/// </remarks>
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

    /// <summary>Stages the removal of the durable progress of an account's bindings, or of one alias of it.</summary>
    /// <param name="session">The open session whose transaction the staged removal joins.</param>
    /// <param name="accountId">The account whose progress is discarded.</param>
    /// <param name="folderAlias">The one alias to discard the progress of, or <see langword="null" /> for every binding of the account.</param>
    /// <param name="cancellationToken">Cancels the read that selects the bindings and the writes that stage their removal.</param>
    /// <returns>The aliases whose bindings held progress, ordered and without repeats.</returns>
    /// <remarks>
    /// <para>
    /// The UID progress and the reconciled modification sequence go together, because they describe one UIDVALIDITY
    /// scope: leaving the sequence behind would tell the backward pass that everything older than it is already
    /// accounted for, over a forward pass that is about to read the folder from its first UID again.
    /// </para>
    /// <para>
    /// Removal rather than an emptied row, so a binding reads exactly as one that has never been synchronized — which
    /// is what makes the next run start at the first UID inside the account's window rather than resume. It is also
    /// what makes a run that is in flight lose the race safely: that run decided from progress that no longer exists,
    /// so <see cref="SaveCheckpointAsync" /> refuses its advance instead of writing a checkpoint in front of mail the
    /// discarded progress was about to have re-read.
    /// </para>
    /// <para>
    /// The removal is per binding rather than per alias. A repointed alias carries a binding of its own and progress of
    /// its own, so discarding by alias alone would leave the older binding's progress behind under a name nothing reads
    /// it by.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<MailFolderAlias>> DiscardCheckpointsAsync(
        IPersistenceSession session,
        MailAccountId accountId,
        MailFolderAlias? folderAlias,
        CancellationToken cancellationToken);
}
