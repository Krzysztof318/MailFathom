// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;

namespace MailFathom.Infrastructure.Persistence.Jobs;

/// <summary>Composes the insert that either creates a job or leaves the one already carrying its identity alone.</summary>
/// <remarks>
/// <para>
/// Written rather than composed, and beside the claim for the same reason: the guarantee is the statement's. The
/// conflict target names the columns the unique index is built on, so a second caller asking for the same execution is
/// refused by the database rather than by a check the application made between reading and writing — and a losing
/// insert writes nothing at all, leaving the existing row's state, attempts, and lease exactly as they were.
/// </para>
/// <para>
/// It is a type of its own so the statement can be read and asserted without a database. A conflict target naming the
/// wrong columns, or an insert that updated the existing row instead of standing down, would both pass a test that only
/// exercised the port.
/// </para>
/// <para>
/// Every value is a parameter. The identifiers are quoted because EF Core names the columns after the properties, which
/// PostgreSQL would otherwise fold to lower case and fail to find, and the payload is cast rather than parameterized as
/// <c>jsonb</c> because the document reaches the statement as text.
/// </para>
/// </remarks>
internal static class JobEnqueueStatement
{
    /// <summary>Composes the statement that writes one job unless its identity is already taken.</summary>
    /// <param name="jobId">The identifier a created job takes.</param>
    /// <param name="request">The execution to enqueue.</param>
    /// <param name="payload">The serialized document describing what the work points at.</param>
    /// <param name="enqueuedAt">The instant the job is written at, and its available instant unless the request names one.</param>
    /// <returns>The statement, whose one row is the identifier of a created job and which returns none when the identity was taken.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request" /> is <see langword="null" />.</exception>
    internal static FormattableString Compose(
        Guid jobId,
        JobEnqueueRequest request,
        string payload,
        DateTimeOffset enqueuedAt)
    {
        ArgumentNullException.ThrowIfNull(request);

        var jobTypeName = request.JobType.Name;
        var idempotencyKey = request.Key.Value;
        var accountId = request.AccountId?.Value;
        var availableAt = request.AvailableAt ?? enqueuedAt;
        var pending = nameof(JobState.Pending);

        return $"""
                INSERT INTO jobs (
                    "Id", "JobType", "IdempotencyKey", "Payload", "MailboxAccountId",
                    "State", "AvailableAt", "EnqueuedAt", "StateChangedAt", "AttemptCount")
                VALUES (
                    {jobId}, {jobTypeName}, {idempotencyKey}, CAST({payload} AS jsonb), {accountId},
                    {pending}, {availableAt}, {enqueuedAt}, {enqueuedAt}, 0)
                ON CONFLICT ("JobType", "IdempotencyKey") DO NOTHING
                RETURNING "Id" AS "Value"
                """;
    }
}
