// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.DeadLetters;
using MailFathom.CodeCoverage;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Jobs;

/// <summary>Reads the jobs that stopped, and writes the two decisions an operator takes about one, in PostgreSQL.</summary>
/// <remarks>
/// <para>
/// The reading is a projection rather than an entity load, and the payload column is the reason: it is a document
/// naming a message occurrence, and nothing on this surface reports it, so it is left unread rather than read and then
/// dropped.
/// </para>
/// <para>
/// Both writes are one conditional update apiece, in the shape every other write against a job row takes. The condition
/// here is the dead-lettered state rather than a lease owner, because a dead letter is held by nobody — which makes the
/// statement the whole of the exclusion: two operators acting at once produce one change, and the second reads the row
/// afterwards to find out that it was the second.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class DeadLetteredJobStore(MailFathomDbContext dbContext, TimeProvider timeProvider)
    : IDeadLetteredJobStore
{
    /// <inheritdoc />
    /// <remarks>The read takes one row past the page, for the reason <see cref="KeysetPageSplit" /> states.</remarks>
    public async Task<DeadLetteredJobPage> ReadPageAsync(
        DeadLetteredJobQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var rows = await this.Filter(query)
            .OrderByDescending(job => job.StateChangedAt)
            .ThenByDescending(job => job.Id)
            .Take(query.PageSize + 1)
            .Select(job => new DeadLetteredJobRow(
                job.Id,
                job.JobType,
                job.IdempotencyKey,
                job.MailboxAccountId,
                job.AttemptCount,
                job.LastFailureClassification,
                job.LastFailureReason,
                job.EnqueuedAt,
                job.StateChangedAt))
            .ToArrayAsync(cancellationToken);

        var (pageRows, hasMore) = KeysetPageSplit.Of(rows, query.PageSize);

        // A row whose type this build does not declare is left out of the answer but still counted for the boundary:
        // the cursor is the position in the reading, so skipping a row must not move where the next page resumes.
        DeadLetteredJob[] jobs =
        [
            .. pageRows
                .Select(DeadLetteredJobMapping.ToDeadLetteredJob)
                .Where(static job => job is not null)
                .Select(static job => job!),
        ];

        return new DeadLetteredJobPage(
            jobs,
            hasMore
                ? DeadLetteredJobCursor.After(
                    pageRows[^1].StateChangedAt,
                    JobId.Create(pageRows[^1].Id),
                    query.FilterFingerprint)
                : null);
    }

    /// <inheritdoc />
    /// <remarks>What the statement writes and leaves alone is <see cref="DeadLetteredJobDecisionStatements.ComposeRetry" />'s.</remarks>
    public async Task<JobRecoveryOutcome> RetryAsync(JobId jobId, CancellationToken cancellationToken)
    {
        var jobIdValue = jobId.Value;

        var retriedRows = await dbContext.Database.ExecuteSqlAsync(
            DeadLetteredJobDecisionStatements.ComposeRetry(jobIdValue, timeProvider.GetUtcNow()),
            cancellationToken);

        return retriedRows == 1
            ? JobRecoveryOutcome.Accepted
            : await this.ExplainRefusalAsync(jobIdValue, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>What the statement writes and leaves alone is <see cref="DeadLetteredJobDecisionStatements.ComposeDrop" />'s.</remarks>
    public async Task<JobRecoveryOutcome> DropAsync(JobId jobId, CancellationToken cancellationToken)
    {
        var jobIdValue = jobId.Value;

        var droppedRows = await dbContext.Database.ExecuteSqlAsync(
            DeadLetteredJobDecisionStatements.ComposeDrop(jobIdValue, timeProvider.GetUtcNow()),
            cancellationToken);

        return droppedRows == 1
            ? JobRecoveryOutcome.Accepted
            : await this.ExplainRefusalAsync(jobIdValue, cancellationToken);
    }

    /// <summary>Applies the filters a query names, leaving the ordering and the page bound to the caller.</summary>
    private IQueryable<JobEntity> Filter(DeadLetteredJobQuery query)
    {
        var jobs = dbContext.Jobs.AsNoTracking().Where(job => job.State == JobState.DeadLettered);

        if (query.JobType is { } jobType)
        {
            var jobTypeName = jobType.Name;

            jobs = jobs.Where(job => job.JobType == jobTypeName);
        }

        if (query.Account is { } account)
        {
            var ownerValue = account.Owner.Value;
            var accountValue = account.Id.Value;

            jobs = jobs.Where(job => job.OwnerId == ownerValue && job.MailboxAccountId == accountValue);
        }

        // The keyset boundary is the pair the order is taken on, so a job that stopped in the same instant as the last
        // one of the previous page is served exactly once rather than skipped or repeated. The identifier comparison is
        // evaluated by PostgreSQL as a `uuid` comparison, which is what the index is ordered by, so it never has to
        // agree with how the CLR happens to compare two `Guid` values.
        if (query.Cursor is { } cursor)
        {
            var boundaryStateChangedAt = cursor.DeadLetteredAt;
            var boundaryId = cursor.JobId.Value;

            jobs = jobs.Where(job =>
                job.StateChangedAt < boundaryStateChangedAt
                || (job.StateChangedAt == boundaryStateChangedAt && job.Id < boundaryId));
        }

        return jobs;
    }

    /// <summary>Says why a conditional update wrote nothing, which is the one thing the row count cannot.</summary>
    /// <remarks>
    /// Asked only after a write that changed no row, so the ordinary path costs one statement. The answer may already
    /// be stale by the time it is read — another operator can act in between — and that is acceptable here: both
    /// refusals tell the caller the same thing, which is that this decision was not the one that took effect.
    /// </remarks>
    private async Task<JobRecoveryOutcome> ExplainRefusalAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var exists = await dbContext.Jobs.AsNoTracking().AnyAsync(job => job.Id == jobId, cancellationToken);

        return exists ? JobRecoveryOutcome.JobNotDeadLettered : JobRecoveryOutcome.JobUnknown;
    }
}
