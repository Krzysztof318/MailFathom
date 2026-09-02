// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Domain.Accounts;
using MailFathom.Infrastructure.Persistence.Entities;

namespace MailFathom.Infrastructure.Persistence.Jobs;

/// <summary>Rebuilds the leased job one claimed row describes.</summary>
internal static class JobRecordMapping
{
    /// <summary>Rebuilds the job a claimed row hands to the attempt that took it.</summary>
    /// <param name="entity">The stored row, as it stands after the claim stamped it.</param>
    /// <returns>The job that row states.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the row names a job type this build does not declare, or carries no lease.</exception>
    /// <remarks>
    /// A row with no lease is refused rather than returned unleased, because this mapping only ever reads a row the
    /// claim statement has just stamped: one without a lease means the row was read through some other path, and
    /// answering with a job nobody holds would let work run outside the exclusion the lease is.
    /// </remarks>
    internal static LeasedJob ToLeasedJob(JobEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        if (!JobType.TryParseName(entity.JobType, out var jobType))
        {
            throw new InvalidOperationException(
                $"Job {entity.Id} names '{entity.JobType}', which is not a job type this build declares.");
        }

        if (entity is not { LeaseOwner: { } leaseOwner, LeaseExpiresAt: { } leaseExpiresAt })
        {
            throw new InvalidOperationException($"Job {entity.Id} was read as claimed and carries no lease.");
        }

        return new LeasedJob(
            JobId.Create(entity.Id),
            jobType,
            JobIdempotencyKey.Create(entity.IdempotencyKey),
            JobPayloadDocument.Deserialize(jobType, entity.Payload),
            entity.MailboxAccountId is { } accountId ? MailAccountId.Create(accountId) : null,
            entity.AttemptCount,
            new JobLease(JobLeaseOwner.Create(leaseOwner), leaseExpiresAt),
            JobTraceContext.FromTraceParent(entity.EnqueuedTraceParent, entity.EnqueuedTraceState));
    }
}
