// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;

namespace MailFathom.Infrastructure.Persistence.Jobs;

/// <summary>Composes the one statement a claim is.</summary>
/// <remarks>
/// <para>
/// Written rather than composed through the query provider, because the claim is the mechanism rather than a query:
/// <c>FOR UPDATE SKIP LOCKED</c> is what makes two workers claiming at the same moment take different jobs instead of
/// waiting on each other, and no LINQ operator expresses it. Selecting and stamping in one statement is what makes the
/// claim atomic — a read followed by a write would leave the window in which both workers saw the same row.
/// </para>
/// <para>
/// It is a type of its own so the statement can be read and asserted without a database. Losing the locking clause,
/// the type filter, or the bound would each fail silently at run time — as duplicated work, as a job taken by a replica
/// that cannot run it, or as a claim that drains the queue — so the statement is verified as text.
/// </para>
/// <para>
/// Two predicates make a job due, and the second is the crash recovery: a pending job whose available instant has
/// passed, and a claimed job whose lease has run out. Nothing has to be told that a process died, because an expired
/// lease is indistinguishable from one whose holder is gone.
/// </para>
/// <para>
/// The predicate opens by naming the two states a claim can take, which the two due predicates below it already imply.
/// It is there so PostgreSQL can prove the partial claim index applies: an implication it would otherwise have to
/// derive through a disjunction, which its prover does not attempt, and a queue whose only volume query fell back to a
/// sequential scan would slow down with its own history. It is written as the claimable states rather than as the
/// terminal ones so that the index filter and this predicate stay one statement of the same fact, whatever terminal
/// states the queue later gains.
/// </para>
/// <para>
/// The order is the turn each job holds rather than the instant it became available, which is what makes the claim fair
/// across owners: the enqueue placed each job one spacing past the latest turn its own owner already had waiting, so
/// draining the queue in turn order interleaves owners instead of working through whoever queued first. Nothing here
/// ranks anything — the ordering is one column of one index — because a claim that had to rank a backlog to find out
/// whose turn it was could no longer be one statement under <c>FOR UPDATE SKIP LOCKED</c>, which PostgreSQL refuses to
/// combine with a window function at all.
/// </para>
/// <para>
/// Every value is a parameter. The identifiers are quoted because EF Core names the columns after the properties, which
/// PostgreSQL would otherwise fold to lower case and fail to find.
/// </para>
/// </remarks>
internal static class JobClaimStatement
{
    /// <summary>Composes the statement that takes and stamps a batch of due jobs.</summary>
    /// <param name="request">Which types this process runs, how many to take, and under what lease.</param>
    /// <param name="claimedAt">The instant the claim is judged and stamped at.</param>
    /// <returns>The statement, whose rows are the identifiers of the jobs this claim took.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request" /> is <see langword="null" />.</exception>
    internal static FormattableString Compose(JobClaimRequest request, DateTimeOffset claimedAt)
    {
        ArgumentNullException.ThrowIfNull(request);

        var handledTypeNames = request.HandledTypes.Select(handledType => handledType.Name).ToArray();
        var pending = nameof(JobState.Pending);
        var claimed = nameof(JobState.Claimed);
        var claimableStates = new[] { pending, claimed };
        var leaseOwner = request.Owner.Value;
        var leaseExpiresAt = claimedAt + request.LeaseDuration;
        var batchSize = request.BatchSize;

        // The locking clause follows LIMIT, which is where the standard puts it. The order matters to the plan as well
        // as to the grammar: the limit counts the rows that survived locking, so a batch of one against a row another
        // worker holds takes the next free row rather than coming back empty.
        return $"""
                WITH due AS (
                    SELECT candidate."Id"
                    FROM jobs AS candidate
                    WHERE candidate."State" = ANY({claimableStates})
                      AND candidate."JobType" = ANY({handledTypeNames})
                      AND ((candidate."State" = {pending} AND candidate."AvailableAt" <= {claimedAt})
                        OR (candidate."State" = {claimed} AND candidate."LeaseExpiresAt" <= {claimedAt}))
                    ORDER BY candidate."TurnAt", candidate."Id"
                    LIMIT {batchSize}
                    FOR UPDATE SKIP LOCKED
                )
                UPDATE jobs AS job
                SET "State" = {claimed},
                    "LeaseOwner" = {leaseOwner},
                    "LeaseExpiresAt" = {leaseExpiresAt},
                    "AttemptCount" = job."AttemptCount" + 1,
                    "StateChangedAt" = {claimedAt}
                FROM due
                WHERE job."Id" = due."Id"
                RETURNING job."Id" AS "Value"
                """;
    }
}
