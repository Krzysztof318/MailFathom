// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Access;
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
    private readonly IMailFolderMappingReader mappings;
    private readonly OptimisticConcurrencyRetryPolicy concurrencyRetryPolicy;
    private readonly MailboxSynchronizationOptions options;
    private readonly AccessAuthorization authorization;

    /// <summary>Initializes the eraser.</summary>
    /// <param name="mirrorStore">Removes the stored mail of one folder and clears its checkpoint.</param>
    /// <param name="mappings">Says what the account's configuration makes of the folder, which is what answers whether its source keeps messages there.</param>
    /// <param name="concurrencyRetryPolicy">Commits one pass, retrying a conflict with a competing writer.</param>
    /// <param name="options">Bounds one pass, reusing the bound the backward pass over stored mail already carries.</param>
    /// <param name="authorization">Answers which principal reached this use case.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public UnmirroredMailFolderEraser(
        IStoredMailFolderMirrorStore mirrorStore,
        IMailFolderMappingReader mappings,
        OptimisticConcurrencyRetryPolicy concurrencyRetryPolicy,
        MailboxSynchronizationOptions options,
        AccessAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(mirrorStore);
        ArgumentNullException.ThrowIfNull(mappings);
        ArgumentNullException.ThrowIfNull(concurrencyRetryPolicy);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(authorization);

        this.mirrorStore = mirrorStore;
        this.mappings = mappings;
        this.concurrencyRetryPolicy = concurrencyRetryPolicy;
        this.options = options;
        this.authorization = authorization;
    }

    /// <summary>Erases one bounded pass of what is stored for a folder nothing mirrors any more.</summary>
    /// <param name="account">The account the folder belongs to, by its generated identifier.</param>
    /// <param name="folderAlias">MailFathom's own name for the folder.</param>
    /// <param name="cancellationToken">Cancels the pass before or during its single transaction.</param>
    /// <returns>What this pass erased, and whether the folder still holds stored mail.</returns>
    /// <exception cref="PersistenceConcurrencyConflictException">Thrown when a competing writer wins a race the bounded retries could not resolve.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the use case was reached by anything but a caller granted <see cref="MailFathomPermission.AdminErase" />.</exception>
    /// <remarks>
    /// <para>
    /// This is the one operation that disposes of stored mail, so it asks for the grant allocated to exactly that and
    /// nothing wider reaches it. The check is here rather than only at the route because an erasure is irreversible and
    /// an entrypoint added later must not be able to perform one by omission.
    /// </para>
    /// <para>
    /// The alias need not be one a mapping still names, so whether the folder is one the source keeps messages in is
    /// answered where it can be — from the account's own mapping — and answered <see langword="false" /> where it
    /// cannot. A held account owes its source a removal record per erased row, and a record naming a folder playing a
    /// virtual role would name occurrences of messages the source keeps in other folders; an alias configuration no
    /// longer describes cannot be told apart from one, so what such an erasure leaves is mail standing on the source
    /// rather than a command against mail nobody asked about.
    /// </para>
    /// </remarks>
    public async Task<MailFolderMirrorErasure> EraseAsync(
        MailAccountId account,
        MailFolderAlias folderAlias,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminErase);

        var erasure = MailFolderMirrorErasure.Nothing;

        var folderHoldsItsOwnMessages = this.mappings.FindFolderNamed(account, folderAlias) is { } mapping
            && !VirtualMailFolderRoles.Includes(mapping.SpecialUse);

        await this.concurrencyRetryPolicy.CommitAsync(
            async (persistenceSession, attemptCancellationToken) => erasure =
                await this.mirrorStore.EraseFolderMirrorAsync(
                    persistenceSession,
                    account,
                    folderAlias,
                    folderHoldsItsOwnMessages,
                    this.options.MaxReconciledEmailsPerRun,
                    attemptCancellationToken),
            cancellationToken);

        return erasure;
    }
}
