// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization.Checkpoints;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Synchronization;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Synchronization;

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

        return entity is null ? null : CheckpointOf(entity);
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

        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
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
                ReconciledThroughModSeq = StoredModSeqOf(checkpoint.ReconciledThroughModSeq),
            });

            return;
        }

        var currentCheckpoint = CheckpointOf(entity);
        if (!currentCheckpoint.RepresentsSameProgressAs(expectedCheckpoint))
        {
            throw new PersistenceConcurrencyConflictException(
                $"Durable synchronization progress for folder {folderResolutionId.Alias.Value} no longer matches the progress this write was based on.");
        }

        entity.UidValidity = checkpoint.UidValidity.Value;
        entity.LastSeenUid = checkpoint.LastSeenUid?.Value;
        entity.SynchronizedAt = LaterOf(entity.SynchronizedAt, checkpoint.SynchronizedAt);
        entity.ReconciledThroughModSeq = StoredModSeqOf(checkpoint.ReconciledThroughModSeq);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MailFolderAlias>> DiscardCheckpointsAsync(
        IPersistenceSession session,
        MailAccountId accountId,
        MailFolderAlias? folderAlias,
        CancellationToken cancellationToken)
    {
        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var account = accountId.Value;
        var alias = folderAlias?.Value;

        // Tracked rather than deleted in one statement, because the removal joins the caller's transaction and an
        // ExecuteDelete would run outside the change tracker and commit on its own.
        var checkpoints = await writeContext.SynchronizationCheckpoints
            .Include(checkpoint => checkpoint.MailFolder)
            .Where(checkpoint => checkpoint.MailFolder.MailboxAccountId == account
                && (alias == null || checkpoint.MailFolder.Alias == alias))
            .ToArrayAsync(cancellationToken);

        writeContext.SynchronizationCheckpoints.RemoveRange(checkpoints);

        return
        [
            .. checkpoints
                .Select(checkpoint => checkpoint.MailFolder.Alias)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .Select(MailFolderAlias.Create),
        ];
    }

    /// <summary>Reads one stored row as the progress it records, including a row written before sequences were tracked.</summary>
    /// <remarks>
    /// A row from before this column existed reads with the sequence absent and every other value intact, which is what
    /// lets an upgraded installation keep its progress instead of resynchronizing its folders.
    /// </remarks>
    private static SynchronizationCheckpoint CheckpointOf(SynchronizationCheckpointEntity entity) =>
        new(
            ImapUidValidity.Create(entity.UidValidity),
            entity.LastSeenUid is { } uid ? ImapUid.Create(uid) : null,
            entity.SynchronizedAt)
        {
            ReconciledThroughModSeq = entity.ReconciledThroughModSeq is { } modSeq ? (ulong)modSeq : null,
        };

    /// <summary>Narrows a modification sequence onto the signed column that holds it.</summary>
    /// <remarks>
    /// The conversion is checked because RFC 7162 bounds the value to 63 bits and the adapter that reads it from the
    /// server enforces the same bound. A value that overflowed here would mean one of those two statements is wrong,
    /// and storing a negative ordering key would make every later comparison answer backwards.
    /// </remarks>
    private static long? StoredModSeqOf(ulong? modSeq) => modSeq is { } value ? checked((long)value) : null;

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
