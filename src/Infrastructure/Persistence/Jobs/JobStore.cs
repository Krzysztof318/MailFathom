// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.CodeCoverage;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Jobs;

/// <summary>Keeps durable background work in PostgreSQL, and leases each job to one attempt at a time.</summary>
/// <remarks>
/// <para>
/// Four of the five operations are written statements rather than composed queries, and each for the same reason: the
/// guarantee is the statement's atomicity. Enqueuing inserts on the unique key and lets the database refuse the
/// duplicate; claiming selects and stamps under <c>FOR UPDATE SKIP LOCKED</c>; and renewal, completion, and release are
/// each a single conditional update that writes nothing when the lease has moved on. Reading a row and then writing it
/// would leave a window between the two in every one of those.
/// </para>
/// <para>
/// The scoped context is used throughout and no method takes a persistence session, because a job is enqueued against
/// state that is already committed. There is therefore nothing for a caller to enlist this in, which is what makes
/// enqueuing uncommitted work structurally impossible rather than merely discouraged.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class JobStore(MailFathomDbContext dbContext, TimeProvider timeProvider) : IJobStore
{
    /// <inheritdoc />
    /// <remarks>
    /// The insert names the conflict target explicitly, so a duplicate is recognized as one the unique index refused
    /// rather than as any other constraint. A losing insert writes nothing at all — the existing row keeps its state,
    /// its attempts, and its lease — and the identifier is then read back, which is what answers a retrying enqueuer
    /// instead of refusing it.
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

        var createdIds = await dbContext.Database
            .SqlQuery<Guid>(JobEnqueueStatement.Compose(
                Guid.CreateVersion7(enqueuedAt),
                request,
                payload,
                enqueuedAt))
            .ToArrayAsync(cancellationToken);

        if (createdIds is [var createdId])
        {
            return new JobEnqueueResult(JobId.Create(createdId), JobEnqueueOutcome.Created);
        }

        var existingId = await dbContext.Jobs
            .AsNoTracking()
            .Where(job => job.JobType == jobTypeName && job.IdempotencyKey == idempotencyKey)
            .Select(job => job.Id)
            .SingleAsync(cancellationToken);

        return new JobEnqueueResult(JobId.Create(existingId), JobEnqueueOutcome.AlreadyEnqueued);
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
            .OrderBy(job => job.AvailableAt)
            .ThenBy(job => job.Id)
            .ToArrayAsync(cancellationToken);

        return [.. claimedJobs.Select(JobRecordMapping.ToLeasedJob)];
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
    /// The available instant moves to now rather than staying where it was, so a released job is claimable immediately
    /// instead of waiting out a schedule that has already been honoured. The attempt stays counted: it was handed out.
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
                 "StateChangedAt" = {releasedAt}
             WHERE "Id" = {jobIdValue}
               AND "State" = {claimed}
               AND "LeaseOwner" = {ownerValue}
             """,
            cancellationToken);

        return releasedRows == 1;
    }
}
