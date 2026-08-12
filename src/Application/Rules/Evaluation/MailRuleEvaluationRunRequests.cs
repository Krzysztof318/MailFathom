// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Rules.Evaluation;

/// <summary>Takes a request to run an account's rules over its whole mailbox, and answers one that is already under way.</summary>
/// <remarks>
/// <para>
/// The request is recorded and nothing is evaluated here. Running the rules is a step of the account's synchronization
/// run, so what this writes is the statement that the run is wanted; the request thread neither performs the work nor
/// keeps it alive, which is what stops an operator's terminal closing from cancelling a walk of their mailbox.
/// </para>
/// <para>
/// A second request while one is outstanding is answered with the run already in front of the account rather than
/// refused or queued. Asking twice for the same thing is asking once: what the caller wanted is for the mail to be
/// re-evaluated, and it is going to be.
/// </para>
/// </remarks>
public sealed class MailRuleEvaluationRunRequests
{
    private readonly IMailRuleEvaluationRunStore runStore;
    private readonly OptimisticConcurrencyRetryPolicy commitPolicy;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the request intake.</summary>
    /// <param name="runStore">Reads whether a run is outstanding and records the one this request asks for.</param>
    /// <param name="commitPolicy">Makes the read and the write one decision, and resolves a race with a competing request.</param>
    /// <param name="timeProvider">Stamps the request.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    public MailRuleEvaluationRunRequests(
        IMailRuleEvaluationRunStore runStore,
        OptimisticConcurrencyRetryPolicy commitPolicy,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(runStore);
        ArgumentNullException.ThrowIfNull(commitPolicy);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.runStore = runStore;
        this.commitPolicy = commitPolicy;
        this.timeProvider = timeProvider;
    }

    /// <summary>Asks for the account's rules to be run over every message stored for it.</summary>
    /// <param name="accountId">The account to run the rules over.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The run the account now has outstanding, and whether this request is what put it there.</returns>
    /// <exception cref="PersistenceConcurrencyConflictException">Thrown when two requests raced past the bounded retries.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    /// <remarks>
    /// The read and the write are one committed decision rather than a check followed by an insert, because two requests
    /// arriving together must resolve to one run. The loser of that race meets the account's own key, is retried from a
    /// fresh read, and is answered with the run the winner asked for.
    /// </remarks>
    public Task<MailRuleEvaluationRunRequest> SubmitAsync(
        MailAccountId accountId,
        CancellationToken cancellationToken) =>
        this.commitPolicy.CommitAsync(
            async (session, attemptCancellationToken) =>
            {
                var outstanding = await this.runStore.FindOutstandingAsync(accountId, attemptCancellationToken);

                if (outstanding is not null)
                {
                    return new MailRuleEvaluationRunRequest(outstanding, Accepted: false);
                }

                var requested = new MailRuleEvaluationRun
                {
                    AccountId = accountId,
                    RequestedAt = this.timeProvider.GetUtcNow(),
                };

                await this.runStore.SaveAsync(session, requested, attemptCancellationToken);

                return new MailRuleEvaluationRunRequest(requested, Accepted: true);
            },
            cancellationToken);
}
