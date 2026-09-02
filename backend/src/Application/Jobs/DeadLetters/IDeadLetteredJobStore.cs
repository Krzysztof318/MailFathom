// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Jobs.DeadLetters;

/// <summary>Reads the jobs that stopped, and carries out the two decisions an operator can take about one.</summary>
/// <remarks>
/// <para>
/// Separate from <see cref="IJobStore" /> because the callers are separate. That contract is the worker's, and every
/// method on it is conditional on the lease the calling attempt holds; nothing here holds a lease, because a dead
/// letter is claimed by nobody. Folding the two together would put an operator's decision behind a lease owner it would
/// have to invent, and would widen the surface a worker sees to include writes no worker may make.
/// </para>
/// <para>
/// Both decisions are conditional on the job still being dead-lettered, which is what makes them safe to repeat and
/// safe to race: two terminals acting on the same job produce one change and one answer of
/// <see cref="JobRecoveryOutcome.JobNotDeadLettered" />, rather than a retry that resurrects work a drop had just
/// finished with.
/// </para>
/// </remarks>
public interface IDeadLetteredJobStore
{
    /// <summary>Serves one bounded page of the jobs nothing will attempt again.</summary>
    /// <param name="query">Which jobs, and how the page is narrowed and continued.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page, and the boundary the next one is asked with where the reading continues.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="query" /> is <see langword="null" />.</exception>
    /// <remarks>A job whose type this build does not declare is left out rather than reported under a name nothing runs, which is what keeps a rolling deployment's older rows from reading as work this instance could act on.</remarks>
    Task<DeadLetteredJobPage> ReadPageAsync(DeadLetteredJobQuery query, CancellationToken cancellationToken);

    /// <summary>Returns one dead letter to the queue, to be run again under the identity it already carries.</summary>
    /// <param name="jobId">The job to attempt again.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What happened to the job.</returns>
    /// <remarks>
    /// <para>
    /// The row is the same row, so the work runs under its original idempotency identity: this repeats the execution
    /// rather than enqueuing a second one, and the promise every handler is registered on — that running it twice with
    /// one payload is the same as running it once — is what makes that safe.
    /// </para>
    /// <para>
    /// The attempt count is given back with the job, because a bound that has already been reached would dead-letter the
    /// work again on its first attempt and make the decision a no-op. The failure that ended it is kept, so the row
    /// still says why it stopped until something newer replaces it.
    /// </para>
    /// </remarks>
    Task<JobRecoveryOutcome> RetryAsync(JobId jobId, CancellationToken cancellationToken);

    /// <summary>Records that one dead letter will never be run, leaving the row and its failure where they are.</summary>
    /// <param name="jobId">The job to drop.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What happened to the job.</returns>
    /// <remarks>
    /// It is a state rather than a deletion, for the reason <see cref="JobState.Dropped" /> gives: the decision is
    /// itself worth keeping, and the row goes on holding the idempotency key that stops the same trigger enqueuing the
    /// same work again.
    /// </remarks>
    Task<JobRecoveryOutcome> DropAsync(JobId jobId, CancellationToken cancellationToken);
}

/// <summary>States what became of a job an operator decided about.</summary>
/// <remarks>
/// One set for both decisions, because the two refusals are the same two: a job this deployment does not hold, and a
/// job that is not in the state either decision applies to. What was asked for is already the method that was called.
/// </remarks>
public enum JobRecoveryOutcome
{
    /// <summary>The job was dead-lettered and the decision was written against it.</summary>
    Accepted = 0,

    /// <summary>No job of this deployment carries the identifier named.</summary>
    JobUnknown = 1,

    /// <summary>The job exists but is not dead-lettered, so nothing was written.</summary>
    /// <remarks>It is the answer to a second terminal acting on a job the first one already retried or dropped, and to an identifier read off a job that is still working.</remarks>
    JobNotDeadLettered = 2,
}
