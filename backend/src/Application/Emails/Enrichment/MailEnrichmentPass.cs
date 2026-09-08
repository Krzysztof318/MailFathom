// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Emails.Enrichment;

/// <summary>Derives what the account's newly cut mail is about, once per message, and writes it down.</summary>
/// <remarks>
/// <para>
/// The last stage of the arrival pipeline, behind the cut, and behind it because a mark cites passages: a message
/// derived before it was cut would have nothing to rest its evidence on. Everything the earlier stages settle is
/// settled by the time this runs — the classification gate has admitted the message, the user's rules have finished
/// with it, and it is not still on its way out of the folder it is sitting in.
/// </para>
/// <para>
/// A step of the account's synchronization run rather than a schedule of its own, for the reason every other pass is:
/// that run already has per-account isolation, a slot count, a jittered backoff, and a failure path that defers the
/// account instead of the process, and only one pass per account is ever in flight because of it.
/// </para>
/// <para>
/// It needs no cursor. A message leaves the selection by being derived from, so an interrupted pass repeats nothing and
/// skips nothing, and what one pass's bound leaves behind is the next run's. That is also the whole of how an existing
/// mailbox is backfilled: a deployment that turns enrichment on drains its stored mail over successive runs,
/// <see cref="MaximumEmailsPerPass" /> at a time, and the report says at every step how far it has got and whether more
/// remains. No sweep of its own exists, because there is no mail the account run does not reach — an account that has
/// stopped synchronizing has stopped receiving the mail a derivation would be owed for.
/// </para>
/// <para>
/// Derivations are made one after another and the first withheld one ends the pass. Both follow from what a derivation
/// costs: it is a provider call, the endpoint's concurrency limiter admits a few invocations at a time and rejects the
/// rest outright rather than queueing them, and every reason a derivation is withheld — an operator who has not turned
/// it on, a spent allowance, an unreachable provider — outlives one message. Asking again per remaining message would
/// buy the same answer while the account run waits.
/// </para>
/// </remarks>
public sealed class MailEnrichmentPass
{
    /// <summary>How many messages one pass derives from.</summary>
    /// <remarks>
    /// Small, because a derivation is a provider call rather than a local computation: this is what one account run is
    /// allowed to hold its slot for, and every other account is waiting behind it. What it leaves behind is outstanding
    /// work the next run resumes. It is a constant rather than a setting because it bounds this pass's latency rather
    /// than describing a deployment — what a deployment declares about the spend is the answering period's own
    /// ceilings, which every derivation is admitted against.
    /// </remarks>
    public const int MaximumEmailsPerPass = 8;

    /// <summary>How many of a message's leading passages one derivation reads.</summary>
    /// <remarks>
    /// A derivation answers what a message is about, and the opening of a message is what says so; a long thread's
    /// later passages are quoted history the cut kept because somebody wrote around it. Reading every passage of every
    /// message would multiply the request size by the length of the correspondence for a sentence that would not
    /// change.
    /// </remarks>
    public const int MaximumPassagesPerEmail = 6;

    private readonly IStoredEmailEnrichmentStore enrichmentStore;
    private readonly IEmailEnricher enricher;
    private readonly SensitiveContentEgressGuard egressGuard;
    private readonly OptimisticConcurrencyRetryPolicy commitPolicy;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the pass from the state it walks and the derivation it asks.</summary>
    /// <param name="enrichmentStore">Reads what is awaiting a derivation and writes down what one produced.</param>
    /// <param name="enricher">Derives one message's marks, in whichever state the deployment left it.</param>
    /// <param name="egressGuard">States whose mail the passages are, so the derivation scans them under that user's posture.</param>
    /// <param name="commitPolicy">Commits one message's record, retrying a conflict with a competing writer.</param>
    /// <param name="timeProvider">Reads when a derivation ran.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public MailEnrichmentPass(
        IStoredEmailEnrichmentStore enrichmentStore,
        IEmailEnricher enricher,
        SensitiveContentEgressGuard egressGuard,
        OptimisticConcurrencyRetryPolicy commitPolicy,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(enrichmentStore);
        ArgumentNullException.ThrowIfNull(enricher);
        ArgumentNullException.ThrowIfNull(egressGuard);
        ArgumentNullException.ThrowIfNull(commitPolicy);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.enrichmentStore = enrichmentStore;
        this.enricher = enricher;
        this.egressGuard = egressGuard;
        this.commitPolicy = commitPolicy;
        this.timeProvider = timeProvider;
    }

    /// <summary>Takes one bounded pass over the account's mail awaiting a derivation.</summary>
    /// <param name="account">The account whose mail is derived from.</param>
    /// <param name="cancellationToken">Cancels the pass between messages; committed records stay durable.</param>
    /// <returns>How many messages this pass derived from, what stopped it, and whether more remain.</returns>
    /// <exception cref="PersistenceConcurrencyConflictException">
    /// Thrown when a competing writer wins a race the bounded retries could not resolve. Records already written stay
    /// durable and the next run resumes by asking the same question.
    /// </exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels. Committed records stay durable.</exception>
    public async Task<MailEnrichmentPassReport> RunAsync(
        MailAccountIdentity account,
        CancellationToken cancellationToken)
    {
        // The switch is honoured here rather than at composition, so a deployment that has not turned enrichment on runs
        // a pass that answers in one comparison and issues no query at all — the selection is a scan per account per
        // run, and nothing would act on what it returned.
        if (!this.enricher.IsActive)
        {
            return new MailEnrichmentPassReport(
                DerivedEmailCount: 0,
                MarkedEmailCount: 0,
                StoppedBy: EmailEnrichmentWithholding.NotActivated,
                EmailsRemain: false);
        }

        // Established for the whole pass rather than per message, because whose mail a passage is decides the posture it
        // is scanned under and every message in the batch belongs to the one account this pass walks.
        using var actingFor = this.egressGuard.ActingFor(account.User);

        var batch = await this.enrichmentStore.GetEmailsAwaitingEnrichmentAsync(
            account,
            MaximumEmailsPerPass,
            MaximumPassagesPerEmail,
            cancellationToken);

        var derivedCount = 0;
        var markedCount = 0;

        foreach (var email in batch)
        {
            var derivation = await this.enricher.DeriveAsync(email, cancellationToken);

            if (derivation.Withheld is { } withholding)
            {
                return new MailEnrichmentPassReport(
                    derivedCount,
                    markedCount,
                    withholding,
                    EmailsRemain: true);
            }

            var enrichment = new EmailEnrichment(
                email.StoredEmailId,
                derivation.Marks,
                this.timeProvider.GetUtcNow());

            // Committed one message at a time rather than one batch at a time, because a derivation that is settled has
            // already been paid for: holding it until the last of the batch had answered would lose every one of them
            // to a provider that stopped answering half way through.
            await this.commitPolicy.CommitAsync(
                (session, attemptCancellationToken) => this.enrichmentStore.SaveAsync(
                    session,
                    enrichment,
                    attemptCancellationToken),
                cancellationToken);

            derivedCount++;

            if (derivation.Marks.Count > 0)
            {
                markedCount++;
            }
        }

        return new MailEnrichmentPassReport(
            derivedCount,
            markedCount,
            StoppedBy: null,
            EmailsRemain: batch.Count == MaximumEmailsPerPass);
    }
}
