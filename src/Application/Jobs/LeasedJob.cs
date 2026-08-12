// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Jobs;

/// <summary>One job a claim handed to one attempt, with the lease that attempt holds it under.</summary>
/// <remarks>
/// The attempt number counts from one and includes this attempt, so a job being run for the first time reports one.
/// It is counted by the claim rather than by whatever runs the work, because a process that dies mid-execution never
/// reaches a line that would have counted it and its attempt would otherwise be invisible.
/// </remarks>
/// <param name="JobId">The job this attempt holds.</param>
/// <param name="JobType">The kind of work, which is also the contract its payload was read back as.</param>
/// <param name="Key">The identity the execution was enqueued under.</param>
/// <param name="Payload">The references the work is described by.</param>
/// <param name="AccountId">The account the work belongs to, or <see langword="null" /> when it belongs to none.</param>
/// <param name="AttemptCount">Which attempt this is, counting from one.</param>
/// <param name="Lease">The lease this attempt holds the job under.</param>
public sealed record LeasedJob(
    JobId JobId,
    JobType JobType,
    JobIdempotencyKey Key,
    IJobPayload Payload,
    MailAccountId? AccountId,
    int AttemptCount,
    JobLease Lease);
