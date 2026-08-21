// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Infrastructure.Persistence.Entities;

namespace MailFathom.Infrastructure.Persistence.Jobs;

/// <summary>Composes the query an enqueue asks whether one type's queue is already as deep as the deployment accepts.</summary>
/// <remarks>
/// <para>
/// A type of its own for the reason the enqueue and claim statements are: the question is whether the depth has been
/// reached, and both halves of that are easy to get wrong in a way only the database would notice. Counting a state that
/// is not waiting would make a draining instance look like a filling one, and counting the whole queue rather than
/// stopping at the bound would make every enqueue pay for the size of a backlog it only needs to compare against.
/// </para>
/// <para>
/// The bound is applied as a ceiling on the rows read rather than as a predicate, so the answer is <em>at least this
/// many</em> rather than <em>how many</em>. That is the whole of what the caller compares, and it keeps the cost the
/// same on a queue of a thousand and on a queue of a million.
/// </para>
/// </remarks>
internal static class JobQueueDepthQuery
{
    /// <summary>Composes the query over the jobs of one type that are waiting, reading no more than the bound.</summary>
    /// <param name="jobs">The job rows to ask, ordinarily read without tracking.</param>
    /// <param name="jobTypeName">The name of the type whose queue is being measured.</param>
    /// <param name="depthBound">The depth to compare against, which is also the greatest number of rows read.</param>
    /// <returns>The query whose count reaches the bound exactly when the queue is full.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="jobs" /> is <see langword="null" />.</exception>
    internal static IQueryable<JobEntity> Compose(IQueryable<JobEntity> jobs, string jobTypeName, int depthBound)
    {
        ArgumentNullException.ThrowIfNull(jobs);

        return jobs
            .Where(job => job.JobType == jobTypeName && job.State == JobState.Pending)
            .Take(depthBound);
    }
}
