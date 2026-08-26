// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Answering.Audit;

namespace MailFathom.Application.Retrieval.AskMail.Audit;

/// <summary>Keeps the record of the questions this deployment answered from each account's mailbox.</summary>
/// <remarks>
/// The record is append-only through this port. Nothing amends an entry, because an entry states a run that has already
/// ended; the only writes besides an append are the erasure the account's retention calls for, which removes whole
/// entries, and the deletion of an email, which reaches the entries naming it through that email's own deletion path
/// rather than through anything here.
/// </remarks>
public interface IMailAnsweringAuditEntryStore
{
    /// <summary>Appends one entry to the record.</summary>
    /// <param name="session">The session the append is staged in.</param>
    /// <param name="entry">The entry to keep.</param>
    /// <param name="cancellationToken">Cancels the staging.</param>
    /// <returns>A task that completes once the append is staged.</returns>
    /// <remarks>
    /// An entry whose run and account are already in the record is left alone rather than duplicated, so a retried
    /// append after a commit whose answer was lost keeps one entry per run per account.
    /// </remarks>
    Task AppendAsync(
        IPersistenceSession session,
        MailAnsweringAuditEntry entry,
        CancellationToken cancellationToken);

    /// <summary>Reads one bounded page of an account's record, newest first.</summary>
    /// <param name="query">The account, the filters, and the boundary the page continues from.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page, and the cursor the following page continues from where one exists.</returns>
    Task<MailAnsweringAuditPage> ReadPageAsync(
        MailAnsweringAuditQuery query,
        CancellationToken cancellationToken);

    /// <summary>Erases up to a bounded number of one account's entries that ended before a given instant.</summary>
    /// <param name="account">The account whose record is aged.</param>
    /// <param name="completedBefore">The instant entries must have ended before to be erased.</param>
    /// <param name="limit">The greatest number of entries one call may erase.</param>
    /// <param name="cancellationToken">Cancels the erasure.</param>
    /// <returns>How many entries were erased, which reaching <paramref name="limit" /> means more remain.</returns>
    /// <remarks>
    /// <para>
    /// It joins no session, because it is a set-based delete rather than a change to state a caller is composing. It is
    /// idempotent: running it twice over the same window erases nothing the second time. The emails an erased entry
    /// named go with it, because they hang on the entry.
    /// </para>
    /// <para>
    /// The bound is what keeps an operator shortening a long retention from turning one pass into a delete that locks
    /// the table against every append behind it. What is left over is erased by the next pass, oldest first.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="limit" /> is not positive.</exception>
    Task<int> EraseCompletedBeforeAsync(
        MailAccountIdentity account,
        DateTimeOffset completedBefore,
        int limit,
        CancellationToken cancellationToken);
}
