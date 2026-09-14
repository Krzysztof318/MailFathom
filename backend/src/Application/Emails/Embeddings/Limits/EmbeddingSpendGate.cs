// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Emails.Embeddings.Limits;

/// <summary>Answers whether embedding may spend right now, and records what it did spend.</summary>
/// <remarks>
/// <para>
/// One type owns both halves because they are one decision: what a period admits is decided by what the same period has
/// been charged, and splitting the question from the answer would let a reader consult one clock and a writer another.
/// </para>
/// <para>
/// The reading is deliberately cheap and unconditional rather than cached. A period's total is one indexed row per
/// user, the read happens beside a network call that costs orders of magnitude more, and a cached figure would be
/// wrong exactly when it matters — after a restart, or while a second worker is spending against the same period.
/// </para>
/// <para>
/// Two readings exist because two callers ask different questions. Work embedding somebody's mail asks where that
/// user stands against both ceilings; an administrative surface acts for nobody's mail and asks where the deployment
/// stands, which is the only question a caller with no user can be answered.
/// </para>
/// <para>
/// A mailbox is the third question, and it is the one background work actually asks. Mail belongs to the account, so
/// a pass holds an account and never a person, while the per-user ceiling still has to mean something: ADR 0014
/// settles it by counting a shared mailbox in full against every user assigned to it, so work proceeds only while
/// every one of them is under their ceiling, and one call's characters are charged to each. That fan-out lives here
/// rather than in each pass, so the reading and the charge cannot come to disagree about who a mailbox is for.
/// </para>
/// <para>
/// The deployment's figure is not the sum of those per-user charges and is never derived from them. Counting a shared
/// mailbox in full against each of its users is what the per-user ceiling means, so the per-user figures add up to
/// more than was sent, and a deployment ceiling read off their sum would stop a mailbox three people share after a
/// third of what the operator declared. The ledger counts what one call sent once, separately.
/// </para>
/// </remarks>
public sealed class EmbeddingSpendGate
{
    private readonly IEmbeddingSpendLedger ledger;
    private readonly IMailAccountAssignments assignments;
    private readonly EmbeddingSpendBudget budget;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes a new gate over one deployment's budget.</summary>
    /// <param name="ledger">Keeps the durable count of what each period has spent.</param>
    /// <param name="assignments">Answers who a mailbox is assigned to, which is who its spend is counted against.</param>
    /// <param name="budget">The ceilings and the period they are counted over.</param>
    /// <param name="timeProvider">Decides which period the present moment belongs to.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public EmbeddingSpendGate(
        IEmbeddingSpendLedger ledger,
        IMailAccountAssignments assignments,
        EmbeddingSpendBudget budget,
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

    /// <summary>Reads where one mailbox stands in the current period, across every user it is assigned to.</summary>
    /// <param name="account">The mailbox whose mail is about to be embedded.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The period, and the strictest standing among the mailbox's users beside the deployment's own.</returns>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    /// <remarks>
    /// The strictest assigned user decides, because a shared mailbox's mail counts in full against each of them: work
    /// on it proceeds only while every one is under their ceiling, and a refusal therefore names the first that is
    /// not. A mailbox assigned to nobody is refused by the same rule read the other way: there is no user for the
    /// per-user ceiling to admit, so the answer is an exhausted per-user period rather than a fresh one. Admitting it
    /// instead would embed mail no caller can read — every caller-facing scope narrows to the accounts somebody is
    /// assigned — and would do so under no per-user ceiling at all, for as long as the mailbox stayed unassigned.
    /// <para>
    /// ponytail: one indexed read per assigned user. A mailbox is shared by a handful of people and the read sits
    /// beside a provider call costing orders of magnitude more, so a grouped read is worth writing only if a
    /// deployment ever assigns a mailbox widely enough for the count to show.
    /// </para>
    /// </remarks>
    public async Task<EmbeddingSpendAdmission> ReadCurrentPeriodForAccountAsync(
        MailAccountId account,
        CancellationToken cancellationToken)
    {
        var periodStart = this.CurrentPeriodStart();
        var assignedUsers = this.assignments.UsersAssignedTo(account);

        EmbeddingSpendPeriod? strictestUser = null;
        EmbeddingSpendPeriod? deployment = null;

        foreach (var user in assignedUsers)
        {
            var consumed = await this.ledger.ReadConsumedInputCharactersAsync(periodStart, user, cancellationToken);

            var standing = this.PeriodOf(
                periodStart,
                consumed.UserConsumedInputCharacterCount,
                this.budget.MaxInputCharactersPerPeriodPerUser);

            deployment ??= this.PeriodOf(
                periodStart,
                consumed.DeploymentConsumedInputCharacterCount,
                this.budget.MaxInputCharactersPerPeriod);

            if (strictestUser is null || IsStricter(standing, strictestUser))
            {
                strictestUser = standing;
            }
        }

        if (deployment is null)
        {
            var consumedEverywhere = await this.ledger.ReadDeploymentConsumedInputCharactersAsync(
                periodStart,
                cancellationToken);

            deployment = this.PeriodOf(periodStart, consumedEverywhere, this.budget.MaxInputCharactersPerPeriod);
        }

        return new EmbeddingSpendAdmission(strictestUser ?? this.ExhaustedPeriod(periodStart), deployment);
    }

