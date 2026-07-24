// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Domain.Accounts;
using MailMcp.Domain.Folders;

namespace MailMcp.Application.Synchronization;

/// <summary>Creates mailbox sessions exposed only through application-owned mail operations.</summary>
public interface IMailboxSessionFactory
{
    /// <summary>Opens a folder read-only so synchronization cannot mutate remote mailbox state.</summary>
    Task<IMailboxSession> OpenReadOnlyAsync(
        MailAccountId accountId,
        MailFolderName folderName,
        CancellationToken cancellationToken);
}
