// Copyright © 2026 Krzysztof Kasprowicz

using System.Diagnostics.CodeAnalysis;
using MailMcp.Application.Synchronization;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Folders;
using MailMcp.Domain.Messages;
using MailMcp.Domain.Synchronization;
using Microsoft.EntityFrameworkCore;

namespace MailMcp.Infrastructure.Persistence;

/// <summary>EF Core implementation for synchronization checkpoints.</summary>
[ExcludeFromCodeCoverage(Justification = "Provider-boundary adapter behavior requires future integration coverage.")]
public sealed class SynchronizationCheckpointStore(MailMcpDbContext dbContext) : ISynchronizationCheckpointStore
{
    /// <inheritdoc />
    public async Task<SynchronizationCheckpoint?> GetCheckpointAsync(
        MailAccountId accountId,
        MailFolderName folderName,
        CancellationToken cancellationToken)
    {
        var entity = await dbContext.MailFolders.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == accountId.Value && x.FolderName == folderName.Value, cancellationToken);
        if (entity is null || entity.UidValidity == 0)
        {
            return null;
        }

        return new SynchronizationCheckpoint(ImapUidValidity.Create(entity.UidValidity), entity.LastSeenUid is { } uid ? ImapUid.Create(uid) : null, entity.SynchronizedAt);
    }

    /// <inheritdoc />
    public async Task SaveCheckpointAsync(
        ISession session,
        MailAccountId accountId,
        MailFolderName folderName,
        SynchronizationCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        var entity = await dbContext.MailFolders.SingleOrDefaultAsync(x => x.AccountId == accountId.Value && x.FolderName == folderName.Value, cancellationToken);
        if (entity is null)
        {
            dbContext.MailFolders.Add(new MailFolderEntity { AccountId = accountId.Value, FolderName = folderName.Value, UidValidity = checkpoint.UidValidity.Value, LastSeenUid = checkpoint.LastSeenUid?.Value, SynchronizedAt = checkpoint.SynchronizedAt });
        }
        else
        {
            entity.UidValidity = checkpoint.UidValidity.Value;
            entity.LastSeenUid = checkpoint.LastSeenUid?.Value;
            entity.SynchronizedAt = checkpoint.SynchronizedAt;
        }
    }
}
