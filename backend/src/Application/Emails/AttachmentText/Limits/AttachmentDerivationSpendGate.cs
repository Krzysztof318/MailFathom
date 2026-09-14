// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;

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
/// <para>
/// A mailbox is the third question, and it is the one a pass actually asks. Mail belongs to the account, so a walk
/// holds an account and never a person, while the per-user ceiling still has to mean something: ADR 0014 counts a
/// shared mailbox in full against every user assigned to it, so reading it proceeds only while every one of them is
/// under their ceiling, and what it consumed is charged to each. That fan-out lives here rather than in the pass, so
/// the reading and the charge cannot come to disagree about who a mailbox is for.
/// </para>
/// </remarks>
public sealed class AttachmentDerivationSpendGate
{
    private readonly IAttachmentDerivationSpendLedger ledger;
    private readonly IMailAccountAssignments assignments;
    private readonly AttachmentDerivationBudget budget;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes a new gate over one deployment's attachment budget.</summary>
    /// <param name="ledger">Keeps the durable count of what each period has consumed.</param>
    /// <param name="assignments">Answers who a mailbox is assigned to, which is who its reading is counted against.</param>
    /// <param name="budget">The ceilings and the period they are counted over.</param>
    /// <param name="timeProvider">Decides which period the present moment belongs to.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public AttachmentDerivationSpendGate(
        IAttachmentDerivationSpendLedger ledger,
        IMailAccountAssignments assignments,
        AttachmentDerivationBudget budget,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(assignments);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.ledger = ledger;
        this.assignments = assignments;
        this.budget = budget;
        this.timeProvider = timeProvider;
    }

    /// <summary>Reads where one mailbox stands on one step, across every user it is assigned to.</summary>
    /// <param name="derivationStep">The step about to be taken.</param>
    /// <param name="account">The mailbox whose mail is about to be read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The period, and the strictest standing among the mailbox's users beside the deployment's own.</returns>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    /// <remarks>
    /// The strictest assigned user decides, because a shared mailbox's mail counts in full against each of them: a
    /// reading proceeds only while every one is under their ceiling. A mailbox assigned to nobody leaves only the
    /// deployment's own figure, which is what the empty case answers with.
    /// <para>
    /// ponytail: one indexed read per assigned user, beside a parse and a provider call that cost orders of magnitude
    /// more. A grouped read is worth writing only if a deployment assigns a mailbox widely enough for it to show.
    /// </para>
    /// </remarks>
    public async Task<AttachmentDerivationAdmission> ReadCurrentPeriodForAccountAsync(
        AttachmentDerivationStep derivationStep,
        MailAccountId account,
        CancellationToken cancellationToken)
    {
        var periodStart = this.CurrentPeriodStart();
        var userCeiling = this.budget.CeilingFor(derivationStep, forUser: true);
        var deploymentCeiling = this.budget.CeilingFor(derivationStep, forUser: false);

        AttachmentDerivationPeriod? strictestUser = null;
        AttachmentDerivationPeriod? deployment = null;

        foreach (var user in this.assignments.UsersAssignedTo(account))
        {
            var consumed = await this.ledger.ReadConsumedAsync(periodStart, derivationStep, user, cancellationToken);

            var standing = this.PeriodOf(derivationStep, periodStart, consumed.UserConsumedUnitCount, userCeiling);

            deployment ??= this.PeriodOf(
                derivationStep,
                periodStart,
                consumed.DeploymentConsumedUnitCount,
                deploymentCeiling);

            if (strictestUser is null || (standing.IsExhausted && !strictestUser.IsExhausted))
            {
                strictestUser = standing;
            }
        }

        if (deployment is null)
        {
            var consumedEverywhere = await this.ledger.ReadDeploymentConsumedAsync(
                periodStart,
                derivationStep,
                cancellationToken);

            deployment = this.PeriodOf(derivationStep, periodStart, consumedEverywhere, deploymentCeiling);
        }

        return new AttachmentDerivationAdmission(
            strictestUser ?? this.PeriodOf(derivationStep, periodStart, 0, userCeiling),
            deployment);
    }

    /// <summary>Charges what reading one mailbox's message consumed to every user that mailbox is assigned to.</summary>
    /// <param name="session">The session committing the readings that work produced.</param>
    /// <param name="derivationStep">The step the units belong to.</param>
    /// <param name="account">The mailbox whose mail was read.</param>
    /// <param name="unitCount">What the work consumed, in that step's own unit.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>A task that completes when every charge has been issued inside the caller's transaction.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the count is negative.</exception>
    /// <remarks>
    /// Charged to whoever is assigned at the moment the reading happens, and never recharged when an assignment
    /// changes: consumption is a record of an event rather than a running apportionment.
    /// </remarks>
    public async Task RecordAccountSpendAsync(
        IPersistenceSession session,
        AttachmentDerivationStep derivationStep,
        MailAccountId account,
        long unitCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentOutOfRangeException.ThrowIfNegative(unitCount);

        var periodStart = this.CurrentPeriodStart();

        foreach (var user in this.assignments.UsersAssignedTo(account))
        {
            await this.ledger.RecordSpendAsync(
                session,
                periodStart,
                derivationStep,
                user,
                unitCount,
                cancellationToken);
        }
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
