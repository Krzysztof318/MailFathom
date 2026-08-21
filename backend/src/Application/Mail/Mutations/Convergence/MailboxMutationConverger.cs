// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Mail.Mutations.Audit;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Transport;

namespace MailFathom.Application.Mail.Mutations.Convergence;

/// <summary>Drives one account's unfinished mutations towards the only two endings a change is allowed to have.</summary>
/// <remarks>
/// <para>
/// The durable record makes a remote write resumable; this is what makes it converge. A mutation somebody authored is
/// not a request that succeeded or failed at one moment — it is a state the mailbox is heading for — and the endings
/// are completed, or given up on where a person can see it. Pending forever is the third ending this exists to remove,
/// because it looks exactly like success from every screen an operator reads.
/// </para>
/// <para>
/// A pass is deliberately not a retry loop. It takes each outstanding record in hand once and either moves it or leaves
/// it, and everything about *when* to come back belongs to the account's synchronization schedule: an unreachable
/// server fails the run, the run's jittered backoff defers the next one, and the mutation's own attempt bound decides
/// when a change that keeps failing stops being attempted. Adding a wait here would put a second retry policy around
/// the same commands, which is the storm both the resilience pipeline and that backoff are shaped to avoid.
/// </para>
/// <para>
/// Resuming is one call: the request is read back off the record and handed to the performer, which finds the record by
/// its idempotency identity, counts the attempt, and continues the protocol sequence from the stage the record names.
/// Nothing here knows what a relocation is made of, and nothing here decides which command to skip.
/// </para>
/// <para>
/// The one case that is not resumed is a placement whose answer never came back. Reissuing it would put a second
/// message in the destination folder and nothing there afterwards would say which one MailFathom made, so it is settled
/// from what the mailbox has since shown about the source occurrence, or given up on when it stays unsettled for the
/// configured grace period.
/// </para>
/// </remarks>
public sealed class MailboxMutationConverger
{
    private readonly IMailboxMutationRecordStore store;
    private readonly IMailboxMutationPerformer performer;
    private readonly IMailTransportSecurityPolicyReader transportSecurityPolicyReader;
    private readonly OptimisticConcurrencyRetryPolicy commitPolicy;
    private readonly IMailboxMutationAuditTrail auditTrail;
    private readonly MailboxConvergenceOptions options;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the converger from the record store and the one path able to perform a change.</summary>
    /// <param name="store">Reads the outstanding records and, through the journal, is written back to.</param>
    /// <param name="performer">Resumes a mutation from the stage its record names.</param>
    /// <param name="transportSecurityPolicyReader">Supplies the connection and authentication policy every attempt obeys.</param>
    /// <param name="commitPolicy">Commits the record movements this class makes without the performer.</param>
    /// <param name="auditTrail">Keeps the history a finished mutation leaves behind, where the account asked for one.</param>
    /// <param name="options">Bounds one pass and carries the unknown-outcome grace period.</param>
    /// <param name="timeProvider">Measures how long an unresolved outcome has been unresolved.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required collaborator is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the configured pass bound is below one or the grace period is negative.</exception>
    public MailboxMutationConverger(
        IMailboxMutationRecordStore store,
        IMailboxMutationPerformer performer,
        IMailTransportSecurityPolicyReader transportSecurityPolicyReader,
        OptimisticConcurrencyRetryPolicy commitPolicy,
        IMailboxMutationAuditTrail auditTrail,
        MailboxConvergenceOptions options,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(performer);
        ArgumentNullException.ThrowIfNull(transportSecurityPolicyReader);
        ArgumentNullException.ThrowIfNull(commitPolicy);
        ArgumentNullException.ThrowIfNull(auditTrail);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxMutationsPerPass, 1, nameof(options));
        ArgumentOutOfRangeException.ThrowIfLessThan(options.UnknownOutcomeGrace, TimeSpan.Zero, nameof(options));

