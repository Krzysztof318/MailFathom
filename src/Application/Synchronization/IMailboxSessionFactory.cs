// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Domain.Accounts;
using MailMcp.Domain.Folders;
using MailMcp.Domain.Transport;

namespace MailMcp.Application.Synchronization;

/// <summary>Creates mailbox sessions exposed only through application-owned mail operations.</summary>
public interface IMailboxSessionFactory
{
    /// <summary>Opens a folder read-only so synchronization cannot mutate remote mailbox state.</summary>
    /// <param name="accountId">The local account identifier.</param>
    /// <param name="folderName">The remote folder to select read-only.</param>
    /// <param name="transportSecurityPolicy">The connection and authentication policy the implementation must obey.</param>
    /// <param name="cancellationToken">Cancels connecting, authenticating, and selecting the folder.</param>
    /// <returns>An open read-only mailbox session the caller owns and must dispose.</returns>
    /// <exception cref="MailboxUnavailableException">Thrown when the mail server did not accept the session within its configured resilience budget.</exception>
    /// <remarks>
    /// The policy is an input rather than something the implementation resolves, so an adapter cannot widen the
    /// permitted authentication mechanisms or downgrade the connection on its own.
    /// </remarks>
    Task<IMailboxSession> OpenReadOnlyAsync(
        MailAccountId accountId,
        MailFolderName folderName,
        MailTransportSecurityPolicy transportSecurityPolicy,
        CancellationToken cancellationToken);
}
