// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Scheduling;
using MailFathom.CodeCoverage;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Jobs;

/// <summary>Keeps in PostgreSQL what each recurring dispatch has already done.</summary>
/// <remarks>
/// The write is one upsert rather than a read followed by an insert, for the reason enqueuing is one statement: two
/// replicas advancing one schedule together must resolve to one row, and a check between the two statements would leave
/// exactly the window that resolution exists to close. Both writers agree on the value they write — the occasion is
/// derived from the declaration and the clock, not from either of them — so last one wins is the right resolution rather
/// than a conflict to report.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class JobScheduleStore(MailFathomDbContext dbContext) : IJobScheduleStore
{
    /// <inheritdoc />
    /// <remarks>Read in one query rather than one per schedule, because the pass decides about every declared schedule and a deployment declares one per scheduled rule and account.</remarks>
    public async Task<IReadOnlyDictionary<string, JobScheduleState>> ReadAsync(
        IReadOnlyCollection<JobScheduleId> ids,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);

        if (ids.Count == 0)
        {
            return new Dictionary<string, JobScheduleState>(StringComparer.Ordinal);
        }

        var identities = ids.Select(id => id.Value).ToArray();
        var stored = await dbContext.JobSchedules
            .AsNoTracking()
            .Where(schedule => identities.Contains(schedule.ScheduleId))
            .ToArrayAsync(cancellationToken);

        return stored.ToDictionary(schedule => schedule.ScheduleId, Read, StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public async Task SaveAsync(JobScheduleState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);

        var scheduleId = state.Id.Value;
        var observedFrom = state.ObservedFrom;
        var lastOccurrenceAt = state.LastOccurrenceAt;
        var lastDispatchedJobId = state.LastDispatchedJobId?.Value;

        await dbContext.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO job_schedules ("ScheduleId", "ObservedFrom", "LastOccurrenceAt", "LastDispatchedJobId")
             VALUES ({scheduleId}, {observedFrom}, {lastOccurrenceAt}, {lastDispatchedJobId})
             ON CONFLICT ("ScheduleId") DO UPDATE
             SET "LastOccurrenceAt" = EXCLUDED."LastOccurrenceAt",
                 "LastDispatchedJobId" = EXCLUDED."LastDispatchedJobId"
             """,
            cancellationToken);
    }

    /// <summary>Rebuilds the state one stored row describes.</summary>
    /// <remarks>
    /// The instant the schedule was first seen is never rewritten by an update, so a row that already exists keeps the
    /// point its occasions are counted from even when a later pass writes everything else about it.
    /// </remarks>
    private static JobScheduleState Read(JobScheduleEntity entity) => new()
    {
        Id = JobScheduleId.Create(entity.ScheduleId),
        ObservedFrom = entity.ObservedFrom,
        LastOccurrenceAt = entity.LastOccurrenceAt,
        LastDispatchedJobId = entity.LastDispatchedJobId is { } jobId ? JobId.Create(jobId) : null,
    };
}
