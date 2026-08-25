// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Owners;
using MailFathom.Infrastructure.Persistence.Sessions;

namespace MailFathom.Infrastructure.Persistence.Synchronization;

/// <summary>Resolves the row that carries one alias binding, which every folder-scoped write is attached to.</summary>
/// <remarks>
/// A binding row is created by folder resolution alone, in its own committed transaction, before anything is
/// synchronized under it. A write path therefore requires the row rather than creating one: a missing row would mean
/// occurrences were being attached to a generation nothing recorded, which is the state the generation exists to
/// prevent.
/// </remarks>
[RequiresIntegrationCoverage]
internal static class MailFolderEntityResolver
{
    public static async Task<MailFolderEntity?> FindAsync(
        MailFathomDbContext dbContext,
        MailAccountId accountId,
        MailFolderResolutionId folderResolutionId,
        CancellationToken cancellationToken)
    {
        var alias = folderResolutionId.Alias.Value;
        var generation = folderResolutionId.Generation.Value;

        // Looked up by its alternate key, so the change-tracker pass is explicit rather than handled by FindAsync.
        return await TrackedEntityLookup.SinglePendingOrPersistedAsync(
            dbContext.MailFolders,
            dbContext.MailFolders,
            candidate => candidate.MailboxAccountId == accountId.Value
                && candidate.Alias == alias
                && candidate.ResolutionGeneration == generation,
            cancellationToken);
    }

    public static async Task<MailFolderEntity> GetRequiredAsync(
        MailFathomDbContext dbContext,
        MailAccountId accountId,
        MailFolderResolutionId folderResolutionId,
        CancellationToken cancellationToken) =>
        await FindAsync(dbContext, accountId, folderResolutionId, cancellationToken)
        ?? throw new InvalidOperationException(
            $"Folder alias {accountId.Value}/{folderResolutionId} has no recorded binding, so nothing can be stored under it.");

    public static async Task<MailFolderEntity> AddAsync(
        MailFathomDbContext dbContext,
        MailAccountId accountId,
        MailFolderResolution resolution,
        CancellationToken cancellationToken)
    {
        // The account is keyed by the identifier itself, so FindAsync already resolves a pending insert without a query.
        var account = await dbContext.MailboxAccounts.FindAsync([accountId.Value], cancellationToken);
        if (account is null)
        {
            // The owner is read rather than minted. A run that invented one would be deciding whose mail this is while
            // storing it, and the record it invented would be the boundary every later read of that mail is judged
            // against; the read is paid for only on the run that first binds one of an account's folders.
            var ownerId = await OwnerAccountResolver.ResolveConfiguredOwnerAsync(dbContext, cancellationToken);

            account = new MailboxAccountEntity { Id = accountId.Value, OwnerId = ownerId };

            dbContext.MailboxAccounts.Add(account);
        }

        var folder = new MailFolderEntity
        {
            MailboxAccountId = accountId.Value,
            Alias = resolution.Alias.Value,
            ResolutionGeneration = resolution.Generation.Value,
            RemotePath = resolution.RemotePath.Value,
            HierarchyDelimiter = resolution.RemotePath.HierarchyDelimiter?.ToString(),
            MailboxAccount = account,
        };

        dbContext.MailFolders.Add(folder);

        return folder;
    }

    /// <summary>Rebuilds the alias binding one row states.</summary>
    /// <param name="entity">The binding row.</param>
    /// <returns>The resolution that row describes, including the remote path a session selects.</returns>
    /// <remarks>
    /// It is the exact inverse of what <see cref="AddAsync" /> writes, and it lives beside it so the two cannot drift:
    /// the delimiter in particular is stored as text and read back as the one character it holds, and a second reading
    /// of that would be a second chance to disagree about an empty string.
    /// </remarks>
    public static MailFolderResolution ToResolution(MailFolderEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        return new MailFolderResolution(
            MailFolderAlias.Create(entity.Alias),
            MailFolderResolutionGeneration.Create(entity.ResolutionGeneration),
            ToRemotePath(entity.RemotePath, entity.HierarchyDelimiter));
    }

    /// <summary>Rebuilds the remote folder a binding row names, from the two columns that hold it.</summary>
    /// <param name="remotePath">The stored path.</param>
    /// <param name="hierarchyDelimiter">The stored delimiter, which is text because PostgreSQL pads a fixed-width character column.</param>
    /// <returns>The path the columns describe.</returns>
    /// <remarks>
    /// It takes the two columns rather than the row, so a read that projects them instead of materializing the entity —
    /// which every bounded read of these rows does — reaches the same reading. The delimiter is the one character the
    /// column holds, and an empty column is a folder whose server reported none; that is the reading
    /// <see cref="AddAsync" /> writes the inverse of, and it is stated here once so nothing gets a second chance to
    /// disagree about an empty string.
    /// </remarks>
    public static RemoteFolderPath ToRemotePath(string remotePath, string? hierarchyDelimiter) =>
        RemoteFolderPath.Create(
            remotePath,
            hierarchyDelimiter is { Length: > 0 } delimiter ? delimiter[0] : null);
}
