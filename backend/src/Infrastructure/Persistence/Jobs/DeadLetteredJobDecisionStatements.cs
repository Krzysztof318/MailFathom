// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;

namespace MailFathom.Infrastructure.Persistence.Jobs;

/// <summary>Composes the two statements an operator's decision about one dead-lettered job is.</summary>
/// <remarks>
/// <para>
/// Both are conditional updates rather than a read followed by a write, and the condition is the safety property: a job
/// that is no longer dead-lettered has been decided about by somebody else, and the update that finds it writes nothing
/// instead of taking a running job away from the worker holding it. The row count is what says which happened.
/// </para>
/// <para>
/// They are a type of their own so the statements can be read and asserted without a database. Losing the state
/// predicate would fail silently at run time — as a claimed job reset under its holder, or a succeeded one dropped — so
/// the statements are verified as text.
/// </para>
/// <para>
/// Every value is a parameter. The identifiers are quoted because EF Core names the columns after the properties, which
/// PostgreSQL would otherwise fold to lower case and fail to find.
/// </para>
/// </remarks>
internal static class DeadLetteredJobDecisionStatements
{
    /// <summary>Composes the statement that offers one dead-lettered job to the queue again.</summary>
    /// <param name="jobId">The job the decision is about.</param>
    /// <param name="retriedAt">The instant the decision is taken and stamped at.</param>
    /// <returns>The statement, whose row count is one when the decision took effect.</returns>
    /// <remarks>
    /// <para>
    /// The attempt count goes back to nothing and the available instant to now, so the job is claimable by the next pass
    /// rather than after whatever backoff the failed attempt had written. The failure columns are left where they are:
    /// the row goes on saying why it stopped until an attempt replaces the answer.
    /// </para>
    /// <para>
    /// The turn moves to now with it, because the one the row is carrying belongs to a queue that no longer exists. A
    /// job that stopped last week holds a turn from last week, and a decision to run it again would otherwise put it —
    /// and every other dead letter an operator returned in the same sitting — in front of every owner's due work. It
    /// keeps a later turn where it somehow has one, which is the same rule a scheduled retry follows.
    /// </para>
    /// </remarks>
    internal static FormattableString ComposeRetry(Guid jobId, DateTimeOffset retriedAt)
    {
        var deadLettered = nameof(JobState.DeadLettered);
        var pending = nameof(JobState.Pending);

        return $"""
                UPDATE jobs
                SET "State" = {pending},
                    "AvailableAt" = {retriedAt},
                    "TurnAt" = GREATEST("TurnAt", {retriedAt}),
                    "AttemptCount" = 0,
                    "StateChangedAt" = {retriedAt}
                WHERE "Id" = {jobId}
                  AND "State" = {deadLettered}
                """;
    }

    /// <summary>Composes the statement that closes one dead-lettered job without running it again.</summary>
    /// <param name="jobId">The job the decision is about.</param>
    /// <param name="droppedAt">The instant the decision is taken and stamped at.</param>
    /// <returns>The statement, whose row count is one when the decision took effect.</returns>
    /// <remarks>
    /// The failure columns are left where they are for the reason the retry leaves them: what stopped the job is the
    /// only account of it there is, and dropping it is a decision about that account rather than a replacement for it.
    /// </remarks>
    internal static FormattableString ComposeDrop(Guid jobId, DateTimeOffset droppedAt)
    {
        var deadLettered = nameof(JobState.DeadLettered);
        var dropped = nameof(JobState.Dropped);

        return $"""
                UPDATE jobs
                SET "State" = {dropped},
                    "StateChangedAt" = {droppedAt}
                WHERE "Id" = {jobId}
                  AND "State" = {deadLettered}
                """;
    }
}
