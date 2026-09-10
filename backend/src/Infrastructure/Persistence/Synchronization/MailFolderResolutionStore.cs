// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Folders;
using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Synchronization;

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
        MailAccountIdentity account,
        MailFolderAlias folderAlias,
        CancellationToken cancellationToken)
    {
        var aliasValue = folderAlias.Value;
        var userValue = account.User.Value;
        var accountValue = account.Id.Value;

        // Every generation of an alias is kept, because occurrences stay attributable to the folder they came from,
        // so the current binding is the highest generation rather than the only row. The user leads the narrowing, as
        // it leads the index: an alias is MailFathom's own name within one user's account.
        var entity = await readContext.MailFolders
            .AsNoTracking()
            .Where(folder => folder.UserId == userValue
                && folder.MailboxAccountId == accountValue
                && folder.Alias == aliasValue)
            .OrderByDescending(folder => folder.ResolutionGeneration)
            .FirstOrDefaultAsync(cancellationToken);

        return entity is null ? null : MailFolderEntityResolver.ToResolution(entity);
    }

    /// <inheritdoc />
    public async Task<MailFolderAlias?> GetAliasBoundToAsync(
        MailAccountIdentity account,
        RemoteFolderPath remotePath,
        CancellationToken cancellationToken)
    {
        var pathValue = remotePath.Value;
        var userValue = account.User.Value;
        var accountValue = account.Id.Value;

        // The generation is compared against the alias's own highest rather than taken as the highest of the rows the
        // path matched: an alias the server has since made MailFathom rebind still holds its earlier generation naming
        // the old folder, and answering with that alias would name a folder it no longer is. Ordered so that two aliases
        // bound to one folder — which configuration permits and nothing else disambiguates — answer the same way twice.
        // The delimiter column is deliberately not compared, which is the reading RemoteFolderPath.NamesSameFolderAs
        // states: a binding written before the server reported a delimiter names the folder one written after names.
        var alias = await readContext.MailFolders
            .AsNoTracking()
            .Where(folder => folder.UserId == userValue
                && folder.MailboxAccountId == accountValue
                && folder.RemotePath == pathValue
                && folder.ResolutionGeneration == readContext.MailFolders
                    .Where(binding => binding.UserId == folder.UserId
                        && binding.MailboxAccountId == folder.MailboxAccountId
                        && binding.Alias == folder.Alias)
                    .Max(binding => binding.ResolutionGeneration))
            .OrderBy(folder => folder.Alias)
            .Select(folder => folder.Alias)
            .FirstOrDefaultAsync(cancellationToken);

        return alias is null ? null : MailFolderAlias.Create(alias);
    }

    /// <inheritdoc />
    public async Task SaveResolutionAsync(
        IPersistenceSession session,
        MailAccountIdentity account,
        MailFolderResolution resolution,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resolution);

        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var existingBinding = await MailFolderEntityResolver.FindAsync(
            writeContext,
            account,
            resolution.Id,
            cancellationToken);

        if (existingBinding is null)
        {
            await MailFolderEntityResolver.AddAsync(writeContext, account, resolution, cancellationToken);

            return;
        }

        // A row already holding this generation is only this run's own binding when it names the same remote folder.
        // Two overlapping runs that resolved the same alias from the same generation to different folders would
        // otherwise both proceed: the loser would adopt the winner's row and store its own folder's occurrences and
        // checkpoint under it, so one (alias, generation) would name two remote folders — exactly what the generation
        // exists to make impossible. It is reported as a conflict, and the next run resolves against what is durable.
        if (MailFolderEntityResolver.ToResolution(existingBinding) != resolution)
        {
            throw new PersistenceConcurrencyConflictException(
                $"Folder alias {account.Id.Value}/{resolution.Id} was bound to a different remote folder by another writer before this run recorded its own binding.");
        }
    }
}
