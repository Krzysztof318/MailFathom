// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;

namespace MailFathom.Application.Emails.Embeddings.Limits;

/// <summary>Keeps the durable count of what each budget period has already sent to a provider.</summary>
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
/// </remarks>
public interface IEmbeddingSpendLedger
{
    /// <summary>Reads what one period has consumed so far.</summary>
    /// <param name="periodStart">The period's start, as the budget places it.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The characters already sent inside that period, which is zero for a period nothing has spent in yet.</returns>
    Task<long> ReadConsumedInputCharactersAsync(DateTimeOffset periodStart, CancellationToken cancellationToken);

    /// <summary>Adds what one provider call sent to the period it belongs to.</summary>
    /// <param name="session">The session whose transaction this write joins.</param>
    /// <param name="periodStart">The period's start, as the budget places it.</param>
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
        long inputCharacterCount,
        CancellationToken cancellationToken);
}
