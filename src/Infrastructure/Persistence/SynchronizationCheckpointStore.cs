// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Synchronization;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence;

/// <summary>EF Core implementation for synchronization checkpoints.</summary>
/// <remarks>
/// The read path uses the scoped context because it joins no transaction. The write path uses the context enlisted in
/// the caller's session, so a checkpoint can only be written inside the transaction the caller opened.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class SynchronizationCheckpointStore(MailFathomDbContext readContext) : ISynchronizationCheckpointStore
{
    /// <inheritdoc />
    public async Task<SynchronizationCheckpoint?> GetCheckpointAsync(
        MailAccountId accountId,
        MailFolderResolutionId folderResolutionId,
        CancellationToken cancellationToken)
    {
        var alias = folderResolutionId.Alias.Value;
        var generation = folderResolutionId.Generation.Value;

        var entity = await readContext.SynchronizationCheckpoints
            .AsNoTracking()
            .SingleOrDefaultAsync(
                checkpoint => checkpoint.MailFolder.MailboxAccountId == accountId.Value
                    && checkpoint.MailFolder.Alias == alias
                    && checkpoint.MailFolder.ResolutionGeneration == generation,
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
        MailFolderResolutionId folderResolutionId,
        SynchronizationCheckpoint? expectedCheckpoint,
        SynchronizationCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        var writeContext = EfCorePersistenceSessionAccessor.DbContextOf(session);
        var folder = await MailFolderEntityResolver.GetRequiredAsync(
            writeContext,
            accountId,
            folderResolutionId,
            cancellationToken);

        var entity = await FindCheckpointForAsync(writeContext, folder, cancellationToken);
        if (entity is null)
        {
            if (expectedCheckpoint is not null)
            {
                throw new PersistenceConcurrencyConflictException(
                    $"Synchronization progress expected for folder {folderResolutionId.Alias.Value} no longer exists.");
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
                $"Durable synchronization progress for folder {folderResolutionId.Alias.Value} no longer matches the progress this write was based on.");
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
        MailFathomDbContext writeContext,
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
