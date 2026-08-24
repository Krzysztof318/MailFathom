// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Execution;
using MailFathom.CodeCoverage;
using MailFathom.Infrastructure.Observability;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Jobs;

/// <summary>Keeps durable background work in PostgreSQL, and leases each job to one attempt at a time.</summary>
/// <remarks>
/// <para>
/// Five of the six operations are written statements rather than composed queries, and each for the same reason: the
/// guarantee is the statement's atomicity. Enqueuing inserts on the unique key and lets the database refuse the
/// duplicate; claiming selects and stamps under <c>FOR UPDATE SKIP LOCKED</c>; and renewal, completion, failure, and
/// release are each a single conditional update that writes nothing when the lease has moved on. Reading a row and then
/// writing it would leave a window between the two in every one of those.
/// </para>
/// <para>
/// The scoped context is used throughout and no method takes a persistence session, because a job is enqueued against
/// state that is already committed. There is therefore nothing for a caller to enlist this in, which is what makes
/// enqueuing uncommitted work structurally impossible rather than merely discouraged.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class JobStore(
    MailFathomDbContext dbContext,
    JobCapacitySettings capacity,
    TimeProvider timeProvider) : IJobStore
{
    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// The insert names the conflict target explicitly, so a duplicate is recognized as one the unique index refused
    /// rather than as any other constraint. A losing insert writes nothing at all — the existing row keeps its state,
    /// its attempts, and its lease — and the identifier is then read back, which is what answers a retrying enqueuer
    /// instead of refusing it.
    /// </para>
    /// <para>
    /// The depth of this type's queue is read before anything is written, and a full one is answered rather than
    /// inserted into. It is deliberately a read and not a condition on the insert: a full queue still has to tell a
    /// retrying enqueuer that its own work is already there, which means looking the identity up, and folding both into
    /// one statement would make the ordinary path pay for the exceptional one.
    /// </para>
    /// <para>
    /// Two enqueuers meeting the bound together can therefore both pass it, and the depth overshoots by as many callers
    /// as raced. That is the bound behaving as backpressure rather than as an invariant, which is what it is for: it
    /// exists to stop a backlog growing without limit, and a handful of rows past the ceiling costs nothing that
    /// serializing every enqueue behind a lock would not cost far more of.
    /// </para>
    /// </remarks>
    public async Task<JobEnqueueResult> EnqueueAsync(
        JobEnqueueRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var payload = JobPayloadDocument.Serialize(request.Payload);
        var enqueuedAt = timeProvider.GetUtcNow();
        var jobTypeName = request.JobType.Name;
        var idempotencyKey = request.Key.Value;

        if (await this.IsQueueFullAsync(jobTypeName, cancellationToken))
        {
            var queuedId = await this.FindJobIdAsync(jobTypeName, idempotencyKey, cancellationToken);

            return queuedId is { } alreadyEnqueuedId
                ? JobEnqueueResult.AlreadyEnqueued(JobId.Create(alreadyEnqueuedId))
                : JobEnqueueResult.RefusedAtCapacity();
        }

        var createdIds = await dbContext.Database
            .SqlQuery<Guid>(JobEnqueueStatement.Compose(
                Guid.CreateVersion7(enqueuedAt),
                request,
                payload,
                enqueuedAt,
                EnqueuedTraceCapture.Current()))
            .ToArrayAsync(cancellationToken);

        if (createdIds is [var createdId])
        {
            return JobEnqueueResult.Created(JobId.Create(createdId));
        }

        var existingId = await dbContext.Jobs
            .AsNoTracking()
            .Where(job => job.JobType == jobTypeName && job.IdempotencyKey == idempotencyKey)
            .Select(job => job.Id)
            .SingleAsync(cancellationToken);

        return JobEnqueueResult.AlreadyEnqueued(JobId.Create(existingId));
    }

    /// <inheritdoc />
    /// <remarks>
    /// The claim itself is the statement; the read that follows it is not part of it. Nothing else can take the rows it
    /// returned, because they are stamped with this attempt and its lease has not run out, so reading them back is a
    /// plain query rather than a second half of the exclusion.
    /// </remarks>
    public async Task<IReadOnlyList<LeasedJob>> ClaimAsync(
        JobClaimRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var claimedAt = timeProvider.GetUtcNow();

        var claimedIds = await dbContext.Database
            .SqlQuery<Guid>(JobClaimStatement.Compose(request, claimedAt))
            .ToArrayAsync(cancellationToken);

        if (claimedIds.Length == 0)
        {
            return [];
        }

        var claimedJobs = await dbContext.Jobs
            .AsNoTracking()
            .Where(job => claimedIds.Contains(job.Id))
            .OrderBy(job => job.TurnAt)
            .ThenBy(job => job.Id)
            .ToArrayAsync(cancellationToken);

        return [.. claimedJobs.Select(JobRecordMapping.ToLeasedJob)];
    }

    /// <inheritdoc />
    /// <remarks>A plain projection: the state is a column, and nothing about reading it needs the row's other values.</remarks>
    public async Task<JobState?> FindStateAsync(JobId jobId, CancellationToken cancellationToken)
    {
        var jobIdValue = jobId.Value;

        return await dbContext.Jobs
            .AsNoTracking()
            .Where(job => job.Id == jobIdValue)
            .Select(job => (JobState?)job.State)
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The condition is the owner and the state rather than the expiry, so an attempt whose lease has run out but which
    /// nothing has reclaimed yet renews it and goes on working. That is safe because it still holds the row exclusively:
    /// what makes two attempts impossible is the claim, and this attempt is still the one the claim stamped. Refusing on
    /// the expiry instead would abandon work nobody else had taken.
    /// </remarks>
    public async Task<JobLease?> RenewLeaseAsync(
        JobId jobId,
        JobLeaseOwner owner,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(leaseDuration, TimeSpan.Zero);

        var leaseExpiresAt = timeProvider.GetUtcNow() + leaseDuration;
        var jobIdValue = jobId.Value;
        var ownerValue = owner.Value;
        var claimed = nameof(JobState.Claimed);

        var renewedRows = await dbContext.Database.ExecuteSqlAsync(
            $"""
             UPDATE jobs
             SET "LeaseExpiresAt" = {leaseExpiresAt}
             WHERE "Id" = {jobIdValue}
               AND "State" = {claimed}
               AND "LeaseOwner" = {ownerValue}
             """,
            cancellationToken);

        return renewedRows == 1 ? new JobLease(owner, leaseExpiresAt) : null;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The lease is cleared with the state, so a terminal row names no holder. What it keeps is its key, which is what
    /// stops the same trigger enqueuing the same work again.
    /// </remarks>
    public async Task<bool> CompleteAsync(JobId jobId, JobLeaseOwner owner, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var stateChangedAt = timeProvider.GetUtcNow();
        var jobIdValue = jobId.Value;
        var ownerValue = owner.Value;
        var claimed = nameof(JobState.Claimed);
        var succeeded = nameof(JobState.Succeeded);

        var completedRows = await dbContext.Database.ExecuteSqlAsync(
            $"""
             UPDATE jobs
             SET "State" = {succeeded},
                 "LeaseOwner" = NULL,
                 "LeaseExpiresAt" = NULL,
                 "StateChangedAt" = {stateChangedAt}
             WHERE "Id" = {jobIdValue}
               AND "State" = {claimed}
               AND "LeaseOwner" = {ownerValue}
             """,
            cancellationToken);

        return completedRows == 1;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// The same conditional update the others are, and the job returns to the queue rather than staying held: what
    /// keeps it from being taken again at once is the available instant, which is the whole of the backoff as far as
    /// the queue is concerned. The attempt count is left where the claim put it, because the attempt was spent.
    /// </para>
    /// <para>
    /// The turn moves with it, but only forward: a job cannot hold a turn earlier than the instant it becomes claimable
    /// again, or the backoff would end with it in front of everything that waited through it. It keeps a later turn
    /// where it has one, because the place the enqueue gave it among its owner's work is not something a transient
    /// failure should improve on.
    /// </para>
    /// </remarks>
    public async Task<bool> ScheduleRetryAsync(
        JobId jobId,
        JobLeaseOwner owner,
        JobFailureRecord failure,
        DateTimeOffset availableAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(failure);

        var stateChangedAt = timeProvider.GetUtcNow();
        var jobIdValue = jobId.Value;
        var ownerValue = owner.Value;
        var claimed = nameof(JobState.Claimed);
        var pending = nameof(JobState.Pending);
        var classification = failure.Classification.ToString();
        var reason = failure.Reason;

        var scheduledRows = await dbContext.Database.ExecuteSqlAsync(
            $"""
             UPDATE jobs
             SET "State" = {pending},
                 "LeaseOwner" = NULL,
                 "LeaseExpiresAt" = NULL,
                 "AvailableAt" = {availableAt},
                 "TurnAt" = GREATEST("TurnAt", {availableAt}),
                 "LastFailureClassification" = {classification},
                 "LastFailureReason" = {reason},
                 "StateChangedAt" = {stateChangedAt}
             WHERE "Id" = {jobIdValue}
               AND "State" = {claimed}
               AND "LeaseOwner" = {ownerValue}
             """,
            cancellationToken);

        return scheduledRows == 1;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The same conditional update completion is, and terminal in the same way: the lease is cleared, the key is kept,
    /// and the available instant is left where it was because no claim reads it again — the claim's own predicate names
    /// the two claimable states, so a dead letter is outside it and outside the partial index over it as well.
    /// </remarks>
    public async Task<bool> DeadLetterAsync(
        JobId jobId,
        JobLeaseOwner owner,
        JobFailureRecord failure,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(failure);

        var stateChangedAt = timeProvider.GetUtcNow();
        var jobIdValue = jobId.Value;
        var ownerValue = owner.Value;
        var claimed = nameof(JobState.Claimed);
        var deadLettered = nameof(JobState.DeadLettered);
        var classification = failure.Classification.ToString();
        var reason = failure.Reason;

        var deadLetteredRows = await dbContext.Database.ExecuteSqlAsync(
            $"""
             UPDATE jobs
             SET "State" = {deadLettered},
                 "LeaseOwner" = NULL,
                 "LeaseExpiresAt" = NULL,
                 "LastFailureClassification" = {classification},
                 "LastFailureReason" = {reason},
                 "StateChangedAt" = {stateChangedAt}
             WHERE "Id" = {jobIdValue}
               AND "State" = {claimed}
               AND "LeaseOwner" = {ownerValue}
             """,
            cancellationToken);

        return deadLetteredRows == 1;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The available instant moves to now rather than staying where it was, so a released job is claimable immediately
    /// instead of waiting out a schedule that has already been honoured. The attempt the claim counted is given back
    /// with it, guarded so a count nothing else could have lowered cannot go negative: a shutdown is the operator's act
    /// rather than the work's failure, and a long job met by a few rolling restarts would otherwise reach the attempt
    /// bound and be dead-lettered without ever having failed.
    /// </remarks>
    public async Task<bool> ReleaseAsync(JobId jobId, JobLeaseOwner owner, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var releasedAt = timeProvider.GetUtcNow();
        var jobIdValue = jobId.Value;
        var ownerValue = owner.Value;
        var claimed = nameof(JobState.Claimed);
        var pending = nameof(JobState.Pending);

        var releasedRows = await dbContext.Database.ExecuteSqlAsync(
            $"""
             UPDATE jobs
             SET "State" = {pending},
                 "LeaseOwner" = NULL,
                 "LeaseExpiresAt" = NULL,
                 "AvailableAt" = {releasedAt},
                 "AttemptCount" = GREATEST("AttemptCount" - 1, 0),
                 "StateChangedAt" = {releasedAt}
             WHERE "Id" = {jobIdValue}
               AND "State" = {claimed}
               AND "LeaseOwner" = {ownerValue}
             """,
            cancellationToken);

        return releasedRows == 1;
    }

    /// <summary>Answers whether this job type already has as much waiting as the configured depth allows.</summary>
    /// <remarks>
    /// Waiting is the pending state alone. A job a worker holds is running, and what bounds that is the concurrency
    /// ceiling rather than this; counting it here would make an instance that is draining its queue look like one that
    /// is filling it.
    /// </remarks>
    private async Task<bool> IsQueueFullAsync(string jobTypeName, CancellationToken cancellationToken)
    {
        var waitingCount = await JobQueueDepthQuery
            .Compose(dbContext.Jobs.AsNoTracking(), jobTypeName, capacity.MaxQueueDepthPerType)
            .CountAsync(cancellationToken);

        return waitingCount >= capacity.MaxQueueDepthPerType;
    }

    /// <summary>Finds the job already carrying an identity, which is what a refused enqueue asks before it refuses.</summary>
    private async Task<Guid?> FindJobIdAsync(
        string jobTypeName,
        string idempotencyKey,
        CancellationToken cancellationToken) => await dbContext.Jobs
        .AsNoTracking()
        .Where(job => job.JobType == jobTypeName && job.IdempotencyKey == idempotencyKey)
        .Select(job => (Guid?)job.Id)
        .SingleOrDefaultAsync(cancellationToken);
}