        this.store = store;
        this.performer = performer;
        this.transportSecurityPolicyReader = transportSecurityPolicyReader;
        this.commitPolicy = commitPolicy;
        this.auditTrail = auditTrail;
        this.options = options;
        this.timeProvider = timeProvider;
    }

    /// <summary>Takes one bounded pass over everything the account has asked a mail server for and not seen finished.</summary>
    /// <param name="accountId">The account whose mutations are converged.</param>
    /// <param name="cancellationToken">Cancels the pass; a mutation already under way is cancelled with it.</param>
    /// <returns>What the pass did, and what the account still owes afterwards.</returns>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels the pass.</exception>
    /// <remarks>
    /// One record's failure never ends the pass. A mail server that refuses one change is usually refusing every change,
    /// so the remaining records fail quickly beside it and the account's next run is what defers them; a change that is
    /// broken on its own must not stop the ones that are not, which is the whole of what "does not block unrelated work"
    /// means here.
    /// </remarks>
    public async Task<MailboxConvergenceReport> ConvergeAsync(
        MailAccountId accountId,
        CancellationToken cancellationToken)
    {
        var outstanding = await this.store.ReadOutstandingAsync(
            accountId,
            this.options.MaxMutationsPerPass,
            cancellationToken);

        if (outstanding.Count == 0)
        {
            // Nothing outstanding is nothing to count: the lifecycle read answers over the same rows, so an account with
            // no unfinished mutation — which is nearly every account on nearly every run — costs one query rather than two.
            return new MailboxConvergenceReport(0, 0, 0, 0, []);
        }

        var transportSecurityPolicy = this.transportSecurityPolicyReader.GetPolicy(accountId);
        var tally = new ConvergenceTally();

        foreach (var candidate in outstanding)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // An abandoned record is in this answer so it stays visible, not so it is worked on again. It is counted
            // where an operator reads it, in the lifecycle totals below.
            if (candidate.Record.IsTerminal)
            {
                continue;
            }

            await this.ConvergeOneAsync(candidate, transportSecurityPolicy, tally, cancellationToken);
        }

        var outstandingCounts = await this.store.ReadLifecycleCountsAsync(accountId, cancellationToken);

        return new MailboxConvergenceReport(
            tally.CompletedCount,
            tally.DeadLetteredCount,
            tally.DeferredCount,
            tally.FailedCount,
            outstandingCounts);
    }

    /// <summary>Moves one record, and absorbs whatever moving it failed on.</summary>
    /// <remarks>
    /// The isolation is around both paths rather than around the mail server alone, because a record read at the start
    /// of the pass can be moved by a concurrent run before this one acts on it — a stage that will not go backwards is
    /// refused where it is written, which is the point of that rule, and a refusal there must cost this account its
    /// remaining mutations no more than a refusal from a mail server does.
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A pass isolates one mutation's failure so the account's remaining mutations still converge; the record already carries how far it got and the account's run backoff defers the next attempt.")]
    private async Task ConvergeOneAsync(
        OutstandingMailboxMutation candidate,
        MailTransportSecurityPolicy transportSecurityPolicy,
        ConvergenceTally tally,
        CancellationToken cancellationToken)
    {
        try
        {
            if (candidate.Record.HasUnknownOutcome)
            {
                await this.SettleUnknownOutcomeAsync(candidate, tally, cancellationToken);

                return;
            }

            var outcome = await this.performer.PerformAsync(
                candidate.Record.Request,
                candidate.Folder,
                transportSecurityPolicy,
                cancellationToken);

            tally.Count(outcome.Status);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MailboxMutationRefusedException)
        {
            // The server answered, the performer has already moved the record to its terminal stage, and no later
            // attempt will be made. Counting it as failed would say three untrue things at once: the operator's log
            // line would promise another attempt, the pass's own report would describe a record its attempt bound is
            // still going to settle, and the account would back off from a healthy server over a decision somebody has
            // to change a folder or a configuration to alter.
            tally.DeadLetteredCount++;
        }
        catch (Exception)
        {
            // The performer has already written the failure onto the record, and abandoned it where the last attempt
            // was spent, so nothing is lost by swallowing it here. What the count is for is the caller: a pass that
            // failed is a run that failed, which is what puts the account into backoff.
            tally.FailedCount++;
        }
    }

    /// <summary>Settles a placement whose answer never came back, from the mailbox rather than by asking again.</summary>
    /// <remarks>
    /// <para>
    /// The three mutations differ here, and the difference is why this is bounded work rather than a retry. A relocation
    /// carried by <c>MOVE</c> removes the source as part of the same command, so a source that has since been seen to
    /// leave its folder is the server's own statement that the command ran, against an occurrence the record names
    /// exactly. A copy and a fallback relocation both leave the source in place, so nothing about it distinguishes a
    /// command that landed from one that never arrived, and the only way to tell would be to search the destination
    /// folder for a message that looks like the right one — a guess about identity, which
    /// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0007-remote-mailbox-mutation-boundary-and-write-session.md">ADR 0007</see>
    /// refuses.
    /// </para>
    /// <para>
    /// What is left for those is time. The record waits for the grace period in case a later run settles it, and is then
    /// given up on so it stands as dead-lettered rather than as a change that is still apparently happening. Giving up
    /// removes nothing and duplicates nothing: it stops MailFathom claiming to know an outcome it does not.
    /// </para>
    /// </remarks>
    private async Task SettleUnknownOutcomeAsync(
        OutstandingMailboxMutation candidate,
        ConvergenceTally tally,
        CancellationToken cancellationToken)
    {
        var record = candidate.Record;
        var journal = new MailboxMutationJournal(
            this.store,
            this.commitPolicy,
            this.auditTrail,
            record,
            candidate.Folder);

        if (record.IsUnknownPlacementSettledBySourceRemoval)
        {
            await journal.CompleteAsync(record.Placement, cancellationToken);
            tally.CompletedCount++;

            return;
        }

        if (this.timeProvider.GetUtcNow() - record.StageChangedAt < this.options.UnknownOutcomeGrace)
        {
            tally.DeferredCount++;

            return;
        }

        await journal.AbandonAsync(MailFathomErrorCode.MailboxMutationOutcomeUnknown, cancellationToken);
        tally.DeadLetteredCount++;
    }

    /// <summary>Accumulates what one pass did, so the counting is not spread across four call sites.</summary>
    private sealed class ConvergenceTally
    {
        internal int CompletedCount { get; set; }

        internal int DeadLetteredCount { get; set; }

        internal int DeferredCount { get; set; }

        internal int FailedCount { get; set; }

        internal void Count(MailboxMutationStatus status)
        {
            switch (status)
            {
                case MailboxMutationStatus.Performed:
                case MailboxMutationStatus.AlreadyPerformed:
                    this.CompletedCount++;
                    break;
                case MailboxMutationStatus.Abandoned:
                    this.DeadLetteredCount++;
                    break;

                // A record that reached the performer at an unacknowledged placement was read before this pass settled
                // it, which a concurrent writer can produce. It is left exactly where the performer left it.
                case MailboxMutationStatus.OutcomeUnknown:
                default:
                    this.DeferredCount++;
                    break;
            }
        }
    }
}
