// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Persistence;
using MailFathom.Application.Rules.History;
using MailFathom.Domain.Access;
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
/// <para>
/// The one place the two kinds of request differ is what they do to a run they find. A schedule finding any run
/// outstanding stands down, because the walk it wanted is under way. An operator finding a scheduled run outstanding
/// replaces it, because what they asked for reaches every rule the account has while the scheduled walk reaches only
/// the rules that opted into a schedule — answering the wider request with the narrower run would tell somebody their
/// whole rule set had been applied when part of it never was. The replacement walks the mailbox from the beginning and
/// reaches everything the scheduled run had reached, and the schedule's next occasion starts a run of its own.
/// </para>
/// </remarks>
public sealed class MailRuleEvaluationRunRequests
{
    private readonly IMailRuleEvaluationRunStore runStore;
    private readonly OptimisticConcurrencyRetryPolicy commitPolicy;
    private readonly TimeProvider timeProvider;
    private readonly AccessAuthorization authorization;

    /// <summary>Initializes the request intake.</summary>
    /// <param name="runStore">Reads whether a run is outstanding and records the one this request asks for.</param>
    /// <param name="commitPolicy">Makes the read and the write one decision, and resolves a race with a competing request.</param>
    /// <param name="timeProvider">Stamps the request.</param>
    /// <param name="authorization">Answers which principal reached this use case.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    public MailRuleEvaluationRunRequests(
        IMailRuleEvaluationRunStore runStore,
        OptimisticConcurrencyRetryPolicy commitPolicy,
        TimeProvider timeProvider,
        AccessAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(runStore);
        ArgumentNullException.ThrowIfNull(commitPolicy);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(authorization);

        this.runStore = runStore;
        this.commitPolicy = commitPolicy;
        this.timeProvider = timeProvider;
        this.authorization = authorization;
    }

    /// <summary>Asks for the account's rules to be run over every message stored for it.</summary>
    /// <param name="accountId">The account to run the rules over.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The run the account now has outstanding, and whether this request is what put it there.</returns>
    /// <exception cref="PersistenceConcurrencyConflictException">Thrown when two requests raced past the bounded retries.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the use case was reached by anything but a caller granted <see cref="MailFathomPermission.AdminOperate" />.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    /// <remarks>
    /// Whether the account is free is decided at the write rather than by a check in front of it, because two requests
    /// arriving together must resolve to one run. The loser of that race meets the account's own key, is retried from a
    /// fresh read, and is answered with the run the winner asked for.
    /// <para>
    /// A scheduled run in front of the account is replaced rather than answered with, because this request reaches every
    /// rule and that one reaches only the rules declaring a schedule.
    /// </para>
    /// <para>
    /// This is the operator's own request, so it asks for the grant that covers making the deployment do work — a pass
    /// over a whole mailbox changes mail on the server. <see cref="SubmitScheduledAsync" /> asks for nothing, because
    /// what reaches it is this process on a rule's own declared occasion rather than a caller.
    /// </para>
    /// </remarks>
    public Task<MailRuleEvaluationRunRequest> SubmitAsync(
        MailAccountId accountId,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminOperate);

        return this.commitPolicy.CommitAsync(
            async (session, attemptCancellationToken) =>
            {
                var requested = new MailRuleEvaluationRun
                {
                    AccountId = accountId,
                    RequestedAt = this.timeProvider.GetUtcNow(),
                    Trigger = MailRuleExecutionTrigger.RequestedRun,
                };

                return await this.StartAsync(session, requested, attemptCancellationToken);
            },
            cancellationToken);
    }

    /// <summary>Asks, on a rule's own declared occasion, for the account's scheduled rules to be run over its mailbox.</summary>
    /// <param name="accountId">The account to run the scheduled rules over.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The run the account now has outstanding, and whether this occasion is what put it there.</returns>
    /// <exception cref="PersistenceConcurrencyConflictException">Thrown when two requests raced past the bounded retries.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    /// <remarks>
    /// Any run already outstanding is the answer, whatever started it. A run somebody asked for reaches every rule this
    /// occasion wanted and more, and a scheduled run is the same walk arriving early, so both make a second walk of one
    /// mailbox work nobody needs — which is the guarantee the mechanism dispatching this occasion also makes about the
    /// job it enqueued.
    /// <para>
    /// It asks for no permission, deliberately. What reaches it is a job this deployment enqueued from a rule's own
    /// declared schedule, so there is no caller to hold one, and requiring an administrative grant here would mean the
    /// schedule ran under a credential nobody presented.
    /// </para>
    /// </remarks>
    public Task<MailRuleEvaluationRunRequest> SubmitScheduledAsync(
        MailAccountId accountId,
        CancellationToken cancellationToken) =>
        this.commitPolicy.CommitAsync(
            async (session, attemptCancellationToken) =>
            {
                var scheduled = new MailRuleEvaluationRun
                {
                    AccountId = accountId,
                    RequestedAt = this.timeProvider.GetUtcNow(),
                    Trigger = MailRuleExecutionTrigger.ScheduledRun,
                };

                return await this.StartAsync(session, scheduled, attemptCancellationToken);
            },
            cancellationToken);

    /// <summary>Starts the run unless the account's row already holds one this request must not replace.</summary>
    /// <param name="session">The session the write is staged in.</param>
    /// <param name="starting">The run this request wants to start.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The run the account now has, and whether this request is what put it there.</returns>
    /// <remarks>
    /// The precedence between the two triggers is asked at the write rather than at a read before it, because a read
    /// answers about the instant it happened and the row is what the next pass will act on. A request that finds the
    /// account claimed is answered with the claim, which is the same answer it would have given from a read — the
    /// difference is that it cannot now be an answer given about a row that has since changed.
    /// </remarks>
    private async Task<MailRuleEvaluationRunRequest> StartAsync(
        IPersistenceSession session,
        MailRuleEvaluationRun starting,
        CancellationToken cancellationToken)
    {
        var claimed = await this.runStore.TryStartAsync(session, starting, cancellationToken);

        return claimed is null
            ? new MailRuleEvaluationRunRequest(starting, Accepted: true)
            : new MailRuleEvaluationRunRequest(claimed, Accepted: false);
    }
}
