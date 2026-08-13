// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Jobs.Scheduling;

/// <summary>What one pass decided about one schedule, and what it passed over deciding it.</summary>
/// <remarks>
/// <see cref="SkippedOccurrenceCount" /> is the part that has to be reported rather than inferred. A process that was
/// down over several occasions dispatches the most recent one and nothing else, so without this count a burst that was
/// deliberately not run would be indistinguishable from a schedule that ran exactly as declared.
/// </remarks>
/// <param name="Id">The schedule the pass decided about.</param>
/// <param name="JobType">The type of work the schedule repeats, which is what a measurement is broken down by.</param>
/// <param name="Outcome">What the pass did.</param>
/// <param name="OccurrenceAt">The occasion the pass acted on, or <see langword="null" /> when it acted on none.</param>
/// <param name="SkippedOccurrenceCount">How many occasions this pass passed over, which is zero for a schedule that has kept up.</param>
public sealed record JobScheduleDispatch(
    JobScheduleId Id,
    JobType JobType,
    JobScheduleDispatchOutcome Outcome,
    DateTimeOffset? OccurrenceAt,
    int SkippedOccurrenceCount);
