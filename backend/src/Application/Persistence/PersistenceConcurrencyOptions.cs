// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Persistence;

/// <summary>Bounds how often a safe local write is repeated after optimistic concurrency conflicts.</summary>
/// <remarks>
/// The bound is deployment-wide rather than per use case. An optimistic conflict is a property of the shared row
/// version that every retrying writer competes for, not of the operation that happened to observe it, so a per-service
/// attempt count would multiply configuration without describing anything a deployment can reason about separately.
/// Backoff shape stays inside <see cref="OptimisticConcurrencyRetryPolicy" /> because it is collision-avoidance detail;
/// the attempt count is an operational safety limit and is therefore configurable.
/// </remarks>
public sealed class PersistenceConcurrencyOptions
{
    /// <summary>Gets or sets the maximum number of complete commit attempts, including the first one.</summary>
    /// <remarks>
    /// Two attempts cover the single lost race that a rare conflict actually represents. Further attempts mostly add
    /// latency before the caller has to reread current state anyway, so exhaustion is reported rather than hidden
    /// behind a longer loop.
    /// </remarks>
    public int MaximumCommitAttempts { get; set; } = 2;
}
