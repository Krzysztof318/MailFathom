// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Folders;
using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence.Emails;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Synchronization;

/// <summary>EF Core state for removing what is stored under a folder MailFathom no longer mirrors.</summary>
/// <remarks>
/// The rows are removed through the change tracker rather than by a set-based delete, so PostgreSQL applies the same
/// cascades an erasing disposition already relies on: the raw MIME, the search document, the passages, their vectors,
/// and any outstanding repair request are declared from the stored email and go with it.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class StoredMailFolderMirrorStore : IStoredMailFolderMirrorStore
{
    /// <inheritdoc />
    public async Task<MailFolderMirrorErasure> EraseFolderMirrorAsync(
        IPersistenceSession session,
        MailAccountIdentity account,
        MailFolderAlias folderAlias,
        int maxEmails,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEmails);

        var sessionContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var aliasValue = folderAlias.Value;
        var ownerValue = account.Owner.Value;
        var accountIdValue = account.Id.Value;

        // One more than the bound is read so the answer says whether a later pass is owed without a second count over
        // the same rows.
        var erased = await sessionContext.StoredEmails
            .Where(email => email.OwnerId == ownerValue
                && email.MailboxAccountId == accountIdValue
                && email.MailFolder.Alias == aliasValue)
            .OrderBy(email => email.Id)
            .Take(maxEmails + 1)
            .ToArrayAsync(cancellationToken);

        var emailsRemain = erased.Length > maxEmails;
        var removed = emailsRemain ? erased[..maxEmails] : erased;

        // What these messages hold leaves storage with them, so their owner's figure gives it back inside the same
        // transaction. What it subtracts is read from the payloads, so the constraint is that it runs before this
        // session commits rather than before the line below it: the removal below only stages a delete the change
        // tracker applies at that commit, so neither statement sees the other's effect until then. A later change
        // making the removal set-based would execute immediately and turn that ordering into a real one.
        await OwnerStoredContentLedger.RemoveAsync(
            sessionContext,
            [.. removed.Select(email => email.Id)],
            cancellationToken);

        // Read before the removal is staged, because the payload rows go by cascade and the keys they carry are the
        // only pointers to the objects holding this folder's mail.
        await ReleasedContentObjects.ReleaseForStoredEmailsAsync(
            session,
            [.. removed.Select(static email => email.Id)],
            cancellationToken);

        sessionContext.StoredEmails.RemoveRange(removed);

        if (!emailsRemain)
        {
            await ClearCheckpointsAsync(sessionContext, ownerValue, accountIdValue, aliasValue, cancellationToken);
        }

        return new MailFolderMirrorErasure(emailsRemain ? maxEmails : erased.Length, emailsRemain);
    }

    /// <summary>Removes the checkpoints of every binding the alias has had, once nothing of the folder is stored.</summary>
    /// <remarks>
    /// Every generation is cleared rather than the current one, because the alias may have been rebound while it was
    /// mirrored and each binding carries a checkpoint of its own. The bindings themselves stay: they are what an alias
    /// resolves through, and a folder nothing mirrors is still the destination of anything written into it.
    /// </remarks>
    private static async Task ClearCheckpointsAsync(
        MailFathomDbContext sessionContext,
        Guid ownerValue,
        string accountIdValue,
        string aliasValue,
        CancellationToken cancellationToken)
    {
        var checkpoints = await sessionContext.SynchronizationCheckpoints
            .Where(checkpoint => checkpoint.MailFolder.OwnerId == ownerValue
                && checkpoint.MailFolder.MailboxAccountId == accountIdValue
                && checkpoint.MailFolder.Alias == aliasValue)
            .ToArrayAsync(cancellationToken);

        if (checkpoints.Length > 0)
        {
            sessionContext.SynchronizationCheckpoints.RemoveRange(checkpoints);
        }
    }
}
