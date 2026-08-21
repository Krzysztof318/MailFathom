// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Jobs.Execution;

/// <summary>States what the queue did with a job whose attempt failed.</summary>
/// <remarks>
/// It is separate from the outcome because the two answer different questions. The outcome says what happened to the
/// attempt — a handler raised, a timeout expired, no handler was registered — and this says what became of the job,
/// which is what an operator waiting on the work actually needs to know.
/// </remarks>
public enum JobFailureDisposition
{
    /// <summary>The job goes back to the queue and becomes claimable again after a jittered delay.</summary>
    RetryScheduled = 0,

    /// <summary>The job is terminal: nothing claims it again, and it keeps the classification and reason that ended it.</summary>
    DeadLettered = 1,
}
