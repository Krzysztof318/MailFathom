// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
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
/// Every spend names the owner it was incurred for, because a deployment serving several people bounds each of them as
/// well as itself and a ledger keyed by the period alone could say only what was spent and never by whom. The owner
/// outlives its own record here on purpose:
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0014-single-tenant-multi-user-ownership-on-the-mail-account.md">ADR 0014</see>
/// keeps a spend row as a cost record rather than erasing it with the mail it paid to index.
/// </para>
/// </remarks>
public interface IEmbeddingSpendLedger
{
    /// <summary>Reads what one period has consumed so far, for one owner and for every owner together.</summary>
    /// <param name="periodStart">The period's start, as the budget places it.</param>
    /// <param name="owner">The owner whose own consumption is asked for beside the deployment's.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Both totals, which are zero for a period nothing has spent in yet.</returns>
    /// <remarks>
    /// One read rather than two, because a gate weighing an owner's figure taken at one moment against a deployment
    /// figure taken at another could admit a request neither total alone admits.
    /// </remarks>
    Task<EmbeddingSpendTotals> ReadConsumedInputCharactersAsync(
        DateTimeOffset periodStart,
        MailOwnerId owner,
        CancellationToken cancellationToken);

    /// <summary>Reads what one period has consumed across every owner, without naming one.</summary>
    /// <param name="periodStart">The period's start, as the budget places it.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The characters already sent inside that period by every owner together.</returns>
    /// <remarks>
    /// This is what an administrative reading asks. A deployment administrator acts for no owner, so the question they
    /// can be answered is the deployment's, and a gate that had to invent an owner to answer it would be attributing a
    /// figure to somebody who did not ask for it.
    /// </remarks>
    Task<long> ReadDeploymentConsumedInputCharactersAsync(
        DateTimeOffset periodStart,
        CancellationToken cancellationToken);

    /// <summary>Adds what one provider call sent to the period and owner it belongs to.</summary>
    /// <param name="session">The session whose transaction this write joins.</param>
    /// <param name="periodStart">The period's start, as the budget places it.</param>
    /// <param name="owner">The owner whose mail the call was embedding.</param>
    /// <param name="inputCharacterCount">The characters the call sent.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>A task that completes when the increment has been issued inside the caller's transaction.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the count is negative.</exception>
    /// <remarks>
    /// Written inside the transaction that commits the vectors the call produced, so the two are one durable fact: a
    /// crash between them cannot leave vectors nothing was charged for, or a charge for vectors that were never stored.
    /// The increment is expressed as an increment rather than as a read followed by a write, so two workers spending
    /// against one period add to each other instead of overwriting one another's total.
    /// </remarks>
    Task RecordSpendAsync(
        IPersistenceSession session,
        DateTimeOffset periodStart,
        MailOwnerId owner,
        long inputCharacterCount,
        CancellationToken cancellationToken);
}
