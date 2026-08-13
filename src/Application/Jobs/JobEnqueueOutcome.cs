// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Jobs;

/// <summary>States what enqueuing did, which is not always what it asked for.</summary>
/// <remarks>
/// Both members are ordinary answers rather than failures, which is why enqueuing reports a result instead of refusing.
/// A trigger that fires twice, a synchronization retry, and a provider redelivery all reach the store with the same
/// key, and being told the work is already queued is what they act on.
/// </remarks>
public enum JobEnqueueOutcome
{
    /// <summary>This call created the job.</summary>
    Created = 0,

    /// <summary>A job with this type and key already existed, in whatever state, and nothing was written.</summary>
    /// <remarks>Every state counts, terminal ones included: a row that succeeded is what stops the same trigger enqueuing the same work again.</remarks>
    AlreadyEnqueued = 1,
}
