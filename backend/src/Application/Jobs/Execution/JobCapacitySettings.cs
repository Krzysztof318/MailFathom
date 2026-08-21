// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Jobs.Execution;

/// <summary>Bounds how much of an instance the queue may take: how much runs at once, and how much may be waiting.</summary>
/// <remarks>
/// <para>
/// Two bounds that are different in kind, which is why they are stated together rather than derived from each other.
/// Concurrency limits what runs at once, and it is what protects the database connections, the memory, and the processor
/// an MCP read also needs. Queue depth limits what may be waiting, and when it is reached the enqueuer is told rather
/// than served — a queue that silently grows is one that fails later, further from the cause, and with a backlog
/// somebody has to clear by hand.
/// </para>
/// <para>
/// Both are per job type as well, because one consumer flooding the queue is the ordinary case rather than the exotic
/// one. A single shared ceiling reached by one type would starve every other, so the per-type bound is what keeps a bulk
/// re-evaluation of one kind of work from being the reason another kind never runs.
/// </para>
/// <para>
/// A per-type concurrency ceiling above the process ceiling is refused rather than clamped. The process ceiling already
/// caps it, so the larger number would state a bound nothing can reach, and a bound that cannot be reached is a bound an
/// operator believes they have.
/// </para>
/// </remarks>
public sealed record JobCapacitySettings
{
    private JobCapacitySettings(int maxConcurrentJobs, int maxConcurrentJobsPerType, int maxQueueDepthPerType)
    {
        this.MaxConcurrentJobs = maxConcurrentJobs;
        this.MaxConcurrentJobsPerType = maxConcurrentJobsPerType;
        this.MaxQueueDepthPerType = maxQueueDepthPerType;
    }

    /// <summary>Gets how many jobs this process may run at once, across every type together.</summary>
    public int MaxConcurrentJobs { get; }

    /// <summary>Gets how many jobs of one type this process may run at once.</summary>
    /// <remarks>Never above <see cref="MaxConcurrentJobs" />, which already caps it.</remarks>
    public int MaxConcurrentJobsPerType { get; }

    /// <summary>Gets how many jobs of one type may be waiting before enqueuing expresses backpressure.</summary>
    /// <remarks>
    /// Waiting means pending rather than outstanding: work a process is already running is bounded by the concurrency
    /// ceilings above and is not part of the depth this refuses against.
    /// </remarks>
    public int MaxQueueDepthPerType { get; }

    /// <summary>States the capacity the queue runs under.</summary>
    /// <param name="maxConcurrentJobs">How many jobs this process may run at once, across every type together.</param>
    /// <param name="maxConcurrentJobsPerType">How many jobs of one type this process may run at once.</param>
    /// <param name="maxQueueDepthPerType">How many jobs of one type may be waiting before enqueuing expresses backpressure.</param>
    /// <returns>The validated capacity.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a bound is not positive, or when <paramref name="maxConcurrentJobsPerType" /> exceeds <paramref name="maxConcurrentJobs" />.</exception>
    public static JobCapacitySettings Create(
        int maxConcurrentJobs,
        int maxConcurrentJobsPerType,
        int maxQueueDepthPerType)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxConcurrentJobs);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxConcurrentJobsPerType);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxConcurrentJobsPerType, maxConcurrentJobs);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxQueueDepthPerType);

        return new JobCapacitySettings(maxConcurrentJobs, maxConcurrentJobsPerType, maxQueueDepthPerType);
    }
}
