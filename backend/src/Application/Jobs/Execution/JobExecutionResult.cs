// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Jobs.Execution;

/// <summary>What one attempt at one job did, in the terms the queue itself is described in.</summary>
/// <remarks>
/// <para>
/// It carries the job's own identity and the type's name and nothing from the work: a payload names a message
/// occurrence and this names the payload's job, so neither a subject, an address, nor any other mail content can reach
/// a log line, a counter, or a span through it. That holds of a failed attempt too, which is why the exception a
/// handler raised stops at the classifier and only the record it produced travels on.
/// </para>
/// <para>
/// The state the record needed is already written by the time a result exists. This is what the caller reports and
/// paces itself by, not a request for the caller to finish the attempt.
/// </para>
/// </remarks>
/// <param name="JobId">The job this attempt held.</param>
/// <param name="JobType">The kind of work, which is the name every report of this result uses.</param>
/// <param name="AttemptCount">Which attempt this was, counting from one.</param>
/// <param name="Outcome">How the attempt ended.</param>
/// <param name="Duration">How long the attempt took, from dispatch to the recorded outcome.</param>
public sealed record JobExecutionResult(
    JobId JobId,
    JobType JobType,
    int AttemptCount,
    JobExecutionOutcome Outcome,
    TimeSpan Duration)
{
    /// <summary>Gets what one failed attempt recorded and what became of the job, and <see langword="null" /> when nothing failed.</summary>
    /// <remarks>
    /// One member rather than several nullable ones, because a classification, a reason, a disposition, and a next
    /// attempt are one answer about one failure: any of them present without the others would describe a state the
    /// queue cannot be in.
    /// </remarks>
    public JobAttemptFailure? AttemptFailure { get; init; }
}
