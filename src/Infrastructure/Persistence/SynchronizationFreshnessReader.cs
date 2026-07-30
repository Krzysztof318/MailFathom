// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.Emails;
using MailMcp.Application.Synchronization;
using MailMcp.CodeCoverage;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Folders;
using Microsoft.EntityFrameworkCore;

namespace MailMcp.Infrastructure.Persistence;

/// <summary>Reads how current each folder's local copy is from the durable synchronization checkpoints.</summary>
/// <remarks>
/// A folder with no checkpoint row is reported with no timestamp rather than dropped, which is why the walk starts from
/// the folders and reaches the checkpoints through an optional relationship. Reading it the other way round would omit
/// exactly the folders whose staleness a caller most needs to see: the ones synchronization has never reached.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class SynchronizationFreshnessReader(MailMcpDbContext dbContext) : ISynchronizationFreshnessReader
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
        // something MailMcp configures.
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

        return folders;
    }
}
