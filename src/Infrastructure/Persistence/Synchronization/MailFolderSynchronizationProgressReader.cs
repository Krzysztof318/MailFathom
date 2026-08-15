// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Synchronization.Administration;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Synchronization;

/// <summary>Reads each alias's durable synchronization progress from the checkpoint of its freshest binding.</summary>
/// <remarks>
/// <para>
/// One alias can have been bound to several remote folders over time and each binding keeps a checkpoint of its own, so
/// the query keeps the binding whose progress moved most recently and discards the rest. The database performs that
/// choice, which is what keeps the rows crossing the boundary at one per alias: how many times a mail server has
/// recreated a folder grows without a ceiling, and an administrative read must not pay for that history.
/// </para>
/// <para>
/// A binding whose checkpoint has never advanced is left out rather than reported with no instant. It describes exactly
/// what a folder with no checkpoint at all describes — synchronization has committed nothing — and the caller composes
/// its folder list from configuration, so such a folder is already reported as one no run has reached.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class MailFolderSynchronizationProgressReader(MailFathomDbContext dbContext)
    : IMailFolderSynchronizationProgressReader
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<MailFolderSynchronizationProgress>> ReadAsync(CancellationToken cancellationToken)
    {
        var advanced = dbContext.MailFolders
            .AsNoTracking()
            .Where(folder => folder.SynchronizationCheckpoint != null
                && folder.SynchronizationCheckpoint.SynchronizedAt != null);

        // The freshest binding stated as "no other binding of this alias is fresher", because that is the shape a
        // provider translates: a grouped aggregate would give the newest instant without the UID recorded beside it,
        // and picking a row inside a group is not something the translation supports. The identifier settles a tie, so
        // two bindings advanced in the same instant still yield one row rather than two.
        var freshest = advanced.Where(folder => !advanced.Any(other =>
            other.MailboxAccountId == folder.MailboxAccountId
            && other.Alias == folder.Alias
            && (other.SynchronizationCheckpoint!.SynchronizedAt > folder.SynchronizationCheckpoint!.SynchronizedAt
                || (other.SynchronizationCheckpoint.SynchronizedAt == folder.SynchronizationCheckpoint.SynchronizedAt
                    && other.Id > folder.Id))));

        var progress = await freshest
            .Select(folder => new
            {
                folder.MailboxAccountId,
                folder.Alias,
                folder.SynchronizationCheckpoint!.UidValidity,
                folder.SynchronizationCheckpoint.LastSeenUid,
                folder.SynchronizationCheckpoint.SynchronizedAt,
            })
            .ToArrayAsync(cancellationToken);

        // Ordered here rather than in SQL because the order is ordinal by contract, and a database's collation is not
        // something MailFathom configures.
        return
        [
            .. progress
                .Select(static binding => new MailFolderSynchronizationProgress(
                    new MailFolderIdentity(
                        MailAccountId.Create(binding.MailboxAccountId),
                        MailFolderAlias.Create(binding.Alias)),
                    ImapUidValidity.Create(binding.UidValidity),
                    binding.LastSeenUid is { } lastSeenUid ? ImapUid.Create(lastSeenUid) : null,
                    binding.SynchronizedAt!.Value))
                .OrderBy(static entry => entry.Folder.AccountId.Value, StringComparer.Ordinal)
                .ThenBy(static entry => entry.Folder.Alias.Value, StringComparer.Ordinal),
        ];
    }
}
