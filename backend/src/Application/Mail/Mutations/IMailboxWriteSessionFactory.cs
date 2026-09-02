// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Synchronization.Sessions;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Transport;

namespace MailFathom.Application.Mail.Mutations;

/// <summary>Opens the one session in MailFathom able to change a remote mailbox.</summary>
/// <remarks>
/// <para>
/// It is a separate factory from <see cref="IMailboxSessionFactory" /> rather than a second method on it, because the
/// separation is what the guarantee rests on: a component that never takes this dependency cannot obtain a session
/// that writes, whatever a later change does inside it.
/// </para>
/// <para>
/// An account holds at most one write connection. It is opened the first time a session is asked for, kept while it is
/// idle for a bounded period rather than closed after each mutation, and closed when that period elapses, so a run of
/// mutations costs one handshake instead of one per change and an account nobody is changing holds no connection at
/// all. A second session for the same account waits for the first to be disposed.
/// </para>
/// </remarks>
public interface IMailboxWriteSessionFactory
{
    /// <summary>Selects a folder for writing so a mutation can be issued against the emails in it.</summary>
    /// <param name="accountId">The local account identifier.</param>
    /// <param name="folder">The alias binding whose remote path is selected, and which scopes every occurrence the session accepts.</param>
    /// <param name="transportSecurityPolicy">The connection and authentication policy the implementation must obey.</param>
    /// <param name="cancellationToken">Cancels waiting for the account's write connection, connecting, authenticating, and selecting the folder.</param>
    /// <returns>An open write session the caller owns and must dispose.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="folder" /> or <paramref name="transportSecurityPolicy" /> is <see langword="null" />.</exception>
    /// <exception cref="MailboxUnavailableException">Thrown when the mail server did not accept the session within its configured resilience budget.</exception>
    /// <remarks>
    /// The policy is an input rather than something the implementation resolves, so an adapter cannot widen the
    /// permitted authentication mechanisms or downgrade the connection on its own. The session receives the whole
    /// binding rather than a path for the same reason the read session does: an occurrence identity is scoped by the
    /// generation the binding carries.
    /// </remarks>
    Task<IMailboxWriteSession> OpenForWritingAsync(
        MailAccountId accountId,
        MailFolderResolution folder,
        MailTransportSecurityPolicy transportSecurityPolicy,
        CancellationToken cancellationToken);
}
