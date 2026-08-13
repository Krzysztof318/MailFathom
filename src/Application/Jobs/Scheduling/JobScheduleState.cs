// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Jobs.Scheduling;

/// <summary>What one recurring dispatch has already done, which is the whole of what a schedule keeps between passes.</summary>
/// <remarks>
/// <para>
/// Durable because a schedule is otherwise unable to tell a restart from an occasion. Without the occasion it last
/// dispatched, a process coming back up would either fire every schedule again or fire none of them, and which of the
/// two it did would depend on when it happened to come back.
/// </para>
/// <para>
/// <see cref="ObservedFrom" /> is what makes a schedule a <em>when</em> rather than a debt. A schedule this deployment
/// has never dispatched is seeded with the instant it was first seen and dispatches nothing for it, so adding a rule at
/// noon does not immediately fire the occasion it declared for three in the morning.
/// </para>
/// <para>
/// Every field is an instant or MailFathom's own identity for something. Nothing derived from a message belongs in a
/// row an operator reads to find out why their housekeeping has not run.
/// </para>
/// </remarks>
public sealed record JobScheduleState
{
    /// <summary>Gets the schedule this state belongs to.</summary>
    public required JobScheduleId Id { get; init; }

    /// <summary>Gets the instant from which this schedule's occasions count, which is when it was first seen.</summary>
    public required DateTimeOffset ObservedFrom { get; init; }

    /// <summary>Gets the occasion last dispatched or deliberately passed over, or <see langword="null" /> while there has been none.</summary>
    /// <remarks>
    /// It is the occasion's own instant rather than the instant the dispatch happened, which is what keeps a schedule
    /// from drifting: a pass that noticed an occasion five minutes late still advances the schedule to the occasion.
    /// </remarks>
    public DateTimeOffset? LastOccurrenceAt { get; init; }

    /// <summary>Gets the job the last dispatch enqueued, or <see langword="null" /> when none is being watched.</summary>
    /// <remarks>
    /// What makes one run per schedule at a time enforceable rather than assumed: the next occasion asks what became of
    /// this job, and stands down while it is still pending or held.
    /// </remarks>
    public JobId? LastDispatchedJobId { get; init; }

    /// <summary>Reads the instant this schedule's occasions are counted from.</summary>
    /// <returns>The occasion last accounted for, and the seeding instant while there has been none.</returns>
    public DateTimeOffset CountedFrom => this.LastOccurrenceAt ?? this.ObservedFrom;
}
