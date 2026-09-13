// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Folders.Local;
using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Folders;

[RequiresIntegrationCoverage]
internal sealed class LocalMailFolderStore(MailFathomDbContext dbContext, TimeProvider timeProvider) : ILocalMailFolderStore
{
    public async Task<LocalMailFolderHolding?> ReadAsync(
        IPersistenceSession session,
        MailAccountIdentity account,
        CancellationToken cancellationToken)
    {
        var sessionContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var accountRow = await FindAccountAsync(sessionContext, account, cancellationToken);

        return accountRow is null
            ? null
            : await HoldingAsync(sessionContext.LocalMailFolders, account, accountRow.CustodyPhase, cancellationToken);
    }

    public async Task<LocalMailFolderHolding?> ReadAsync(MailAccountIdentity account, CancellationToken cancellationToken)
    {
        var userValue = account.User.Value;
        var accountIdValue = account.Id.Value;

        var phase = await dbContext.MailboxAccounts
            .AsNoTracking()
            .Where(row => row.UserId == userValue && row.Id == accountIdValue)
            .Select(static row => (MailAccountCustodyPhase?)row.CustodyPhase)
            .SingleOrDefaultAsync(cancellationToken);

        return phase is { } found
            ? await HoldingAsync(dbContext.LocalMailFolders.AsNoTracking(), account, found, cancellationToken)
            : null;
    }

    public async Task SaveAsync(
        IPersistenceSession session,
        MailAccountIdentity account,
        IReadOnlyCollection<LocalMailFolder> saved,
        IReadOnlyCollection<LocalMailFolderId> erased,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(erased);

        var sessionContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var accountRow = await FindAccountAsync(sessionContext, account, cancellationToken)
            ?? throw new InvalidOperationException("A local folder hierarchy is written only for an account its own read found.");

        accountRow.LocalMailFoldersRevision++;

        foreach (var folder in saved)
        {
            var row = await sessionContext.LocalMailFolders.FindAsync([folder.Id.Value], cancellationToken);

            if (row is null)
            {
                sessionContext.LocalMailFolders.Add(NewRow(account, folder));
            }
            else
            {
                Apply(row, folder);
            }
        }

        if (erased.Count is 0)
        {
            return;
        }

        var erasedAt = timeProvider.GetUtcNow();

        foreach (var row in await AccountFolders(sessionContext.LocalMailFolders, account)
            .Where(row => erased.Select(static id => id.Value).Contains(row.Id))
            .ToArrayAsync(cancellationToken))
        {
            row.ErasedAt = erasedAt;
        }
    }

    public async Task PlaceAsync(
        IPersistenceSession session,
        MailAccountIdentity account,
        StoredEmailId email,
        LocalMailFolderId folder,
        CancellationToken cancellationToken)
    {
        var sessionContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);

        // Found through the change tracker rather than updated set-based, because the message is the row synchronization
        // stored a moment ago in this same session and is still a pending insert no statement can see.
        var row = await sessionContext.StoredEmails.FindAsync([email.Value], cancellationToken);

        if (row is null
            || row.UserId != account.User.Value
            || row.MailboxAccountId != account.Id.Value
            || row.LocalMailFolderId is not null)
        {
            return;
        }

