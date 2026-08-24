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
/// It is also where a job's turn is decided, which is the whole of what makes the claim fair across owners. Deciding it
/// here rather than in the claim is what keeps the claim one indexed statement: the order is a column by the time the
/// queue is drained, so no worker has to rank a backlog to find out whose turn it is. The cost is one read of where the
/// owner's waiting work has reached, on the enqueue rather than on the hot path.
/// </para>
/// <para>
/// Two enqueues for one owner arriving together both read the same latest turn and both take the one after it, so an
/// owner occasionally holds two jobs at a single turn. That is the shape the queue-depth bound already has and is
/// answered the same way: what fairness owes is a limit on how far a backlog may run ahead of everybody else rather
/// than an invariant, and a turn shared by as many jobs as raced for it costs nothing that serializing every enqueue
/// behind a lock would not cost far more of. The identifier breaks the tie, so the order stays total and stays the
/// order the two were written in.
/// </para>
/// <para>
/// Every value is a parameter. The identifiers are quoted because EF Core names the columns after the properties, which
/// PostgreSQL would otherwise fold to lower case and fail to find, and the payload is cast rather than parameterized as
/// <c>jsonb</c> because the document reaches the statement as text.
/// </para>
/// </remarks>
internal static class JobEnqueueStatement
{
    /// <summary>How far past its owner's latest waiting turn a newly enqueued job is placed.</summary>
    /// <remarks>
    /// <para>
    /// The rate at which one owner may claim ground ahead of the clock: a second of turn per job. An owner enqueuing
    /// nothing sits on the instant its work becomes available, so a deployment serving one owner claims in the order it
    /// always did, and an owner enqueuing a thousand jobs at once holds turns spread over the next thousand seconds
    /// rather than a thousand turns at the same instant. That is what lets another owner's due job, whose turn is the
    /// instant it arrived, overtake the part of the backlog whose turn has not come.
    /// </para>
    /// <para>
    /// A second rather than a tuned figure, because what the spacing has to be smaller than is how fast the deployment
    /// drains one owner's work, and every deployment that keeps up at all drains more than one job per second per
    /// active owner. Larger would interleave more coarsely without bounding anything further; smaller would let a
    /// backlog claim more of the clock than the workers can serve, which is the FIFO behaviour this replaces.
    /// </para>
    /// </remarks>
    internal static readonly TimeSpan TurnSpacing = TimeSpan.FromSeconds(1);

    /// <summary>Composes the statement that writes one job unless its identity is already taken.</summary>
    /// <param name="jobId">The identifier a created job takes.</param>
    /// <param name="request">The execution to enqueue.</param>
    /// <param name="payload">The serialized document describing what the work points at.</param>
    /// <param name="enqueuedAt">The instant the job is written at, and its available instant unless the request names one.</param>
    /// <param name="enqueuedTrace">The trace the enqueue is happening inside, or <see langword="null" /> when none is being recorded.</param>
    /// <returns>The statement, whose one row is the identifier of a created job and which returns none when the identity was taken.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The trace is written by the insert and by nothing else. A losing insert leaves the row that already exists
    /// alone, including its trace, which is the right answer rather than a limitation: the job that will run is the one
    /// that was enqueued first, and pointing its attempt at a later caller would name a cause that produced nothing.
    /// </remarks>
    internal static FormattableString Compose(
        Guid jobId,
        JobEnqueueRequest request,
        string payload,
        DateTimeOffset enqueuedAt,
        JobTraceContext? enqueuedTrace)
    {
        ArgumentNullException.ThrowIfNull(request);

        var jobTypeName = request.JobType.Name;
        var idempotencyKey = request.Key.Value;
        var accountId = request.AccountId?.Value;
        var availableAt = request.AvailableAt ?? enqueuedAt;
        var pending = nameof(JobState.Pending);
        var claimableStates = new[] { pending, nameof(JobState.Claimed) };
        var turnSpacing = TurnSpacing;
        var traceParent = enqueuedTrace?.TraceParent;
        var traceState = enqueuedTrace?.TraceState;

        // Written as a select over one composed row rather than as a values list, because the turn is decided from what
        // the same owner already has waiting and a values list has nothing to correlate that against. The row is always
        // produced: the owner is read as a scalar subquery, which answers null for work that belongs to no account
        // rather than answering no row at all, so an ownerless job is still inserted and still claimable.
        //
        // The latest turn is taken one mailbox at a time and the largest of those kept, rather than as one maximum over
        // the owner's work joined together. The two answer the same value and only the first is a read of the index: an
        // aggregate over a join is computed from the whole join, so against a backlog of two hundred thousand rows
        // PostgreSQL scans the queue for it — measured at 6742 buffers against 10 for the form below, which walks the
        // claim index backwards from each mailbox and stops at the first row.
        return $"""
                INSERT INTO jobs (
                    "Id", "JobType", "IdempotencyKey", "Payload", "MailboxAccountId",
                    "State", "AvailableAt", "TurnAt", "EnqueuedAt", "StateChangedAt", "AttemptCount",
                    "EnqueuedTraceParent", "EnqueuedTraceState")
                SELECT
                    {jobId}, {jobTypeName}, {idempotencyKey}, CAST({payload} AS jsonb), {accountId},
                    {pending}, {availableAt},
                    GREATEST(
                        {availableAt},
                        (SELECT MAX(mailbox."TurnAt")
                         FROM mailbox_accounts AS owned
                         CROSS JOIN LATERAL (
                             SELECT waiting."TurnAt"
                             FROM jobs AS waiting
                             WHERE waiting."State" = ANY({claimableStates})
                               AND waiting."MailboxAccountId" = owned."Id"
                             ORDER BY waiting."TurnAt" DESC
                             LIMIT 1) AS mailbox
                         WHERE owned."OwnerId" = owning."OwnerId") + {turnSpacing}),
                    {enqueuedAt}, {enqueuedAt}, 0,
                    {traceParent}, {traceState}
                FROM (
                    SELECT (SELECT account."OwnerId" FROM mailbox_accounts AS account WHERE account."Id" = {accountId})
                        AS "OwnerId") AS owning
                ON CONFLICT ("JobType", "IdempotencyKey") DO NOTHING
                RETURNING "Id" AS "Value"
                """;
    }
}
