// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Synchronization.Checkpoints;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Synchronization;

/// <summary>Reads how current each folder's local copy is from the durable synchronization checkpoints.</summary>
/// <remarks>
/// A folder with no checkpoint row is reported with no timestamp rather than dropped, which is why the walk starts from
/// the folders and reaches the checkpoints through an optional relationship. Reading it the other way round would omit
/// exactly the folders whose staleness a caller most needs to see: the ones synchronization has never reached.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class SynchronizationFreshnessReader(MailFathomDbContext dbContext) : ISynchronizationFreshnessReader
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<MailboxFolderFreshness>> ReadAsync(
        MailboxScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        // One alias can have been bound to several remote folders over time, and each binding keeps its own checkpoint.
        // PostgreSQL performs the aggregate so the rows that cross the boundary number one per alias in scope, which is
        // the size of the result itself: the number of historical bindings behind an alias grows without a ceiling as a
        // server recreates folders, and grouping in process would make an unrelated request pay for that history.
        var freshestBindings = await Matching(dbContext.MailFolders.AsNoTracking(), scope)
            .Select(folder => new
            {
                folder.MailboxAccountId,
                folder.Alias,
                SynchronizedAt = folder.SynchronizationCheckpoint == null
                    ? null
                    : folder.SynchronizationCheckpoint.SynchronizedAt,
            })
            .GroupBy(binding => new { binding.MailboxAccountId, binding.Alias })
            .Select(alias => new
            {
                alias.Key.MailboxAccountId,
                alias.Key.Alias,
                SynchronizedAt = alias.Max(binding => binding.SynchronizedAt),
            })
            .ToArrayAsync(cancellationToken);

        // Ordered here rather than in SQL because the order is ordinal by contract, and a database's collation is not
        // something MailFathom configures.
        return
        [
            .. freshestBindings
                .Select(alias => new MailboxFolderFreshness(
                    MailAccountId.Create(alias.MailboxAccountId),
                    MailFolderAlias.Create(alias.Alias),
                    alias.SynchronizedAt))
                .OrderBy(freshness => freshness.AccountId.Value, StringComparer.Ordinal)
                .ThenBy(freshness => freshness.FolderAlias.Value, StringComparer.Ordinal),
        ];
    }

    private static IQueryable<MailFolderEntity> Matching(IQueryable<MailFolderEntity> folders, MailboxScope scope)
    {
        if (scope.AccountIds.Count > 0)
        {
            var accountIds = scope.AccountIds.Select(static accountId => accountId.Value).ToArray();
            folders = folders.Where(folder => accountIds.Contains(folder.MailboxAccountId));
        }

        if (scope.FolderAliases.Count > 0)
        {
            var folderAliases = scope.FolderAliases.Select(static alias => alias.Value).ToArray();
            folders = folders.Where(folder => folderAliases.Contains(folder.Alias));
        }

        // A folder this read returns nothing from is withheld from how fresh it is as well. The timestamp says a folder
        // exists and when it was last read, which is exactly what a caller must not learn about a folder they may not
        // read — and what would name the junk folder to a caller who did not ask for it.
        return ExcludedMailFolders.Excluding(folders, scope.WithheldFolders);
    }
}
