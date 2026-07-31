// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Transport;

namespace MailFathom.Application.Synchronization;

/// <summary>Creates mailbox sessions exposed only through application-owned mail operations.</summary>
public interface IMailboxSessionFactory
{
    /// <summary>Opens a folder read-only so synchronization cannot mutate remote mailbox state.</summary>
    /// <param name="accountId">The local account identifier.</param>
    /// <param name="folder">The alias binding whose remote path is selected read-only.</param>
    /// <param name="transportSecurityPolicy">The connection and authentication policy the implementation must obey.</param>
    /// <param name="cancellationToken">Cancels connecting, authenticating, and selecting the folder.</param>
    /// <returns>An open read-only mailbox session the caller owns and must dispose.</returns>
    /// <exception cref="MailboxUnavailableException">Thrown when the mail server did not accept the session within its configured resilience budget.</exception>
    /// <remarks>
    /// The session receives the whole binding rather than a path, because every occurrence identity it produces is
    /// scoped by the generation the binding carries.
    /// <para>
    /// The policy is an input rather than something the implementation resolves, so an adapter cannot widen the
    /// permitted authentication mechanisms or downgrade the connection on its own.
    /// </para>
    /// </remarks>
    Task<IMailboxSession> OpenReadOnlyAsync(
        MailAccountId accountId,
        MailFolderResolution folder,
        MailTransportSecurityPolicy transportSecurityPolicy,
        CancellationToken cancellationToken);
}
