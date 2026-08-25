// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;

namespace MailFathom.Application.Jobs;

/// <summary>Indicates that a job could not enqueue the segment carrying the rest of its own work.</summary>
/// <remarks>
/// <para>
/// A handler that walks more than one attempt's worth of work hands the remainder to a segment of its own, and that
/// hand-on is the only record of where the walk had reached. A queue at its configured depth answers the enqueue with
/// backpressure rather than a job, so an attempt that read the refusal and returned would be recorded as having
/// succeeded while the rest of its work existed nowhere.
/// </para>
/// <para>
/// It is raised rather than reported as a result because the caller the outcome belongs to is the executor rather than
/// the handler: what the refusal asks for is the same segment attempted again once the queue has drained, which is what
/// ending the attempt produces. It is classified as transient for that reason, and the attempt budget is what stops a
/// queue that never drains from being retried forever.
/// </para>
/// </remarks>
public sealed class JobHandOnRefusedAtCapacityException : MailFathomException
{
    /// <summary>Initializes a new refusal naming the job type whose queue was at its depth.</summary>
    /// <param name="jobType">The job type the refused segment would have been enqueued under.</param>
    public JobHandOnRefusedAtCapacityException(JobType jobType)
        : base($"A '{jobType}' job could not enqueue the segment carrying the rest of its work, " +
            "because that job type is at its configured queue depth.")
    {
        this.JobType = jobType;
    }

    /// <summary>Gets the job type the refused segment would have been enqueued under.</summary>
    public JobType JobType { get; }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.JobHandOnRefusedAtCapacity;
}
