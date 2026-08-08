// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;

namespace MailFathom.Application.Emails.Embeddings.Limits;

/// <summary>Answers whether embedding may spend right now, and records what it did spend.</summary>
/// <remarks>
/// <para>
/// One type owns both halves because they are one decision: what a period admits is decided by what the same period has
/// been charged, and splitting the question from the answer would let a reader consult one clock and a writer another.
/// </para>
/// <para>
/// The reading is deliberately cheap and unconditional rather than cached. A period's total is one indexed row, the
/// read happens beside a network call that costs orders of magnitude more, and a cached figure would be wrong exactly
/// when it matters — after a restart, or while a second worker is spending against the same period.
/// </para>
/// </remarks>
public sealed class EmbeddingSpendGate
{
    private readonly IEmbeddingSpendLedger ledger;
    private readonly EmbeddingSpendBudget budget;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes a new gate over one deployment's budget.</summary>
    /// <param name="ledger">Keeps the durable count of what each period has spent.</param>
    /// <param name="budget">The ceiling and the period it is counted over.</param>
    /// <param name="timeProvider">Decides which period the present moment belongs to.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public EmbeddingSpendGate(
        IEmbeddingSpendLedger ledger,
        EmbeddingSpendBudget budget,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.ledger = ledger;
        this.budget = budget;
        this.timeProvider = timeProvider;
    }

    /// <summary>Reads where the current period stands, which is what a run consults before it starts spending.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The period, its consumption, and what it still admits.</returns>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    /// <remarks>
    /// This is the reading an activation weighs its estimate against, so it answers for a deployment with no ceiling
    /// too: the period is still named and still counted, and only the ceiling is absent.
    /// </remarks>
    public async Task<EmbeddingSpendPeriod> ReadCurrentPeriodAsync(CancellationToken cancellationToken)
    {
        var now = this.timeProvider.GetUtcNow();
        var periodStart = this.budget.PeriodStartAt(now);
        var consumed = await this.ledger.ReadConsumedInputCharactersAsync(periodStart, cancellationToken);

        return new EmbeddingSpendPeriod(
            periodStart,
            periodStart + this.budget.Period,
            consumed,
            this.budget.IsUnbounded ? null : this.budget.MaxInputCharactersPerPeriod);
    }

    /// <summary>Charges one provider call to the period it happened in.</summary>
    /// <param name="session">The session committing the vectors that call produced.</param>
    /// <param name="inputCharacterCount">The characters the call sent.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>A task that completes when the charge has been issued inside the caller's transaction.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the count is negative.</exception>
    /// <remarks>
    /// A deployment with no ceiling is charged exactly as one with a ceiling is. The count is what an operator watches
    /// to decide whether to declare a ceiling at all, so leaving it unwritten would make the figure appear only once it
    /// was already too late to be useful.
    /// </remarks>
    public Task RecordSpendAsync(
        IPersistenceSession session,
        long inputCharacterCount,
        CancellationToken cancellationToken)
    {
        var periodStart = this.budget.PeriodStartAt(this.timeProvider.GetUtcNow());

        return this.ledger.RecordSpendAsync(session, periodStart, inputCharacterCount, cancellationToken);
    }
}
