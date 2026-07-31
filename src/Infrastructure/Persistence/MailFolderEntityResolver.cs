// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailMcp.CodeCoverage;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Folders;

namespace MailMcp.Infrastructure.Persistence;

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
        MailMcpDbContext dbContext,
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
        MailMcpDbContext dbContext,
        MailAccountId accountId,
        MailFolderResolutionId folderResolutionId,
        CancellationToken cancellationToken) =>
        await FindAsync(dbContext, accountId, folderResolutionId, cancellationToken)
        ?? throw new InvalidOperationException(
            $"Folder alias {accountId.Value}/{folderResolutionId} has no recorded binding, so nothing can be stored under it.");

    public static async Task<MailFolderEntity> AddAsync(
        MailMcpDbContext dbContext,
        MailAccountId accountId,
        MailFolderResolution resolution,
        CancellationToken cancellationToken)
    {
        // The account is keyed by the identifier itself, so FindAsync already resolves a pending insert without a query.
        var account = await dbContext.MailboxAccounts.FindAsync([accountId.Value], cancellationToken);
        if (account is null)
        {
            account = new MailboxAccountEntity { Id = accountId.Value };

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
}
