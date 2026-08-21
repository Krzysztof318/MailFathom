// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Jobs.Execution;

/// <summary>Measures how much work of each type is waiting to be claimed.</summary>
/// <remarks>
/// <para>
/// Waiting is the pending state alone, which is the same reading the enqueue bound is applied against: a job a worker
/// holds is running, and what bounds that is the concurrency ceiling rather than the queue's depth. Counting a running
/// job here would make an instance draining its queue look like one filling it, and would make the depth an operator
/// watches disagree with the depth an enqueue is refused at.
/// </para>
/// <para>
/// The count saturates at the configured depth bound rather than reporting a true total, so the cost of asking stays
/// the same on a queue of a thousand and on a queue of a million. A reading sitting at the bound is the reading that
/// matters anyway: it is the point at which enqueuing is already being refused as backpressure.
/// </para>
/// </remarks>
public interface IJobQueueDepthReader
{
    /// <summary>Measures what is waiting for each of the types named.</summary>
    /// <param name="jobTypes">The types to measure, ordinarily the ones this process can run.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>One reading per type named, in the order they were named.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="jobTypes" /> is <see langword="null" />.</exception>
    /// <remarks>A type with nothing waiting is reported as zero rather than left out, so a queue that emptied stops reporting its last non-zero depth.</remarks>
    Task<IReadOnlyList<JobQueueDepthReading>> ReadWaitingDepthsAsync(
        IReadOnlyList<JobType> jobTypes,
        CancellationToken cancellationToken);
}

/// <summary>How much work of one type was waiting when the queue was last measured.</summary>
/// <param name="JobType">The kind of work measured.</param>
/// <param name="WaitingCount">How many jobs of it were claimable or waiting for their next attempt, up to the configured depth bound.</param>
public sealed record JobQueueDepthReading(JobType JobType, int WaitingCount);
