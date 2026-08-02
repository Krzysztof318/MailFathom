// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Folders;
using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence;

/// <summary>EF Core implementation for durable alias bindings.</summary>
/// <remarks>
/// The read path uses the scoped context because it joins no transaction. The write path uses the context enlisted in
/// the caller's session, so a binding can only be written inside the transaction the caller opened.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class MailFolderResolutionStore(MailFathomDbContext readContext) : IMailFolderResolutionStore
{
    /// <inheritdoc />
    public async Task<MailFolderResolution?> GetCurrentResolutionAsync(
        MailAccountId accountId,
        MailFolderAlias folderAlias,
        CancellationToken cancellationToken)
    {
        var aliasValue = folderAlias.Value;

        // Every generation of an alias is kept, because occurrences stay attributable to the folder they came from,
        // so the current binding is the highest generation rather than the only row.
        var entity = await readContext.MailFolders
            .AsNoTracking()
            .Where(folder => folder.MailboxAccountId == accountId.Value && folder.Alias == aliasValue)
            .OrderByDescending(folder => folder.ResolutionGeneration)
            .FirstOrDefaultAsync(cancellationToken);

        return entity is null ? null : ToResolution(entity);
    }

    /// <inheritdoc />
    public async Task SaveResolutionAsync(
        IPersistenceSession session,
        MailAccountId accountId,
        MailFolderResolution resolution,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resolution);

        var writeContext = EfCorePersistenceSessionAccessor.DbContextOf(session);
        var existingBinding = await MailFolderEntityResolver.FindAsync(
            writeContext,
            accountId,
            resolution.Id,
            cancellationToken);

        if (existingBinding is null)
        {
            await MailFolderEntityResolver.AddAsync(writeContext, accountId, resolution, cancellationToken);

            return;
        }

        // A row already holding this generation is only this run's own binding when it names the same remote folder.
        // Two overlapping runs that resolved the same alias from the same generation to different folders would
        // otherwise both proceed: the loser would adopt the winner's row and store its own folder's occurrences and
        // checkpoint under it, so one (alias, generation) would name two remote folders — exactly what the generation
        // exists to make impossible. It is reported as a conflict, and the next run resolves against what is durable.
        if (ToResolution(existingBinding) != resolution)
        {
            throw new PersistenceConcurrencyConflictException(
                $"Folder alias {accountId.Value}/{resolution.Id} was bound to a different remote folder by another writer before this run recorded its own binding.");
        }
    }

    private static MailFolderResolution ToResolution(MailFolderEntity entity) => new(
        MailFolderAlias.Create(entity.Alias),
        MailFolderResolutionGeneration.Create(entity.ResolutionGeneration),
        RemoteFolderPath.Create(
            entity.RemotePath,
            entity.HierarchyDelimiter is { Length: > 0 } delimiter ? delimiter[0] : null));
}
