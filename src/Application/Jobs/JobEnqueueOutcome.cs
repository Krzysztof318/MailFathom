// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Jobs;

/// <summary>States what enqueuing did, which is not always what it asked for.</summary>
/// <remarks>
/// Every member is an ordinary answer rather than a failure, which is why enqueuing reports a result instead of
/// throwing. A trigger that fires twice, a synchronization retry, and a provider redelivery all reach the store with the
/// same key, and being told the work is already queued is what they act on; a caller meeting a full queue is being told
/// to stop producing, which is a decision it can make and an exception would only have obscured.
/// </remarks>
public enum JobEnqueueOutcome
{
    /// <summary>This call created the job.</summary>
    Created = 0,

    /// <summary>A job with this type and key already existed, in whatever state, and nothing was written.</summary>
    /// <remarks>Every state counts, terminal ones included: a row that succeeded is what stops the same trigger enqueuing the same work again.</remarks>
    AlreadyEnqueued = 1,

    /// <summary>As many jobs of this type were already waiting as the queue accepts, so nothing was written.</summary>
    /// <remarks>
    /// This is backpressure rather than a defect: the work was neither queued nor lost, and it is the caller's to
    /// enqueue again once the queue has drained, to slow down, or to stop producing. The bound is per job type, so
    /// another type is enqueued normally while this one is full.
    /// </remarks>
    RefusedAtCapacity = 2,
}
