// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.Folders;
using MailMcp.Application.Persistence;
using MailMcp.CodeCoverage;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Folders;
using Microsoft.EntityFrameworkCore;

namespace MailMcp.Infrastructure.Persistence;

/// <summary>EF Core implementation for durable alias bindings.</summary>
/// <remarks>
/// The read path uses the scoped context because it joins no transaction. The write path uses the context enlisted in
/// the caller's session, so a binding can only be written inside the transaction the caller opened.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class MailFolderResolutionStore(MailMcpDbContext readContext) : IMailFolderResolutionStore
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

        if (existingBinding is not null)
        {
            return;
        }

        await MailFolderEntityResolver.AddAsync(writeContext, accountId, resolution, cancellationToken);
    }

    private static MailFolderResolution ToResolution(MailFolderEntity entity) => new(
        MailFolderAlias.Create(entity.Alias),
        MailFolderResolutionGeneration.Create(entity.ResolutionGeneration),
        RemoteFolderPath.Create(
            entity.RemotePath,
            entity.HierarchyDelimiter is { Length: > 0 } delimiter ? delimiter[0] : null));
}
