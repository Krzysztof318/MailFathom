// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.CodeCoverage;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Folders;

namespace MailMcp.Infrastructure.Persistence;

[RequiresIntegrationCoverage]
internal static class MailFolderEntityResolver
{
    public static async Task<MailFolderEntity> GetOrAddAsync(
        MailMcpDbContext dbContext,
        MailAccountId accountId,
        MailFolderName folderName,
        CancellationToken cancellationToken)
    {
        // Looked up by its alternate key, so the change-tracker pass is explicit rather than handled by FindAsync.
        var folder = await TrackedEntityLookup.SinglePendingOrPersistedAsync(
            dbContext.MailFolders,
            dbContext.MailFolders,
            candidate => candidate.MailboxAccountId == accountId.Value && candidate.RemoteName == folderName.Value,
            cancellationToken);

        if (folder is not null)
        {
            return folder;
        }

        // The account is keyed by the identifier itself, so FindAsync already resolves a pending insert without a query.
        var account = await dbContext.MailboxAccounts.FindAsync([accountId.Value], cancellationToken);
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
