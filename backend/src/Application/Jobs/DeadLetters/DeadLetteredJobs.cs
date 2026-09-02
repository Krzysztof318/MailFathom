// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;

namespace MailFathom.Application.Jobs.DeadLetters;

/// <summary>The three things an operator does about background work that stopped: read it, run one again, or drop one.</summary>
/// <remarks>
/// <para>
/// The store keeps the rows and is written by the queue itself; this is what an operator reaches, and it exists so that
/// the grant is asked where the decision is made rather than only at the routes serving it today. Reading what stopped
/// reports the deployment's own state, while returning a job to the queue makes it run again — against somebody's
/// mailbox, under the identity the row already carries — so the two are published under different grants and a
/// credential provisioned to watch a queue cannot act on it.
/// </para>
/// <para>
/// Neither decision performs the work. A retry writes the row back to a state the next worker claims from, and a drop
/// records that nothing will, which is why both answer immediately whatever the job was about.
/// </para>
/// </remarks>
public sealed class DeadLetteredJobs
{
    private readonly IDeadLetteredJobStore deadLetters;
    private readonly AccessAuthorization authorization;

    /// <summary>Initializes the operations over the dead-letter store.</summary>
    /// <param name="deadLetters">Keeps the jobs nothing will attempt again, and performs a decision about one.</param>
    /// <param name="authorization">Answers which principal reached this use case.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public DeadLetteredJobs(IDeadLetteredJobStore deadLetters, AccessAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(deadLetters);
        ArgumentNullException.ThrowIfNull(authorization);

        this.deadLetters = deadLetters;
        this.authorization = authorization;
    }

    /// <summary>Reads one page of the jobs nothing will attempt again.</summary>
    /// <param name="query">The filters and where the page continues from.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page, and the cursor the following one is asked with.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="query" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the use case was reached by anything but a caller granted <see cref="MailFathomPermission.AdminRead" />.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    public Task<DeadLetteredJobPage> ReadPageAsync(
        DeadLetteredJobQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        this.authorization.RequirePermission(MailFathomPermission.AdminRead);

        return this.deadLetters.ReadPageAsync(query, cancellationToken);
    }

    /// <summary>Returns one dead letter to the queue, to be run again under the identity it already carries.</summary>
    /// <param name="jobId">The job to attempt again.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What became of the job, including that it was not one this deployment holds or had already moved on.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the use case was reached by anything but a caller granted <see cref="MailFathomPermission.AdminOperate" />.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    public Task<JobRecoveryOutcome> RetryAsync(JobId jobId, CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminOperate);

        return this.deadLetters.RetryAsync(jobId, cancellationToken);
    }

    /// <summary>Records that one dead letter will never be run.</summary>
    /// <param name="jobId">The job to drop.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What became of the job, including that it was not one this deployment holds or had already moved on.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the use case was reached by anything but a caller granted <see cref="MailFathomPermission.AdminOperate" />.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    /// <remarks>Dropping asks for the same grant retrying does rather than the erasing one, because the row and its failure stay where they are: what it records is a decision, not a removal.</remarks>
    public Task<JobRecoveryOutcome> DropAsync(JobId jobId, CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminOperate);

        return this.deadLetters.DropAsync(jobId, cancellationToken);
    }
}
