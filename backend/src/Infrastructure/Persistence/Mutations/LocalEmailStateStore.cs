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
using MailFathom.Infrastructure.Persistence.Synchronization;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Mutations;

/// <summary>Reads and writes the stored state a held account's change commits to, inside the change's own transaction.</summary>
/// <remarks>
/// The row is read tracked rather than projected, because one call can commit several changes to one message — a person
/// marking a message read and flagging it at once — and each has to see what the one before it staged. Every read and
/// write is scoped to the account as well as the identity, so a change can never reach another user's mail by naming it.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class LocalEmailStateStore(TimeProvider timeProvider) : ILocalEmailStateStore
{
    /// <inheritdoc />
    public async Task<LocalEmailState?> ReadAsync(
        IPersistenceSession session,
        MailAccountId account,
        StoredEmailId email,
        CancellationToken cancellationToken)
    {
        var row = await FindAsync(session, account, email, cancellationToken);

        if (row is null)
        {
            return null;
        }

        return new LocalEmailState(
            MailFolderEntityResolver.ToResolution(row.MailFolder),
            row.LocalMailFolderId is { } folder ? LocalMailFolderId.Create(folder) : null,
            row.IsRemotelySeen,
            row.IsRemotelyFlagged,
            RemoteEmailKeywords.Create(row.RemoteKeywords));
    }

    /// <inheritdoc />
    public async Task WriteAsync(
        IPersistenceSession session,
        MailAccountId account,
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
        MailAccountId account,
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

        // This is the one erasure path a person's own delete takes on a held account, so it owes the source removal
        // record every other erasure path owes: the row about to go is the only thing that still says where the
        // message is on the source, and the drain may not have reached it yet.
        MailboxSourceRemovalRecords.Stage(sessionContext, [row], timeProvider.GetUtcNow());

        // The cascade every stored message's erasure runs, in the order LocalMailFolderStore states it: the user's storage
        // figure and the content objects are read from the row the removal below only stages.
        await AccountStoredContentLedger.RemoveAsync(sessionContext, removedIds, cancellationToken);
        await ReleasedContentObjects.ReleaseForStoredEmailsAsync(session, removedIds, cancellationToken);
        sessionContext.StoredEmails.Remove(row);
    }

    private static async Task<StoredEmailEntity?> FindAsync(
        IPersistenceSession session,
        MailAccountId account,
        StoredEmailId email,
        CancellationToken cancellationToken)
    {
        var sessionContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var emailId = email.Value;
        var accountId = account.Value;

        return await sessionContext.StoredEmails
            .Include(static row => row.MailFolder)
            .Where(row => row.Id == emailId && row.MailboxAccountId == accountId)
            .Where(StoredEmailTombstone.IsNotTombstoned)
            .SingleOrDefaultAsync(cancellationToken);
    }
}
