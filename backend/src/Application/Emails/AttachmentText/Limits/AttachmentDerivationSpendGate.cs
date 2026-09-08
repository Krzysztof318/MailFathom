// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;

namespace MailFathom.Application.Emails.AttachmentText.Limits;

/// <summary>Answers whether one step of attachment reading may consume right now, and records what it did consume.</summary>
/// <remarks>
/// <para>
/// One type owns both halves for the reason <c>EmbeddingSpendGate</c> does: what a period admits is decided by what the
/// same period has been charged, and splitting the question from the answer would let a reader consult one clock and a
/// writer another.
/// </para>
/// <para>
/// Two readings exist because two callers ask different questions. A pass reading somebody's mail asks where that user
/// stands against both ceilings; an administrative surface acts for nobody's mail and asks where the deployment stands,
/// which is the only question a caller with no user can be answered.
/// </para>
/// </remarks>
public sealed class AttachmentDerivationSpendGate
{
    private readonly IAttachmentDerivationSpendLedger ledger;
    private readonly AttachmentDerivationBudget budget;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes a new gate over one deployment's attachment budget.</summary>
    /// <param name="ledger">Keeps the durable count of what each period has consumed.</param>
    /// <param name="budget">The ceilings and the period they are counted over.</param>
    /// <param name="timeProvider">Decides which period the present moment belongs to.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public AttachmentDerivationSpendGate(
        IAttachmentDerivationSpendLedger ledger,
        AttachmentDerivationBudget budget,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.ledger = ledger;
        this.budget = budget;
        this.timeProvider = timeProvider;
    }

    /// <summary>Reads where one user stands on one step in the current period, which is what a pass consults before it reads.</summary>
    /// <param name="derivationStep">The step about to be taken.</param>
    /// <param name="user">The user whose mail is about to be read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The period, what the user and the deployment have consumed, and what each still admits.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    public async Task<AttachmentDerivationAdmission> ReadCurrentPeriodForAsync(
        AttachmentDerivationStep derivationStep,
        MailUserId user,
        CancellationToken cancellationToken)
    {
        if (!user.IsSpecified)
        {
            throw new ArgumentException("Attachment derivation is charged to a named user.", nameof(user));
        }

        var periodStart = this.CurrentPeriodStart();
        var consumed = await this.ledger.ReadConsumedAsync(periodStart, derivationStep, user, cancellationToken);

        return new AttachmentDerivationAdmission(
            this.PeriodOf(derivationStep, periodStart, consumed.UserConsumedUnitCount, this.budget.CeilingFor(derivationStep, forUser: true)),
            this.PeriodOf(derivationStep, periodStart, consumed.DeploymentConsumedUnitCount, this.budget.CeilingFor(derivationStep, forUser: false)));
    }

    /// <summary>Reads where the deployment stands on one step, whatever any one user has consumed of it.</summary>
    /// <param name="derivationStep">The step being reported on.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The period, its consumption across every user, and what it still admits.</returns>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    /// <remarks>
    /// This is the reading an activation weighs its estimate against, so it answers for a deployment with no ceiling
    /// too: the period is still named and still counted, and only the ceiling is absent.
    /// </remarks>
    public async Task<AttachmentDerivationPeriod> ReadCurrentPeriodAsync(
        AttachmentDerivationStep derivationStep,
        CancellationToken cancellationToken)
    {
        var periodStart = this.CurrentPeriodStart();
        var consumed = await this.ledger.ReadDeploymentConsumedAsync(periodStart, derivationStep, cancellationToken);

        return this.PeriodOf(derivationStep, periodStart, consumed, this.budget.CeilingFor(derivationStep, forUser: false));
    }

    /// <summary>Charges what reading one message consumed to the period, step, and user it happened for.</summary>
    /// <param name="session">The session committing the readings that work produced.</param>
    /// <param name="derivationStep">The step the units belong to.</param>
    /// <param name="user">The user whose mail was read.</param>
    /// <param name="unitCount">What the work consumed, in that step's own unit.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>A task that completes when the charge has been issued inside the caller's transaction.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the count is negative.</exception>
    /// <remarks>
    /// A deployment with no ceiling is charged exactly as one with a ceiling is. The count is what an operator watches
    /// to decide whether to declare a ceiling at all, so leaving it unwritten would make the figure appear only once it
    /// was already too late to be useful.
    /// </remarks>
    public Task RecordSpendAsync(
        IPersistenceSession session,
        AttachmentDerivationStep derivationStep,
        MailUserId user,
        long unitCount,
        CancellationToken cancellationToken)
    {
        if (!user.IsSpecified)
        {
            throw new ArgumentException("Attachment derivation is charged to a named user.", nameof(user));
        }

        return this.ledger.RecordSpendAsync(
            session,
            this.CurrentPeriodStart(),
            derivationStep,
            user,
            unitCount,
            cancellationToken);
    }

    private DateTimeOffset CurrentPeriodStart() => this.budget.PeriodStartAt(this.timeProvider.GetUtcNow());

    private AttachmentDerivationPeriod PeriodOf(
        AttachmentDerivationStep derivationStep,
        DateTimeOffset periodStart,
        long consumed,
        long ceiling) =>
        new(derivationStep, periodStart, periodStart + this.budget.Period, consumed, ceiling == 0 ? null : ceiling);
}
