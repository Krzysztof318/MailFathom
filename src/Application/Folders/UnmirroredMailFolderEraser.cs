// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Folders;

/// <summary>Takes away the local copy of a folder whose mapping has stopped mirroring it.</summary>
/// <remarks>
/// <para>
/// Nothing runs this because a configuration value changed. Turning a folder's synchronization off keeps what the
/// folder had already stored, because rows nobody may read are not stale in a way anybody observes and erasing them
/// would charge an operator a whole remirror for a switch they may flip back the same week. Getting rid of the local
/// copy is therefore an act somebody performs, and this is what such a command runs.
/// </para>
/// <para>
/// The removal is the deletion path an erasing disposition already uses rather than a second one — the row goes, and
/// PostgreSQL takes its raw MIME, its search document, its passages, and their vectors with it. It is bounded per pass
/// for the same reason reconciliation is: a mailbox's worth of rows is not one transaction, and a pass that ended early
/// leaves the rest for the next one rather than half a folder in an unrepeatable state.
/// </para>
/// </remarks>
public sealed class UnmirroredMailFolderEraser
{
    private readonly IStoredMailFolderMirrorStore mirrorStore;
    private readonly OptimisticConcurrencyRetryPolicy concurrencyRetryPolicy;
    private readonly MailboxSynchronizationOptions options;

    /// <summary>Initializes the eraser.</summary>
    /// <param name="mirrorStore">Removes the stored mail of one folder and clears its checkpoint.</param>
    /// <param name="concurrencyRetryPolicy">Commits one pass, retrying a conflict with a competing writer.</param>
    /// <param name="options">Bounds one pass, reusing the bound the backward pass over stored mail already carries.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public UnmirroredMailFolderEraser(
        IStoredMailFolderMirrorStore mirrorStore,
        OptimisticConcurrencyRetryPolicy concurrencyRetryPolicy,
        MailboxSynchronizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(mirrorStore);
        ArgumentNullException.ThrowIfNull(concurrencyRetryPolicy);
        ArgumentNullException.ThrowIfNull(options);

        this.mirrorStore = mirrorStore;
        this.concurrencyRetryPolicy = concurrencyRetryPolicy;
        this.options = options;
    }

    /// <summary>Erases one bounded pass of what is stored for a folder nothing mirrors any more.</summary>
    /// <param name="accountId">The account the folder belongs to.</param>
    /// <param name="folderAlias">MailFathom's own name for the folder.</param>
    /// <param name="cancellationToken">Cancels the pass before or during its single transaction.</param>
    /// <returns>What this pass erased, and whether the folder still holds stored mail.</returns>
    /// <exception cref="PersistenceConcurrencyConflictException">Thrown when a competing writer wins a race the bounded retries could not resolve.</exception>
    public async Task<MailFolderMirrorErasure> EraseAsync(
        MailAccountId accountId,
        MailFolderAlias folderAlias,
        CancellationToken cancellationToken)
    {
        var erasure = MailFolderMirrorErasure.Nothing;

        await this.concurrencyRetryPolicy.CommitAsync(
            async (persistenceSession, attemptCancellationToken) => erasure =
                await this.mirrorStore.EraseFolderMirrorAsync(
                    persistenceSession,
                    accountId,
                    folderAlias,
                    this.options.MaxReconciledEmailsPerRun,
                    attemptCancellationToken),
            cancellationToken);

        return erasure;
    }
}