        row.LocalMailFolderId = folder.Value;
    }

    public async Task<LocalMailFolderMailErasure> EraseMailOfErasedFoldersAsync(
        IPersistenceSession session,
        MailAccountIdentity account,
        int maxEmails,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEmails);

        var sessionContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var accountRow = await FindAccountAsync(sessionContext, account, cancellationToken);

        if (accountRow is null)
        {
            return new LocalMailFolderMailErasure(ErasedEmailCount: 0, EmailsRemain: false);
        }

        // Advanced for the reason an edit advances it: an arrival placing a message into a folder this pass is about to
        // retire has to conflict with it rather than land in a folder nobody will erase again.
        accountRow.LocalMailFoldersRevision++;

        var userValue = account.User.Value;
        var accountIdValue = account.Id.Value;
        var erasedFolderIds = AccountFolders(sessionContext.LocalMailFolders, account)
            .Where(static row => row.ErasedAt != null)
            .Select(static row => (Guid?)row.Id);

        // One more than the bound is read so the answer says whether a later pass is owed without a second count.
        var batch = await sessionContext.StoredEmails
            .Where(email => email.UserId == userValue
                && email.MailboxAccountId == accountIdValue
                && erasedFolderIds.Contains(email.LocalMailFolderId))
            .OrderBy(static email => email.Id)
            .Take(maxEmails + 1)
            .ToArrayAsync(cancellationToken);

        var emailsRemain = batch.Length > maxEmails;
        var removed = emailsRemain ? batch[..maxEmails] : batch;
        Guid[] removedIds = [.. removed.Select(static email => email.Id)];

        // The cascade one stored message's erasure runs, in the order the unmirrored folder's erasure states it: the
        // user's storage figure and the content objects are read from rows the removal below only stages.
        await UserStoredContentLedger.RemoveAsync(sessionContext, removedIds, cancellationToken);
        await ReleasedContentObjects.ReleaseForStoredEmailsAsync(session, removedIds, cancellationToken);
        sessionContext.StoredEmails.RemoveRange(removed);

        if (!emailsRemain)
        {
            await RetireErasedFoldersAsync(sessionContext, account, cancellationToken);
        }

        return new LocalMailFolderMailErasure(removed.Length, emailsRemain);
    }

    public async Task<MailFolderAlias?> EraseEmailAsync(
        IPersistenceSession session,
        MailAccountIdentity account,
        StoredEmailId email,
        CancellationToken cancellationToken)
    {
        var sessionContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var userValue = account.User.Value;
        var accountIdValue = account.Id.Value;
        var emailValue = email.Value;

        var row = await sessionContext.StoredEmails
            .Include(static stored => stored.MailFolder)
            .SingleOrDefaultAsync(
                stored => stored.Id == emailValue
                    && stored.UserId == userValue
                    && stored.MailboxAccountId == accountIdValue,
                cancellationToken);

        if (row is null)
        {
            return null;
        }

        Guid[] removedIds = [row.Id];

        // The same cascade an erased folder's mail runs, in the same order, for the same reason.
        await UserStoredContentLedger.RemoveAsync(sessionContext, removedIds, cancellationToken);
        await ReleasedContentObjects.ReleaseForStoredEmailsAsync(session, removedIds, cancellationToken);
        sessionContext.StoredEmails.Remove(row);

        return MailFolderAlias.Create(row.MailFolder.Alias);
    }

    private static ValueTask<MailboxAccountEntity?> FindAccountAsync(
        MailFathomDbContext sessionContext,
        MailAccountIdentity account,
        CancellationToken cancellationToken) =>
        sessionContext.MailboxAccounts.FindAsync([account.User.Value, account.Id.Value], cancellationToken);

    private static IQueryable<LocalMailFolderEntity> AccountFolders(
        IQueryable<LocalMailFolderEntity> folders,
        MailAccountIdentity account)
    {
        var userValue = account.User.Value;
        var accountIdValue = account.Id.Value;

        return folders.Where(row => row.UserId == userValue && row.MailboxAccountId == accountIdValue);
    }

    private static async Task<LocalMailFolderHolding> HoldingAsync(
        IQueryable<LocalMailFolderEntity> folders,
        MailAccountIdentity account,
        MailAccountCustodyPhase phase,
        CancellationToken cancellationToken)
    {
        if (phase != MailAccountCustodyPhase.Held)
        {
            return new LocalMailFolderHolding(phase, [], []);
        }

        var rows = await AccountFolders(folders, account).ToArrayAsync(cancellationToken);

        return new LocalMailFolderHolding(
            phase,
            [.. rows.Where(static row => row.ErasedAt is null).Select(ToFolder)],
            [
                .. rows
                    .Where(static row => row.ErasedAt is not null && row.SourceFolderAlias is not null)
                    .Select(static row => MailFolderAlias.Create(row.SourceFolderAlias!))
                    .Distinct(),
            ]);
    }

    /// <summary>Removes the rows of erased folders whose mail is gone, keeping only what says where a source's arrivals go.</summary>
    private static async Task RetireErasedFoldersAsync(
        MailFathomDbContext sessionContext,
        MailAccountIdentity account,
        CancellationToken cancellationToken)
    {
        var erasedRows = await AccountFolders(sessionContext.LocalMailFolders, account)
            .Where(static row => row.ErasedAt != null)
            .ToArrayAsync(cancellationToken);

        foreach (var kept in erasedRows.Where(static row => row.SourceFolderAlias is not null))
        {
            // The name a person may have typed is not kept past the mail it named; the alias is the operator's own word.
            kept.ParentId = null;
            kept.Name = kept.SourceFolderAlias!;
            kept.NameKey = kept.SourceFolderAlias!.ToUpperInvariant();
        }

        sessionContext.LocalMailFolders.RemoveRange(erasedRows.Where(static row => row.SourceFolderAlias is null));
    }

    private static LocalMailFolder ToFolder(LocalMailFolderEntity row) => new(
        LocalMailFolderId.Create(row.Id),
        row.ParentId is { } parentId ? LocalMailFolderId.Create(parentId) : null,
        LocalMailFolderName.Create(row.Name),
        row.Role,
        row.SourceFolderAlias is { } alias ? MailFolderAlias.Create(alias) : null);

    private static LocalMailFolderEntity NewRow(MailAccountIdentity account, LocalMailFolder folder)
    {
        var row = new LocalMailFolderEntity
        {
            Id = folder.Id.Value,
            UserId = account.User.Value,
            MailboxAccountId = account.Id.Value,
            Name = folder.Name.Value,
            NameKey = folder.Name.ComparisonKey,
        };

        Apply(row, folder);

        return row;
    }

    private static void Apply(LocalMailFolderEntity row, LocalMailFolder folder)
    {
        row.ParentId = folder.ParentId?.Value;
        row.Name = folder.Name.Value;
        row.NameKey = folder.Name.ComparisonKey;
        row.Role = folder.Role;
        row.SourceFolderAlias = folder.SourceFolderAlias?.Value;
    }
}
