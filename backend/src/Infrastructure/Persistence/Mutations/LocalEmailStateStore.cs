// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Mutations.Local;
using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Mutations;

/// <summary>Reads and writes the stored state a held account's change commits to, inside the change's own transaction.</summary>
/// <remarks>
/// The row is read tracked rather than projected, because one call can commit several changes to one message — a person
/// marking a message read and flagging it at once — and each has to see what the one before it staged. Every read and
/// write is scoped to the account as well as the identity, so a change can never reach another user's mail by naming it.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class LocalEmailStateStore : ILocalEmailStateStore
{
    /// <inheritdoc />
    public async Task<LocalEmailState?> ReadAsync(
        IPersistenceSession session,
        MailAccountIdentity account,
        StoredEmailId email,
        CancellationToken cancellationToken)
    {
        var row = await FindAsync(session, account, email, cancellationToken);

        if (row is null)
        {
            return null;
        }

        var sourceFolder = new MailFolderResolution(
            MailFolderAlias.Create(row.MailFolder.Alias),
            MailFolderResolutionGeneration.Create(row.MailFolder.ResolutionGeneration),
            RemoteFolderPath.Create(row.MailFolder.RemotePath));

        return new LocalEmailState(
            sourceFolder,
            row.LocalMailFolderId is { } folder ? LocalMailFolderId.Create(folder) : null,
            row.IsRemotelySeen,
            row.IsRemotelyFlagged,
            RemoteEmailKeywords.Create(row.RemoteKeywords));
    }

    /// <inheritdoc />
    public async Task WriteAsync(
        IPersistenceSession session,
        MailAccountIdentity account,
        StoredEmailId email,
        LocalEmailState state,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);

        var row = await FindAsync(session, account, email, cancellationToken)
            ?? throw new InvalidOperationException("A held account's change is written only to a message its own read found.");

        row.LocalMailFolderId = state.Folder?.Value;
        row.IsRemotelySeen = state.IsSeen;
        row.IsRemotelyFlagged = state.IsFlagged;
        row.RemoteKeywords = [.. state.Keywords.Values];
    }

    /// <inheritdoc />
    public async Task EraseAsync(
        IPersistenceSession session,
        MailAccountIdentity account,
        StoredEmailId email,
        CancellationToken cancellationToken)
    {
        var row = await FindAsync(session, account, email, cancellationToken);

        if (row is null)
        {
            return;
        }

        var sessionContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        Guid[] removedIds = [row.Id];

        // The cascade every stored message's erasure runs, in the order LocalMailFolderStore states it: the user's storage
        // figure and the content objects are read from the row the removal below only stages.
        await UserStoredContentLedger.RemoveAsync(sessionContext, removedIds, cancellationToken);
        await ReleasedContentObjects.ReleaseForStoredEmailsAsync(session, removedIds, cancellationToken);
        sessionContext.StoredEmails.Remove(row);
    }

    private static async Task<StoredEmailEntity?> FindAsync(
        IPersistenceSession session,
        MailAccountIdentity account,
        StoredEmailId email,
        CancellationToken cancellationToken)
    {
        var sessionContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var emailId = email.Value;
        var userId = account.User.Value;
        var accountId = account.Id.Value;

        return await sessionContext.StoredEmails
            .Include(static row => row.MailFolder)
            .Where(row => row.Id == emailId && row.UserId == userId && row.MailboxAccountId == accountId)
            .Where(StoredEmailTombstone.IsNotTombstoned)
            .SingleOrDefaultAsync(cancellationToken);
    }
}
