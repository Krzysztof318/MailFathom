// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Jobs.Scheduling;

/// <summary>What one pass over one schedule did about it.</summary>
public enum JobScheduleDispatchOutcome
{
    /// <summary>The schedule was seen for the first time, so its occasions count from now and nothing was dispatched.</summary>
    /// <remarks>A schedule is a <em>when</em> rather than a debt, so the occasion that had already passed when the declaration arrived is not owed to anybody.</remarks>
    Seeded = 0,

    /// <summary>No occasion has passed since the one the schedule last accounted for.</summary>
    NotDue = 1,

    /// <summary>The occasion was enqueued, and this pass is what wrote the job.</summary>
    Dispatched = 2,

    /// <summary>The occasion was already enqueued under this identity, so the job that carries it was answered with.</summary>
    /// <remarks>Two replicas reaching one occasion together is the ordinary way here, and it is the queue's own uniqueness rather than a lock that resolves it.</remarks>
    AlreadyDispatched = 3,

    /// <summary>The job the previous occasion enqueued is still pending or held, so this one stood down.</summary>
    PreviousRunInFlight = 4,

    /// <summary>The queue held as much of this type as its depth allows, so the occasion was passed over.</summary>
    RefusedAtCapacity = 5,
}
