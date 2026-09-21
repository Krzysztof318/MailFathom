// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Synchronization.Restore;

/// <summary>Records an operator's verdict on an append the restore issued and never got an answer to.</summary>
/// <remarks>
/// <para>
/// The one act of this mode that only a person can perform. An <c>APPEND</c> whose answer never came back leaves a
/// folder that may hold the copy and may not, and nothing the folder shows afterwards tells a copy MailFathom
/// appended apart from one somebody else put there — so MailFathom refuses to guess, and an operator looks and says.
/// Until they do, the account stays in <see cref="MailAccountCustodyPhase.Restoring" />.
/// </para>
/// <para>
/// It is a separate use case from the pass rather than a method on it because it has a caller with a principal behind
/// it. The pass runs under the account's lease with nobody asking, and a permission check there would either have no
/// principal to read or would be read from whichever request happened to open the scope. See
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0034-holding-a-mailbox-mailfathom-alone-keeps.md">ADR 0034</see>.
/// </para>
/// </remarks>
public sealed class MailboxRestoreSettlement
{
    private readonly IMailboxRestoreStore store;
    private readonly OptimisticConcurrencyRetryPolicy commitPolicy;
    private readonly AccessAuthorization authorization;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes a new instance of the <see cref="MailboxRestoreSettlement" /> class.</summary>
    /// <param name="store">Holds the records an operator settles.</param>
    /// <param name="commitPolicy">Commits the verdict.</param>
    /// <param name="authorization">Decides whether the caller may settle one at all.</param>
    /// <param name="timeProvider">Supplies the instant the verdict is recorded at.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public MailboxRestoreSettlement(
        IMailboxRestoreStore store,
        OptimisticConcurrencyRetryPolicy commitPolicy,
        AccessAuthorization authorization,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(commitPolicy);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.store = store;
        this.commitPolicy = commitPolicy;
        this.authorization = authorization;
        this.timeProvider = timeProvider;
    }

    /// <summary>Records what an operator found in the folder the unanswered append was issued against.</summary>
    /// <param name="account">The account the record belongs to.</param>
    /// <param name="record">The record being settled.</param>
    /// <param name="sourceHoldsTheCopy">Whether the operator found the copy in the folder.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns><see langword="true" /> when the record was still standing and the verdict was written.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller's grant omits <c>mailfathom.admin.custody.write</c>.</exception>
    /// <remarks>
    /// Under the custody permission rather than the configuration writer's, for the reason the switch itself is: a
    /// verdict of <see langword="false" /> puts a message back onto somebody's mail server, and one of
    /// <see langword="true" /> leaves a message MailFathom holds with no occurrence it will ever write. Both are acts
    /// on the mailbox rather than readings of it.
    /// </remarks>
    public Task<bool> SettleAsync(
        MailAccountId account,
        MailboxRestoreAppendId record,
        bool sourceHoldsTheCopy,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminCustodyWrite);

        return this.commitPolicy.CommitAsync(
            (session, token) => this.store.SettleAppendAsync(
                session,
                account,
                record,
                sourceHoldsTheCopy,
                this.timeProvider.GetUtcNow(),
                token),
            cancellationToken);
    }
}
