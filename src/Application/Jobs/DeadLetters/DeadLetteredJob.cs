// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Jobs.DeadLetters;

/// <summary>One job nothing will attempt again, as the operator deciding what to do about it reads it.</summary>
/// <remarks>
/// <para>
/// Everything here is either MailFathom's own name for something or a count: the type's name, the identity the enqueuer
/// composed out of aliases and identifiers, the account alias, how many attempts were spent, and the operator-safe
/// record of what ended it. The payload is deliberately absent — it names a message occurrence, and an operator
/// deciding whether to run the work again does not need to be told which message while deciding it.
/// </para>
/// <para>
/// The two instants answer different questions. <see cref="EnqueuedAt" /> says how long the work has been outstanding,
/// which is what makes a backlog of dead letters legible as one; <see cref="DeadLetteredAt" /> says when it stopped,
/// which is what a failure is correlated against the deployment change that caused it by.
/// </para>
/// </remarks>
/// <param name="JobId">The job's own identifier, which is what a retry or a drop names it by.</param>
/// <param name="JobType">The kind of work.</param>
/// <param name="Key">The identity the enqueuer composed, which the row keeps and a retry runs under unchanged.</param>
/// <param name="AccountId">The account the work belongs to, and <see langword="null" /> when it belongs to none.</param>
/// <param name="AttemptCount">How many attempts were handed out before the job stopped.</param>
/// <param name="EnqueuedAt">When the work was first enqueued.</param>
/// <param name="DeadLetteredAt">When the job reached the state it is in now.</param>
public sealed record DeadLetteredJob(
    JobId JobId,
    JobType JobType,
    JobIdempotencyKey Key,
    MailAccountId? AccountId,
    int AttemptCount,
    DateTimeOffset EnqueuedAt,
    DateTimeOffset DeadLetteredAt)
{
    /// <summary>Gets the classification and reason of the failure that ended the job, and <see langword="null" /> where the row carries none.</summary>
    /// <remarks>
    /// Every dead letter the executor writes carries one, because the write that produces the state is the write that
    /// records the failure. It is nullable because the two columns are, and a row that somehow reached this state
    /// without one is better reported as a job with no recorded reason than refused on the way to an operator who is
    /// looking at it precisely because something went wrong.
    /// </remarks>
    public JobFailureRecord? LastFailure { get; init; }
}
