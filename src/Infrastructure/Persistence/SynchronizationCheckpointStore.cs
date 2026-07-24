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
// TODO: Remove this exclusion when the planned PostgreSQL integration tests are enabled.
[ExcludeFromCodeCoverage(Justification = "Will be covered later by PostgreSQL integration tests.")]
public sealed class SynchronizationCheckpointStore(MailMcpDbContext dbContext) : ISynchronizationCheckpointStore
{
    /// <inheritdoc />
    public async Task<SynchronizationCheckpoint?> GetCheckpointAsync(
        MailAccountId accountId,
        MailFolderName folderName,
        CancellationToken cancellationToken)
    {
        var entity = await dbContext.SynchronizationCheckpoints
            .AsNoTracking()
            .SingleOrDefaultAsync(
                checkpoint => checkpoint.MailFolder.MailboxAccountId == accountId.Value
                    && checkpoint.MailFolder.RemoteName == folderName.Value,
                cancellationToken);
        if (entity is null)
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
        var folder = await MailFolderEntityResolver.GetOrAddAsync(
            dbContext,
            accountId,
            folderName,
            cancellationToken);
        var entity = dbContext.SynchronizationCheckpoints.Local.SingleOrDefault(
            candidate => ReferenceEquals(candidate.MailFolder, folder)
                || (folder.Id != 0 && candidate.MailFolderId == folder.Id));
        if (entity is null && folder.Id != 0)
        {
            entity = await dbContext.SynchronizationCheckpoints.SingleOrDefaultAsync(
                candidate => candidate.MailFolderId == folder.Id,
                cancellationToken);
        }

        if (entity is null)
        {
            dbContext.SynchronizationCheckpoints.Add(new SynchronizationCheckpointEntity
            {
                MailFolder = folder,
                UidValidity = checkpoint.UidValidity.Value,
                LastSeenUid = checkpoint.LastSeenUid?.Value,
                SynchronizedAt = checkpoint.SynchronizedAt,
            });
        }
        else
        {
            entity.UidValidity = checkpoint.UidValidity.Value;
            entity.LastSeenUid = checkpoint.LastSeenUid?.Value;
            entity.SynchronizedAt = checkpoint.SynchronizedAt;
        }
    }
}
