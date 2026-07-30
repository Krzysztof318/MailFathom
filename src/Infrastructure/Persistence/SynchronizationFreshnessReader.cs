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

        var bindings = await Matching(dbContext.MailFolders.AsNoTracking(), scope)
            .Select(folder => new
            {
                folder.MailboxAccountId,
                folder.Alias,
                SynchronizedAt = folder.SynchronizationCheckpoint == null
                    ? null
                    : folder.SynchronizationCheckpoint.SynchronizedAt,
            })
            .ToArrayAsync(cancellationToken);

        // One alias can have been bound to several remote folders over time, and each binding keeps its own checkpoint.
        // The grouping happens here rather than in SQL because the rows are already narrowed to the queried scope and
        // number one per binding, so the aggregate costs nothing while an aggregate over an optional relationship would
        // be one more query shape to prove translatable.
        return
        [
            .. bindings
                .GroupBy(binding => (binding.MailboxAccountId, binding.Alias))
                .Select(alias => new MailboxFolderFreshness(
                    MailAccountId.Create(alias.Key.MailboxAccountId),
                    MailFolderAlias.Create(alias.Key.Alias),
                    alias.Max(binding => binding.SynchronizedAt)))
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
