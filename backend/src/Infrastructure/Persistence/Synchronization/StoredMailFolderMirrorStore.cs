// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Folders;
using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
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
        MailAccountId accountId,
        MailFolderAlias folderAlias,
        int maxEmails,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEmails);

        var sessionContext = EfCorePersistenceSessionAccessor.DbContextOf(session);
        var aliasValue = folderAlias.Value;
        var accountIdValue = accountId.Value;

        // One more than the bound is read so the answer says whether a later pass is owed without a second count over
        // the same rows.
        var erased = await sessionContext.StoredEmails
            .Where(email => email.MailboxAccountId == accountIdValue && email.MailFolder.Alias == aliasValue)
            .OrderBy(email => email.Id)
            .Take(maxEmails + 1)
            .ToArrayAsync(cancellationToken);

        var emailsRemain = erased.Length > maxEmails;

        sessionContext.StoredEmails.RemoveRange(emailsRemain ? erased[..maxEmails] : erased);

        if (!emailsRemain)
        {
            await ClearCheckpointsAsync(sessionContext, accountIdValue, aliasValue, cancellationToken);
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
        string accountIdValue,
        string aliasValue,
        CancellationToken cancellationToken)
    {
        var checkpoints = await sessionContext.SynchronizationCheckpoints
            .Where(checkpoint => checkpoint.MailFolder.MailboxAccountId == accountIdValue
                && checkpoint.MailFolder.Alias == aliasValue)
            .ToArrayAsync(cancellationToken);

        if (checkpoints.Length > 0)
        {
            sessionContext.SynchronizationCheckpoints.RemoveRange(checkpoints);
        }
    }
}
