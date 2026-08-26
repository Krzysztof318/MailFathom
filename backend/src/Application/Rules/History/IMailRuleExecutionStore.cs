// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Rules.History;

/// <summary>Keeps the record of what each rule concluded about each email, and what those conclusions asked for.</summary>
/// <remarks>
/// Append-only through this port. Nothing amends an execution, because an execution states a reading that has already
/// happened; the only writes besides an append are the erasure the retention window calls for, and the deletion of an
/// email, which reaches the executions naming it through that email's own deletion path rather than through anything
/// here.
/// </remarks>
public interface IMailRuleExecutionStore
{
    /// <summary>Appends one batch's executions to the history.</summary>
    /// <param name="session">The session the append is staged in, which is the one the batch's evaluations commit through.</param>
    /// <param name="executions">The executions to keep, in the order the pass produced them.</param>
    /// <param name="cancellationToken">Cancels the staging.</param>
    /// <returns>A task that completes once the append is staged in the session.</returns>
    /// <remarks>
    /// Staged in the caller's session so a batch's evaluations, the requests they produced, and the record of why are one
    /// commit. A rolled-back batch therefore leaves no execution behind for work that was never recorded as done, and the
    /// re-evaluation that follows records its own.
    /// </remarks>
    Task AppendAsync(
        IPersistenceSession session,
        IReadOnlyList<MailRuleExecution> executions,
        CancellationToken cancellationToken);

    /// <summary>Reads one bounded page of an account's history, newest first.</summary>
    /// <param name="query">The account, the filters, and the boundary the page continues from.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page, and the cursor the following page continues from where one exists.</returns>
    Task<MailRuleExecutionPage> ReadPageAsync(
        MailRuleExecutionQuery query,
        CancellationToken cancellationToken);

    /// <summary>Erases up to a bounded number of one account's executions recorded before a given instant.</summary>
    /// <param name="account">The account whose history is aged.</param>
    /// <param name="evaluatedBefore">The instant an execution must have been recorded before to be erased.</param>
    /// <param name="limit">The greatest number of executions one call may erase.</param>
    /// <param name="cancellationToken">Cancels the erasure.</param>
    /// <returns>How many executions were erased, which reaching <paramref name="limit" /> means more remain.</returns>
    /// <remarks>
    /// <para>
    /// It joins no session, because it is a set-based delete rather than a change to state a caller is composing. It is
    /// idempotent: running it twice over the same window erases nothing the second time. The actions an erased execution
    /// recorded go with it, because they hang on the execution.
    /// </para>
    /// <para>
    /// The bound is what keeps an operator shortening a long retention from turning one pass into a delete that locks
    /// the table against every append behind it. What is left over is erased by the next pass, oldest first.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="limit" /> is not positive.</exception>
    Task<int> EraseEvaluatedBeforeAsync(
        MailAccountIdentity account,
        DateTimeOffset evaluatedBefore,
        int limit,
        CancellationToken cancellationToken);
}
