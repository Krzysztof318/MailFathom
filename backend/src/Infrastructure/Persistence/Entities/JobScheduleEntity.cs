// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>What one recurring dispatch has already done, keyed by the identity its declaration composes.</summary>
/// <remarks>
/// <para>
/// One row per schedule, so a schedule cannot be two things at once and a second replica advancing it writes over the
/// same row rather than adding another. There is deliberately no row for a schedule nobody has declared: the
/// declarations live in configuration, and this table records only what has happened to them.
/// </para>
/// <para>
/// No optimistic concurrency token, for the reason the job row carries none. Two replicas reaching one occasion compose
/// the same idempotency key, so the queue resolves the duplicate and both then write the same occasion here; a conflict
/// reported over that would be a retry spent agreeing with itself.
/// </para>
/// <para>
/// No foreign key onto the job the last dispatch enqueued. The job row can be pruned or dropped independently, and a
/// schedule that could not advance because the row it pointed at had gone would stop for good.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class JobScheduleEntity
{
    /// <summary>Gets or sets the identity the declaration composes, which is what the schedule is keyed by.</summary>
    public required string ScheduleId { get; set; }

    /// <summary>Gets or sets the instant the schedule was first seen, from which its occasions count.</summary>
    public DateTimeOffset ObservedFrom { get; set; }

    /// <summary>Gets or sets the occasion last dispatched or passed over, absent while there has been none.</summary>
    public DateTimeOffset? LastOccurrenceAt { get; set; }

    /// <summary>Gets or sets the job the last dispatch enqueued, absent while none is being watched.</summary>
    public Guid? LastDispatchedJobId { get; set; }
}
