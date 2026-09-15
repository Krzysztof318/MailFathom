// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Accounts.Custody;

/// <summary>Persists what an administrator asked one account's custody to be, and how far the work of granting it has got.</summary>
/// <remarks>
/// <para>
/// The requested value and the phase are two columns on the account rather than one, and they are written by different
/// callers: an administrative command asks for a custody, and the account's own supervision moves the phase once the
/// work that direction owes has been done. Neither may write the other's, which is what keeps a switch a period rather
/// than an instant.
/// </para>
/// <para>
/// Every phase write is conditional on the phase it was decided from, because the account's supervision may move across
/// a lease handover: a replica that read <c>Mirrored</c>, spent a run draining, and then wrote <c>Held</c> would be
/// writing over whatever the replica that replaced it had since decided. See
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0034-holding-a-mailbox-mailfathom-alone-keeps.md">ADR 0034</see>.
/// </para>
/// </remarks>
public interface IMailAccountCustodyStore
{
    /// <summary>Reads one account's custody, joining no transaction.</summary>
    /// <param name="account">The account.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The state, or <see langword="null" /> where the deployment holds no such account.</returns>
    Task<MailAccountCustodyState?> ReadAsync(MailAccountId account, CancellationToken cancellationToken);

    /// <summary>Writes the custody an administrator asked for, leaving the phase where it stands.</summary>
    /// <param name="session">The transaction the switch commits in.</param>
    /// <param name="account">The account.</param>
    /// <param name="requested">The custody asked for.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The state the account had before the write, or <see langword="null" /> where the deployment holds no such account.</returns>
    /// <remarks>
    /// The phase is untouched deliberately. Asking for <c>HoldMailbox</c> does not make an account held — the account's
    /// next run carries whatever it already asked its server for and moves the phase itself — and asking to mirror
    /// again does not make it mirrored, because the mailbox has to be appended back first.
    /// </remarks>
    Task<MailAccountCustodyState?> RequestAsync(
        IPersistenceSession session,
        MailAccountId account,
        MailAccountCustody requested,
        CancellationToken cancellationToken);

    /// <summary>Moves one account's phase, but only from the phase it was decided from.</summary>
    /// <param name="session">The transaction the move commits in.</param>
    /// <param name="account">The account.</param>
    /// <param name="decidedFrom">The phase the caller read before deciding.</param>
    /// <param name="moveTo">The phase to write.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns><see langword="true" /> when the account still stood at <paramref name="decidedFrom" /> and the phase was written.</returns>
    /// <remarks>A refused move is an ordinary answer: another replica moved the account, and this one re-reads rather than writing over it.</remarks>
    Task<bool> MovePhaseAsync(
        IPersistenceSession session,
        MailAccountId account,
        MailAccountCustodyPhase decidedFrom,
        MailAccountCustodyPhase moveTo,
        CancellationToken cancellationToken);
}
