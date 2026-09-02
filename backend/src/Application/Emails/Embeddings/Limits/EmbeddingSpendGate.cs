// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;

namespace MailFathom.Application.Emails.Embeddings.Limits;

/// <summary>Answers whether embedding may spend right now, and records what it did spend.</summary>
/// <remarks>
/// <para>
/// One type owns both halves because they are one decision: what a period admits is decided by what the same period has
/// been charged, and splitting the question from the answer would let a reader consult one clock and a writer another.
/// </para>
/// <para>
/// The reading is deliberately cheap and unconditional rather than cached. A period's total is one indexed row per
/// owner, the read happens beside a network call that costs orders of magnitude more, and a cached figure would be
/// wrong exactly when it matters — after a restart, or while a second worker is spending against the same period.
/// </para>
/// <para>
/// Two readings exist because two callers ask different questions. Work embedding somebody's mail asks where that
/// owner stands against both ceilings; an administrative surface acts for nobody's mail and asks where the deployment
/// stands, which is the only question a caller with no owner can be answered.
/// </para>
/// </remarks>
public sealed class EmbeddingSpendGate
{
    private readonly IEmbeddingSpendLedger ledger;
    private readonly EmbeddingSpendBudget budget;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes a new gate over one deployment's budget.</summary>
    /// <param name="ledger">Keeps the durable count of what each period has spent.</param>
    /// <param name="budget">The ceilings and the period they are counted over.</param>
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

    /// <summary>Reads where one owner stands in the current period, which is what work consults before it spends.</summary>
    /// <param name="owner">The owner whose mail is about to be embedded.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The period, what the owner and the deployment have consumed, and what each still admits.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    public async Task<EmbeddingSpendAdmission> ReadCurrentPeriodForAsync(
        MailOwnerId owner,
        CancellationToken cancellationToken)
    {
        if (!owner.IsSpecified)
        {
            throw new ArgumentException("Embedding spend is charged to a named owner.", nameof(owner));
        }

        var periodStart = this.CurrentPeriodStart();
        var consumed = await this.ledger.ReadConsumedInputCharactersAsync(periodStart, owner, cancellationToken);

        return new EmbeddingSpendAdmission(
            this.PeriodOf(periodStart, consumed.OwnerConsumedInputCharacterCount, this.budget.MaxInputCharactersPerPeriodPerOwner),
            this.PeriodOf(periodStart, consumed.DeploymentConsumedInputCharacterCount, this.budget.MaxInputCharactersPerPeriod));
    }

    /// <summary>Reads where the deployment stands in the current period, whatever any one owner has spent of it.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The period, its consumption across every owner, and what it still admits.</returns>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    /// <remarks>
    /// This is the reading an activation weighs its estimate against, so it answers for a deployment with no ceiling
    /// too: the period is still named and still counted, and only the ceiling is absent.
    /// </remarks>
    public async Task<EmbeddingSpendPeriod> ReadCurrentPeriodAsync(CancellationToken cancellationToken)
    {
        var periodStart = this.CurrentPeriodStart();
        var consumed = await this.ledger.ReadDeploymentConsumedInputCharactersAsync(periodStart, cancellationToken);

        return this.PeriodOf(periodStart, consumed, this.budget.MaxInputCharactersPerPeriod);
    }

    /// <summary>Charges one provider call to the period it happened in and the owner it was made for.</summary>
    /// <param name="session">The session committing the vectors that call produced.</param>
    /// <param name="owner">The owner whose mail the call was embedding.</param>
    /// <param name="inputCharacterCount">The characters the call sent.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>A task that completes when the charge has been issued inside the caller's transaction.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the count is negative.</exception>
    /// <remarks>
    /// A deployment with no ceiling is charged exactly as one with a ceiling is. The count is what an operator watches
    /// to decide whether to declare a ceiling at all, so leaving it unwritten would make the figure appear only once it
    /// was already too late to be useful.
    /// </remarks>
    public Task RecordSpendAsync(
        IPersistenceSession session,
        MailOwnerId owner,
        long inputCharacterCount,
        CancellationToken cancellationToken)
    {
        if (!owner.IsSpecified)
        {
            throw new ArgumentException("Embedding spend is charged to a named owner.", nameof(owner));
        }

        return this.ledger.RecordSpendAsync(
            session,
            this.CurrentPeriodStart(),
            owner,
            inputCharacterCount,
            cancellationToken);
    }

    private DateTimeOffset CurrentPeriodStart() => this.budget.PeriodStartAt(this.timeProvider.GetUtcNow());

    private EmbeddingSpendPeriod PeriodOf(DateTimeOffset periodStart, long consumed, long ceiling) =>
        new(periodStart, periodStart + this.budget.Period, consumed, ceiling == 0 ? null : ceiling);
}
