// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;

namespace MailFathom.Application.Emails.Embeddings.Limits;

/// <summary>Keeps the durable count of what each budget period has already sent to a provider, and for whom.</summary>
/// <remarks>
/// <para>
/// Durable rather than held in memory, because the failure a spend ceiling exists to prevent is precisely the one an
/// in-process counter cannot see: a process that crashes and restarts in a loop would begin every period again from
/// zero and spend the whole ceiling on each attempt.
/// </para>
/// <para>
/// It is also deliberately not derived from the stored vectors, which would need no table at all. A generation that is
/// superseded has its vectors removed in bounded batches, so a count taken over them would erase the record of a spend
/// that genuinely happened — and the period in which a model change is paid for is exactly the period an operator is
/// watching.
/// </para>
/// <para>
/// Every spend names the users it was incurred for, because a deployment serving several people bounds each of them
/// as well as itself and a ledger keyed by the period alone could say only what was spent and never by whom. What the
/// deployment spent is counted separately from them rather than summed out of them, because
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0014-single-tenant-multi-user-ownership-on-the-mail-account.md">ADR 0014</see>
/// counts a shared mailbox in full against each of its users: the per-user figures deliberately add up to more than
/// was sent, and a deployment ceiling read off their sum would stop a shared mailbox at a fraction of what an
/// operator declared. The user outlives its own record here on purpose:
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0014-single-tenant-multi-user-ownership-on-the-mail-account.md">ADR 0014</see>
/// keeps a spend row as a cost record rather than erasing it with the mail it paid to index.
/// </para>
/// </remarks>
public interface IEmbeddingSpendLedger
{
    /// <summary>Reads what one period has consumed so far, for one user and for the deployment.</summary>
    /// <param name="periodStart">The period's start, as the budget places it.</param>
    /// <param name="user">The user whose own consumption is asked for beside the deployment's.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Both totals, which are zero for a period nothing has spent in yet.</returns>
    /// <remarks>
    /// One read rather than two, because a gate weighing a user's figure taken at one moment against a deployment
    /// figure taken at another could admit a request neither total alone admits.
    /// </remarks>
    Task<EmbeddingSpendTotals> ReadConsumedInputCharactersAsync(
        DateTimeOffset periodStart,
        UserId user,
        CancellationToken cancellationToken);

    /// <summary>Reads what one period actually sent to a provider, whoever it was counted against.</summary>
    /// <param name="periodStart">The period's start, as the budget places it.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The characters already sent inside that period, counted once per call.</returns>
    /// <remarks>
    /// This is what an administrative reading asks, and it is the deployment's own count rather than the sum of the
    /// per-user ones: a call embedding a mailbox two people are assigned is charged to both of them and sent once.
    /// A deployment administrator acts for no user, so the question they can be answered is this one, and a gate that
    /// had to invent a user to answer it would be attributing a figure to somebody who did not ask for it.
    /// </remarks>
    Task<long> ReadDeploymentConsumedInputCharactersAsync(
        DateTimeOffset periodStart,
        CancellationToken cancellationToken);

    /// <summary>Adds what one provider call sent, once to the period and in full to each user it is counted against.</summary>
    /// <param name="session">The session whose transaction this write joins.</param>
    /// <param name="periodStart">The period's start, as the budget places it.</param>
    /// <param name="users">The users the call is charged to, which is empty for a mailbox assigned to nobody.</param>
    /// <param name="inputCharacterCount">The characters the call sent.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>A task that completes when every increment has been issued inside the caller's transaction.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> or <paramref name="users" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the count is negative.</exception>
    /// <remarks>
    /// One call rather than one per user, because the deployment's own figure is charged here too and charging it from
    /// a loop would multiply it by however many people share the mailbox. A call charged to nobody still moves the
    /// deployment's figure, so a mailbox with no assigned user is bounded by the deployment's ceiling rather than by
    /// nothing at all.
    /// <para>
    /// Written inside the transaction that commits the vectors the call produced, so the two are one durable fact: a
    /// crash between them cannot leave vectors nothing was charged for, or a charge for vectors that were never stored.
    /// Each increment is expressed as an increment rather than as a read followed by a write, so two workers spending
    /// against one period add to each other instead of overwriting one another's total.
    /// </para>
    /// </remarks>
    Task RecordSpendAsync(
        IPersistenceSession session,
        DateTimeOffset periodStart,
        IReadOnlyCollection<UserId> users,
        long inputCharacterCount,
        CancellationToken cancellationToken);
}
