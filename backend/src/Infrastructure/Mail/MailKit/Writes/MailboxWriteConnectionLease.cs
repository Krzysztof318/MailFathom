// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;

namespace MailFathom.Infrastructure.Mail.MailKit.Writes;

/// <summary>Holds the account's write connection for one session, and gives it back when the session ends.</summary>
/// <remarks>
/// Disposing the lease releases the connection to the next caller and starts its idle clock; it does not close the
/// connection, which is what makes a run of mutations cost one handshake rather than one each. Disposal is idempotent,
/// because a session disposed twice must not release a gate it no longer holds and hand a second caller a connection
/// somebody else is using.
/// </remarks>
internal sealed class MailboxWriteConnectionLease : IAsyncDisposable
{
    private readonly Func<ValueTask> release;

    private bool released;

    internal MailboxWriteConnectionLease(
        MailAccountId accountId,
        MailKitImapConnection connection,
        Func<ValueTask> release)
    {
        this.AccountId = accountId;
        this.Connection = connection;
        this.release = release;
    }

    /// <summary>Gets the account the leased connection belongs to.</summary>
    internal MailAccountId AccountId { get; }

    /// <summary>Gets the connection, which is selected for writing and used by this lease alone.</summary>
    internal MailKitImapConnection Connection { get; }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (this.released)
        {
            return ValueTask.CompletedTask;
        }

        this.released = true;

        return this.release();
    }
}