    /// <summary>Charges one provider call to the period it happened in and to every user the mailbox is assigned to.</summary>
    /// <param name="session">The session committing the vectors that call produced.</param>
    /// <param name="account">The mailbox whose mail the call was embedding.</param>
    /// <param name="inputCharacterCount">The characters the call sent.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>A task that completes when every charge has been issued inside the caller's transaction.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the count is negative.</exception>
    /// <remarks>
    /// Charged to whoever is assigned at the moment the call is made, and never recharged when an assignment changes:
    /// spend is a record of an event rather than a running apportionment. The deployment's own figure is charged once
    /// by the same write, so a mailbox assigned to nobody still moves it and a mailbox three people share moves it by
    /// what was sent rather than by three times it.
    /// </remarks>
    public Task RecordAccountSpendAsync(
        IPersistenceSession session,
        MailAccountId account,
        long inputCharacterCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentOutOfRangeException.ThrowIfNegative(inputCharacterCount);

        return this.ledger.RecordSpendAsync(
            session,
            this.CurrentPeriodStart(),
            this.assignments.UsersAssignedTo(account),
            inputCharacterCount,
            cancellationToken);
    }

    /// <summary>Reads where one user stands in the current period, which is what work consults before it spends.</summary>
    /// <param name="user">The user whose mail is about to be embedded.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The period, what the user and the deployment have consumed, and what each still admits.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    public async Task<EmbeddingSpendAdmission> ReadCurrentPeriodForAsync(
        MailUserId user,
        CancellationToken cancellationToken)
    {
        if (!user.IsSpecified)
        {
            throw new ArgumentException("Embedding spend is charged to a named user.", nameof(user));
        }

        var periodStart = this.CurrentPeriodStart();
        var consumed = await this.ledger.ReadConsumedInputCharactersAsync(periodStart, user, cancellationToken);

        return new EmbeddingSpendAdmission(
            this.PeriodOf(periodStart, consumed.UserConsumedInputCharacterCount, this.budget.MaxInputCharactersPerPeriodPerUser),
            this.PeriodOf(periodStart, consumed.DeploymentConsumedInputCharacterCount, this.budget.MaxInputCharactersPerPeriod));
    }

    /// <summary>Reads where the deployment stands in the current period, whatever any one user has spent of it.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The period, its consumption across every user, and what it still admits.</returns>
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

    /// <summary>Charges one provider call to the period it happened in and the user it was made for.</summary>
    /// <param name="session">The session committing the vectors that call produced.</param>
    /// <param name="user">The user whose mail the call was embedding.</param>
    /// <param name="inputCharacterCount">The characters the call sent.</param>
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
        MailUserId user,
        long inputCharacterCount,
        CancellationToken cancellationToken)
    {
        if (!user.IsSpecified)
        {
            throw new ArgumentException("Embedding spend is charged to a named user.", nameof(user));
        }

        return this.ledger.RecordSpendAsync(
            session,
            this.CurrentPeriodStart(),
            [user],
            inputCharacterCount,
            cancellationToken);
    }

    // Exhaustion first, because that is what decides whether the work proceeds at all, and the characters still
    // admitted second, so the standing handed back is the one the mailbox actually has rather than whichever assigned
    // user the scan happened to read first. A period counting against no ceiling admits everything, so it is the
    // loosest there is and never displaces one that counts.
    private static bool IsStricter(EmbeddingSpendPeriod candidate, EmbeddingSpendPeriod standing) =>
        candidate.IsExhausted != standing.IsExhausted
            ? candidate.IsExhausted
            : candidate.RemainingInputCharacterCount is { } remaining
                && (standing.RemainingInputCharacterCount is not { } strictest || remaining < strictest);

    private DateTimeOffset CurrentPeriodStart() => this.budget.PeriodStartAt(this.timeProvider.GetUtcNow());

    private EmbeddingSpendPeriod PeriodOf(DateTimeOffset periodStart, long consumed, long ceiling) =>
        new(periodStart, periodStart + this.budget.Period, consumed, ceiling == 0 ? null : ceiling);

    /// <summary>The per-user standing of a mailbox no user is assigned, which admits nothing whatever is configured.</summary>
    /// <remarks>
    /// A ceiling of nothing rather than the configured one, because the configured one may be absent and an absent
    /// ceiling admits everything. There is no user here for a per-user ceiling to be about, so the honest answer is
    /// that no per-user allowance exists rather than that the deployment declared none.
    /// </remarks>
    private EmbeddingSpendPeriod ExhaustedPeriod(DateTimeOffset periodStart) =>
        new(periodStart, periodStart + this.budget.Period, 0, 0);
}
