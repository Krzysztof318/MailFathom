// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Access;
using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Payloads;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;

namespace MailFathom.Application.Mail.Maintenance;

/// <summary>Takes a request to re-derive a scope's stored mail, and answers one that is already under way.</summary>
/// <remarks>
/// <para>
/// The request is recorded and nothing is re-read here. The walk is carried by durable background work, so what this
/// writes is the statement that the run is wanted together with the job that carries its first segment; the request
/// thread neither performs the work nor keeps it alive, which is what stops an operator's terminal closing from
/// cancelling a walk of their mailbox and what makes the answer immediate however large that mailbox is.
/// </para>
/// <para>
/// A second request while one is outstanding is answered with the run already in front of the scope rather than refused
/// or queued. Asking twice for the same thing is asking once: what the caller wanted is for the mail to be re-read, and
/// it is going to be.
/// </para>
/// <para>
/// The enqueue is made on every request rather than only on the one that started the run, and it is the same key each
/// time. That is what makes asking again the repair for the one state nothing else recovers from: a run whose segment
/// was written down but whose job never reached the queue, because the process stopped between the two writes or the
/// queue was full when it was asked. A job that is there is answered with itself and nothing is duplicated, and one
/// that was dead-lettered still holds its key — which is <c>mfctl jobs retry</c>'s to return rather than this one's to
/// enqueue past, so the answer names it as a run nothing is carrying instead of reporting the enqueue as a success.
/// </para>
/// </remarks>
public sealed class StoredMailRederivationRequests
{
    private readonly IStoredMailRederivationRunStore runStore;
    private readonly IJobStore jobs;
    private readonly OptimisticConcurrencyRetryPolicy commitPolicy;
    private readonly TimeProvider timeProvider;
    private readonly AccessAuthorization authorization;

    /// <summary>Initializes the request intake.</summary>
    /// <param name="runStore">Reads whether a run is outstanding and records the one this request asks for.</param>
    /// <param name="jobs">Enqueues the segment the run is on.</param>
    /// <param name="commitPolicy">Makes the read and the write one decision, and resolves a race with a competing request.</param>
    /// <param name="timeProvider">Stamps the request.</param>
    /// <param name="authorization">Answers which principal reached this use case.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    public StoredMailRederivationRequests(
        IStoredMailRederivationRunStore runStore,
        IJobStore jobs,
        OptimisticConcurrencyRetryPolicy commitPolicy,
        TimeProvider timeProvider,
        AccessAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(runStore);
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(commitPolicy);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(authorization);

        this.runStore = runStore;
        this.jobs = jobs;
        this.commitPolicy = commitPolicy;
        this.timeProvider = timeProvider;
        this.authorization = authorization;
    }

    /// <summary>Composes the identity two enqueues of one segment of one run are compared by.</summary>
    /// <param name="run">The run whose current segment is being enqueued.</param>
    /// <returns>The key that segment's job is enqueued under.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="run" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The folder rather than the whole scope, and the reason is a bound rather than a preference: a key may be 256
    /// characters, while an account identifier and a folder alias may each be 128, so a composition of both plus the run
    /// and the segment can exceed it — and a key that cannot be composed would refuse a request that is perfectly
    /// ordinary. Nothing is lost, because the account is a column of the job's own row: an operator reading a stuck job
    /// sees the account there and the folder here, and the two together are the scope.
    /// </remarks>
    public static JobIdempotencyKey KeyOf(StoredMailRederivationRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        var folder = run.Scope.Folder?.Value ?? "*";

        return JobIdempotencyKey.Create(string.Create(
            CultureInfo.InvariantCulture,
            $"{folder}:{run.RunId.Value:d}:{run.SegmentCount}"));
    }

    /// <summary>Asks for every stored message of the scope to have its metadata re-read from the MIME already held.</summary>
    /// <param name="scope">The account, and the one folder of it, to re-read.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The run the scope now has, whether this request started it, and what is carrying it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scope" /> is <see langword="null" />.</exception>
    /// <exception cref="PersistenceConcurrencyConflictException">Thrown when two requests raced past the bounded retries.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the use case was reached by anything but a caller granted <see cref="MailFathomPermission.AdminOperate" />.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    /// <remarks>
    /// The read and the write are one committed decision rather than a check followed by an insert, because two requests
    /// arriving together must resolve to one run. The loser of that race meets the scope's own key, is retried from a
    /// fresh read, and is answered with the run the winner asked for.
    /// <para>
    /// Asking a deployment to walk a whole mailbox is work it performs on request, which is the grant it asks for.
    /// Reading where the run got to is a different grant, and neither implies the other.
    /// </para>
    /// </remarks>
    public async Task<StoredMailRederivationRequest> SubmitAsync(
        StoredMailScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        this.authorization.RequirePermission(MailFathomPermission.AdminOperate);

        var requested = await this.commitPolicy.CommitAsync(
            async (session, attemptCancellationToken) =>
            {
                var existing = await this.runStore.FindAsync(scope, attemptCancellationToken);

                if (existing is { IsOutstanding: true })
                {
                    return (Run: existing, Accepted: false);
                }

                var requestedAt = this.timeProvider.GetUtcNow();

                var started = new StoredMailRederivationRun
                {
                    RunId = StoredMailRederivationRunId.Create(Guid.CreateVersion7(requestedAt)),
                    Scope = scope,
                    RequestedAt = requestedAt,
                    SegmentCount = 1,
                };

                await this.runStore.SaveAsync(session, started, attemptCancellationToken);

                return (Run: started, Accepted: true);
            },
            cancellationToken);

        var enqueued = await this.jobs.EnqueueAsync(
            JobEnqueueRequest.Create(
                KeyOf(requested.Run),
                RederiveStoredMailJobPayload.For(scope.Account, scope.Folder),
                scope.Account),
            cancellationToken);

        return new StoredMailRederivationRequest(
            requested.Run,
            requested.Accepted,
            await this.CarriageOfAsync(enqueued, cancellationToken));
    }

    /// <summary>Reads what the enqueue leaves carrying the segment, which the outcome alone does not say.</summary>
    /// <remarks>
    /// A job that is already there is answered with itself whatever state it is in, so an enqueue meeting a segment
    /// that dead-lettered reports exactly what one meeting a segment waiting in the queue reports. Reading the state is
    /// what separates them, and the separation is the point: an operator told the work is queued waits, and a run whose
    /// segment nothing will attempt again is one they would wait on forever.
    /// <para>
    /// The reading can go stale the instant it is taken, which is what makes it safe to take here — the answer sends
    /// somebody to look at a queue rather than excluding anything, and asking again re-reads it.
    /// </para>
    /// </remarks>
    private async Task<StoredMailRederivationCarriage> CarriageOfAsync(
        JobEnqueueResult enqueued,
        CancellationToken cancellationToken)
    {
        if (enqueued.Outcome is JobEnqueueOutcome.RefusedAtCapacity)
        {
            return StoredMailRederivationCarriage.QueueAtCapacity;
        }

        if (enqueued.Outcome is JobEnqueueOutcome.Created || enqueued.JobId is not { } jobId)
        {
            return StoredMailRederivationCarriage.Carried;
        }

        return await this.jobs.FindStateAsync(jobId, cancellationToken) switch
        {
            JobState.Pending or JobState.Claimed => StoredMailRederivationCarriage.Carried,
            _ => StoredMailRederivationCarriage.Stopped,
        };
    }
}
