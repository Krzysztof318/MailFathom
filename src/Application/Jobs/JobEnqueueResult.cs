// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Jobs;

/// <summary>Names the job an enqueue asked for, and says what the queue did with the request.</summary>
/// <remarks>
/// A refused request names no job, because a refusal wrote nothing and found nothing: there is no row for it to point
/// at. That is the one meaning an absent identifier carries here, and <see cref="Outcome" /> states it, so a caller
/// reads the outcome and never infers anything from the absence itself.
/// </remarks>
public sealed record JobEnqueueResult
{
    private JobEnqueueResult(JobEnqueueOutcome outcome, JobId? jobId)
    {
        this.Outcome = outcome;
        this.JobId = jobId;
    }

    /// <summary>Gets what the queue did with the request.</summary>
    public JobEnqueueOutcome Outcome { get; }

    /// <summary>Gets the job carrying the requested type and key, or <see langword="null" /> when the request was refused.</summary>
    public JobId? JobId { get; }

    /// <summary>Reports that this call wrote the job.</summary>
    /// <param name="jobId">The job this call created.</param>
    /// <returns>The result of a created execution.</returns>
    public static JobEnqueueResult Created(JobId jobId) => new(JobEnqueueOutcome.Created, jobId);

    /// <summary>Reports that a job with this type and key was already there, in whatever state.</summary>
    /// <param name="jobId">The job that already carried the requested identity.</param>
    /// <returns>The result of an execution that needed no second row.</returns>
    public static JobEnqueueResult AlreadyEnqueued(JobId jobId) => new(JobEnqueueOutcome.AlreadyEnqueued, jobId);

    /// <summary>Reports that this job type already had as much waiting as the queue accepts.</summary>
    /// <returns>The backpressure the caller acts on.</returns>
    public static JobEnqueueResult RefusedAtCapacity() => new(JobEnqueueOutcome.RefusedAtCapacity, jobId: null);
}
