// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.Persistence;
using MailMcp.Application.Synchronization;
using MailMcp.CodeCoverage;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Emails;
using MailMcp.Domain.Folders;
using MailMcp.Domain.Synchronization;
using Microsoft.EntityFrameworkCore;

namespace MailMcp.Infrastructure.Persistence;

/// <summary>EF Core implementation for synchronization checkpoints.</summary>
/// <remarks>
/// The read path uses the scoped context because it joins no transaction. The write path uses the context enlisted in
/// the caller's session, so a checkpoint can only be written inside the transaction the caller opened.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class SynchronizationCheckpointStore(MailMcpDbContext readContext) : ISynchronizationCheckpointStore
{
    /// <inheritdoc />
    public async Task<SynchronizationCheckpoint?> GetCheckpointAsync(
        MailAccountId accountId,
        MailFolderName folderName,
        CancellationToken cancellationToken)
    {
        var entity = await readContext.SynchronizationCheckpoints
            .AsNoTracking()
            .SingleOrDefaultAsync(
                checkpoint => checkpoint.MailFolder.MailboxAccountId == accountId.Value
                    && checkpoint.MailFolder.RemoteName == folderName.Value,
                cancellationToken);

        if (entity is null)
        {
            return null;
        }

        return new SynchronizationCheckpoint(
            ImapUidValidity.Create(entity.UidValidity),
            entity.LastSeenUid is { } uid ? ImapUid.Create(uid) : null,
            entity.SynchronizedAt);
    }

    /// <inheritdoc />
    public async Task SaveCheckpointAsync(
        IPersistenceSession session,
        MailAccountId accountId,
        MailFolderName folderName,
        SynchronizationCheckpoint? expectedCheckpoint,
        SynchronizationCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        var writeContext = EfCorePersistenceSessionAccessor.DbContextOf(session);
        var folder = await MailFolderEntityResolver.GetOrAddAsync(
            writeContext,
            accountId,
            folderName,
            cancellationToken);

        var entity = await FindCheckpointForAsync(writeContext, folder, cancellationToken);
        if (entity is null)
        {
            if (expectedCheckpoint is not null)
            {
                throw new PersistenceConcurrencyConflictException(
                    $"Synchronization progress expected for folder {folderName.Value} no longer exists.");
            }

            writeContext.SynchronizationCheckpoints.Add(new SynchronizationCheckpointEntity
            {
                MailFolder = folder,
                UidValidity = checkpoint.UidValidity.Value,
                LastSeenUid = checkpoint.LastSeenUid?.Value,
                SynchronizedAt = checkpoint.SynchronizedAt,
            });

            return;
        }

        var currentCheckpoint = new SynchronizationCheckpoint(
            ImapUidValidity.Create(entity.UidValidity),
            entity.LastSeenUid is { } uid ? ImapUid.Create(uid) : null,
            entity.SynchronizedAt);
        if (!currentCheckpoint.RepresentsSameProgressAs(expectedCheckpoint))
        {
            throw new PersistenceConcurrencyConflictException(
                $"Durable synchronization progress for folder {folderName.Value} no longer matches the progress this write was based on.");
        }

        entity.UidValidity = checkpoint.UidValidity.Value;
        entity.LastSeenUid = checkpoint.LastSeenUid?.Value;
        entity.SynchronizedAt = LaterOf(entity.SynchronizedAt, checkpoint.SynchronizedAt);
    }

    private static DateTimeOffset? LaterOf(DateTimeOffset? current, DateTimeOffset? proposed) =>
        current is not null && (proposed is null || current > proposed)
            ? current
            : proposed;

    private static async Task<SynchronizationCheckpointEntity?> FindCheckpointForAsync(
        MailMcpDbContext writeContext,
        MailFolderEntity folder,
        CancellationToken cancellationToken)
    {
        // EF Core fixes up the inverse navigation as soon as a checkpoint referencing this folder is tracked, which covers
        // a checkpoint added earlier in this same uncommitted session and still without its foreign key value.
        if (folder.SynchronizationCheckpoint is { } pendingCheckpoint)
        {
            return pendingCheckpoint;
        }

        // A folder that is itself still pending insert cannot have a persisted checkpoint, so no query is worth issuing.
        if (writeContext.Entry(folder).State == EntityState.Added)
        {
            return null;
        }

        return await writeContext.SynchronizationCheckpoints.FindAsync([folder.Id], cancellationToken);
    }
}
