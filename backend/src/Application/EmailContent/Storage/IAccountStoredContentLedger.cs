// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;

namespace MailFathom.Application.EmailContent.Storage;

/// <summary>Answers how much stored mail content one account holds, without reading its mail to find out.</summary>
/// <remarks>
/// <para>
/// A deployment-wide ceiling can ask the database what a table occupies, in constant time and without touching a row.
/// A per-user ceiling has no such question available: a catalogue answers for a table and never for a share of one, and
/// summing a mailbox's payload lengths would put a scan of a whole mailbox in front of every folder run. So the figure
/// is maintained as it changes — one counter per account, moved inside the transaction that stores or removes the
/// payload — and read here as a single row.
/// </para>
/// <para>
/// The counter is the account's rather than a user's because the mail is one copy however many users are assigned the
/// mailbox. A user's figure is the sum over the accounts assigned to them, which is what
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0014-single-tenant-multi-user-ownership-on-the-mail-account.md">ADR 0014</see>
/// requires of a shared mailbox: it counts in full against every user assigned to it, and an assignment moves the
/// figure without rewriting a row.
/// </para>
/// <para>
/// The figure is the payload bytes rather than what a disk fills with, which is what the deployment's ceiling counts.
/// The two are deliberately different quantities, because only one of them is attributable to a mailbox at all, and
/// nothing reconciles one against the other.
/// </para>
/// <para>
/// A maintained counter can drift where a payload leaves storage by a path nothing told it about, so the recomputation
/// it was derived from stays available rather than being a one-off in a migration. That is what makes drift repairable
/// instead of permanent, and it is the same statement the counter is asserted against.
/// </para>
/// <para>
/// Both members refuse an account naming nothing. Nothing gates this port — a synchronization run calls it directly —
/// so the refusal is the port's own, and an implementation that answered such an account would be keeping a counter
/// for "nothing" that reads exactly like a working figure.
/// </para>
/// </remarks>
public interface IAccountStoredContentLedger
{
    /// <summary>Reads what one account's stored mail content occupies, from the maintained counter.</summary>
    /// <param name="account">The account asked about.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The bytes that account's stored payloads hold.</returns>
    /// <remarks>
    /// An account whose counter has never been written is derived once and adopted rather than answered as zero, so an
    /// upgraded deployment and one that has just erased and re-synchronized both start from what storage actually
    /// holds. Every later read is the single row.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="account" /> names nothing.</exception>
    Task<long> ReadStoredContentBytesAsync(MailAccountId account, CancellationToken cancellationToken);

    /// <summary>Recomputes one account's figure from its stored payloads and adopts it.</summary>
    /// <param name="account">The account whose counter is re-derived.</param>
    /// <param name="cancellationToken">Cancels the recomputation.</param>
    /// <returns>The figure that was adopted.</returns>
    /// <remarks>
    /// This is the expensive answer the maintained one exists to avoid, so it is a repair rather than something a run
    /// reaches for. Unlike every movement of the figure, it replaces a total rather than adding to one, so it cannot be
    /// left to race: it claims the account's row before it recomputes and holds it until the recomputation is written,
    /// so a store committing meanwhile is either counted by the recomputation or applied on top of it, never discarded
    /// by it. That makes it the one operation here which needs a transaction of its own.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="account" /> names nothing.</exception>
    Task<long> RederiveStoredContentBytesAsync(MailAccountId account, CancellationToken cancellationToken);
}
