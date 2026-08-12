// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Jobs;

/// <summary>States where one durable job stands between being enqueued and being finished with.</summary>
/// <remarks>
/// The members are names the process reads and the stored row carries, and nothing about a state has to be carried
/// with it, so this is an ordinary enum rather than a closed enumeration. It is stored as its name for the reason every
/// other bounded value in this schema is: the row stays readable in an ad-hoc query, and no later member can change
/// what an existing row means.
/// </remarks>
public enum JobState
{
    /// <summary>The job is enqueued and claimable once its available instant has passed.</summary>
    Pending = 0,

    /// <summary>A worker holds a lease on the job. It becomes claimable again on its own when that lease expires.</summary>
    Claimed = 1,

    /// <summary>The work was done. The row is terminal and keeps its idempotency key, so the same trigger cannot enqueue it again.</summary>
    Succeeded = 2,
}
