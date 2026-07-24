// Copyright © 2026 Krzysztof Kasprowicz

using System.Diagnostics.CodeAnalysis;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Folders;
using Microsoft.EntityFrameworkCore;

namespace MailMcp.Infrastructure.Persistence;

// TODO: Remove this exclusion when the planned PostgreSQL integration tests are enabled.
[ExcludeFromCodeCoverage(Justification = "Will be covered later by PostgreSQL integration tests.")]
internal static class MailFolderEntityResolver
{
    public static async Task<MailFolderEntity> GetOrAddAsync(
        MailMcpDbContext dbContext,
        MailAccountId accountId,
        MailFolderName folderName,
        CancellationToken cancellationToken)
    {
        var folder = dbContext.MailFolders.Local.SingleOrDefault(
            candidate => candidate.MailboxAccountId == accountId.Value && candidate.RemoteName == folderName.Value)
            ?? await dbContext.MailFolders.SingleOrDefaultAsync(
                candidate => candidate.MailboxAccountId == accountId.Value && candidate.RemoteName == folderName.Value,
                cancellationToken);
        if (folder is not null)
        {
            return folder;
        }

        var account = dbContext.MailboxAccounts.Local.SingleOrDefault(candidate => candidate.Id == accountId.Value)
            ?? await dbContext.MailboxAccounts.SingleOrDefaultAsync(candidate => candidate.Id == accountId.Value, cancellationToken);
        if (account is null)
        {
            account = new MailboxAccountEntity { Id = accountId.Value };
            dbContext.MailboxAccounts.Add(account);
        }

        folder = new MailFolderEntity
        {
            MailboxAccountId = accountId.Value,
            RemoteName = folderName.Value,
            MailboxAccount = account,
        };
        dbContext.MailFolders.Add(folder);
        return folder;
    }
}
