// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Spam.Runs;

/// <summary>Takes a request to classify an account's whole mailbox, and answers one that is already under way.</summary>
/// <remarks>
/// <para>
/// The request is recorded and nothing is classified here. Carrying the run is a step of the account's synchronization
/// run, so what this writes is the statement that the run is wanted; the request thread neither performs the work nor
/// keeps it alive, which is what stops an operator's terminal closing from cancelling a walk of their mailbox and what
/// makes the answer immediate however large that mailbox is.
/// </para>
/// <para>
/// A second request while one is outstanding is answered with the run already in front of the account rather than
/// refused or queued. Asking twice for the same thing is asking once: what the caller wanted is for the mail to be
/// classified, and it is going to be.
/// </para>
/// </remarks>
public sealed class SpamClassificationRunRequests
{
    private readonly ISpamClassificationRunStore runStore;
    private readonly OptimisticConcurrencyRetryPolicy commitPolicy;
    private readonly TimeProvider timeProvider;
    private readonly AccessAuthorization authorization;

    /// <summary>Initializes the request intake.</summary>
    /// <param name="runStore">Reads whether a run is outstanding and records the one this request asks for.</param>
    /// <param name="commitPolicy">Makes the read and the write one decision, and resolves a race with a competing request.</param>
    /// <param name="timeProvider">Stamps the request.</param>
    /// <param name="authorization">Answers which principal reached this use case.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    public SpamClassificationRunRequests(
        ISpamClassificationRunStore runStore,
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

    /// <summary>Asks for every message stored for the account to be classified on the terms given.</summary>
    /// <param name="accountId">The account to walk.</param>
    /// <param name="terms">What the run should do.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The run the account now has outstanding, and whether this request is what put it there.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="terms" /> is <see langword="null" />.</exception>
    /// <exception cref="PersistenceConcurrencyConflictException">Thrown when two requests raced past the bounded retries.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the use case was reached by anything but a caller granted <see cref="MailFathomPermission.AdminOperate" />.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    /// <remarks>
    /// The read and the write are one committed decision rather than a check followed by an insert, because two requests
    /// arriving together must resolve to one run. The loser of that race meets the account's own key, is retried from a
    /// fresh read, and is answered with the run the winner asked for.
    /// <para>
    /// A run asked to act moves mail on somebody's server, so the grant it asks for is the one covering work the
    /// deployment performs on request — reading what a run concluded is a different grant and neither implies the other.
    /// </para>
    /// </remarks>
    public Task<SpamClassificationRunRequest> SubmitAsync(
        MailAccountId accountId,
        SpamClassificationRunTerms terms,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(terms);

        this.authorization.RequirePermission(MailFathomPermission.AdminOperate);

        return this.commitPolicy.CommitAsync(
            async (session, attemptCancellationToken) =>
            {
                var outstanding = await this.runStore.FindOutstandingAsync(accountId, attemptCancellationToken);

                if (outstanding is not null)
                {
                    return new SpamClassificationRunRequest(outstanding, Accepted: false);
                }

                var requested = new SpamClassificationRun
                {
                    AccountId = accountId,
                    RequestedAt = this.timeProvider.GetUtcNow(),
                    Terms = terms,
                };

                await this.runStore.SaveAsync(session, requested, attemptCancellationToken);

                return new SpamClassificationRunRequest(requested, Accepted: true);
            },
            cancellationToken);
    }
}
