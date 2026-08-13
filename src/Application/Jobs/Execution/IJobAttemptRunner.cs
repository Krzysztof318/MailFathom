// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Jobs.Execution;

/// <summary>Runs one leased job in isolation from every other job running beside it.</summary>
/// <remarks>
/// <para>
/// It exists because jobs run at once and <see cref="JobExecutor" /> writes through the persistence session of whatever
/// scope resolved it. Two attempts sharing one session would use one database connection concurrently, which is neither
/// safe nor detectable afterwards, so each attempt needs a scope of its own — and creating one is the composition root's
/// to do rather than the application's.
/// </para>
/// <para>
/// The port is therefore narrow on purpose: it says what an attempt costs to start, and nothing about how work is
/// claimed, bounded, timed, or recorded, all of which stays where the rest of the queue's policy is.
/// </para>
/// </remarks>
public interface IJobAttemptRunner
{
    /// <summary>Runs one job this process holds a lease on, and reports what the attempt did.</summary>
    /// <param name="job">The job this attempt holds.</param>
    /// <param name="stoppingToken">Stops the work because the host is stopping; the job is then released rather than failed.</param>
    /// <returns>What the attempt did, in terms that carry no mail content.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="job" /> is <see langword="null" />.</exception>
    Task<JobExecutionResult> RunAsync(LeasedJob job, CancellationToken stoppingToken);
}
