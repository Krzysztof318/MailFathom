// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Jobs.Execution;

/// <summary>States how one attempt at one job ended.</summary>
/// <remarks>
/// The three ways an attempt is stopped from outside are separate members rather than one cancellation, because they
/// mean opposite things to an operator: a shutdown is a deployment and costs the job nothing, a timeout is the job
/// exceeding what it was allowed, and a lost lease means another attempt already owns the work.
/// </remarks>
public enum JobExecutionOutcome
{
    /// <summary>The handler finished and the job is recorded as done.</summary>
    Succeeded = 0,

    /// <summary>The handler raised, and the failure is recorded against the job.</summary>
    HandlerFailed = 1,

    /// <summary>No handler is registered for the job's type, so the job is recorded as failed rather than claimed again.</summary>
    HandlerMissing = 2,

    /// <summary>The execution exceeded its configured timeout and was cancelled.</summary>
    TimedOut = 3,

    /// <summary>The host is stopping, so the lease was given back and the job is claimable immediately.</summary>
    ReleasedForShutdown = 4,

    /// <summary>The lease moved to another attempt, so this one wrote nothing about the job.</summary>
    LeaseLost = 5,
}
