// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Jobs;

/// <summary>Keeps durable background work, and hands each job to one worker at a time.</summary>
/// <remarks>
/// <para>
/// Two of the guarantees here are the schema's rather than this contract's. Enqueuing is idempotent because the job
/// type and the key are unique together in the database, so two callers asking for the same execution at the same
/// moment both reach it and one of them loses there; a check followed by a write would leave the window between the two
/// statements open, which is the window everything above depends on being closed. And a claim is exclusive because one
/// statement selects and stamps the row under <c>FOR UPDATE SKIP LOCKED</c>, so two workers claiming at once take
/// different jobs rather than waiting on each other.
/// </para>
/// <para>
/// <strong>No method here takes a persistence session</strong>, and that is the contract rather than an omission. A job
/// is enqueued against state that is already committed — never inside the synchronization transaction that stored the
/// message — so there is no session for a caller to join it to and no way to enqueue work whose subject may still roll
/// back.
/// </para>
/// <para>
/// What this delivers is at-least-once execution and nothing stronger. Uniqueness stops the same work being
/// <em>enqueued</em> twice; only a handler can stop a re-run after a crash from having a second effect, so a handler is
/// registered on the promise that running it twice with one payload is the same as running it once.
/// </para>
/// </remarks>
public interface IJobStore
{
    /// <summary>Writes the execution down, or reports the job that already carries this type and key.</summary>
    /// <param name="request">The execution to enqueue.</param>
    /// <param name="cancellationToken">Cancels the write or the read that follows a losing insert.</param>
    /// <returns>The job for this request, and whether this call is the one that created it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request" /> is <see langword="null" />.</exception>
    /// <exception cref="JobPayloadTooLargeException">Thrown when the serialized payload exceeds the bound the enqueue boundary applies.</exception>
    /// <remarks>The job starts claimable, with no attempt counted and no lease, so enqueuing runs nothing by itself.</remarks>
    Task<JobEnqueueResult> EnqueueAsync(JobEnqueueRequest request, CancellationToken cancellationToken);

    /// <summary>Takes up to a batch of due jobs this process can run, and leases each of them to one attempt.</summary>
    /// <param name="request">Which types this process runs, how many to take, and under what lease.</param>
    /// <param name="cancellationToken">Cancels the claim.</param>
    /// <returns>The jobs this call took, oldest first, and an empty answer when none was due.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// A job is due when it is pending and its available instant has passed, or when it is claimed under a lease that
    /// has expired. The second is what makes a crash recoverable: work in flight when a process died is taken by the
    /// next claim without an operator doing anything.
    /// </para>
    /// <para>
    /// Ordering is best effort. The statement hands a worker the next <em>available</em> row, so work that must happen
    /// in a fixed order relative to other work cannot express that through this queue.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<LeasedJob>> ClaimAsync(JobClaimRequest request, CancellationToken cancellationToken);

    /// <summary>Pushes a held job's lease further out, so a long execution is not reclaimed underneath it.</summary>
    /// <param name="jobId">The job whose lease is renewed.</param>
    /// <param name="owner">The attempt claiming to hold it.</param>
    /// <param name="leaseDuration">How much longer the job is held from now.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The renewed lease, or <see langword="null" /> when this attempt no longer holds the job.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="owner" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="leaseDuration" /> is not positive.</exception>
    /// <remarks>An absent answer is the signal to stop working: the lease expired and another attempt has the job, so anything this one goes on to produce would be a second execution's result.</remarks>
    Task<JobLease?> RenewLeaseAsync(
        JobId jobId,
        JobLeaseOwner owner,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    /// <summary>Ends a held job as done, leaving a terminal row that keeps its key.</summary>
    /// <param name="jobId">The job to complete.</param>
    /// <param name="owner">The attempt claiming to hold it.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true" /> when this attempt still held the job and the completion was written; otherwise <see langword="false" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="owner" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Writing nothing is the point of the compare-and-set. Without it, an attempt that lost its lease and finished
    /// late would overwrite the outcome of the attempt that replaced it.
    /// </remarks>
    Task<bool> CompleteAsync(JobId jobId, JobLeaseOwner owner, CancellationToken cancellationToken);

    /// <summary>Gives a held job back unfinished, so it is claimable again at once.</summary>
    /// <param name="jobId">The job to release.</param>
    /// <param name="owner">The attempt claiming to hold it.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true" /> when this attempt still held the job and the release was written; otherwise <see langword="false" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="owner" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// It is what a shutdown does with work it was holding, and it is deliberately not a failure: the attempt is spent
    /// and stays counted, but the job returns to the queue immediately rather than waiting out its lease.
    /// </remarks>
    Task<bool> ReleaseAsync(JobId jobId, JobLeaseOwner owner, CancellationToken cancellationToken);
}
