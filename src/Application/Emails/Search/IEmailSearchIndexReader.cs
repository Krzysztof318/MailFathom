// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.Search;

/// <summary>Reads one bounded, ranked window of locally stored emails out of the lexical index.</summary>
/// <remarks>
/// <para>
/// The port is read-only and joins no transaction, per
/// <see href="../../../docs/decisions/0001-application-owned-repositories-for-persistence-ports.md">ADR 0001</see>: it
/// takes no persistence session, because a read has nothing to participate in.
/// </para>
/// <para>
/// It is separate from <see cref="IStoredEmailTimelineReader" /> because it reads a different table for a different
/// order. The timeline reads mail metadata in a keyset order over a total ordering; this reads the derived search
/// documents in relevance order, which has no total ordering of its own and no cursor into it.
/// </para>
/// <para>
/// Two obligations belong to every implementation rather than to its callers. The query text reaches the database as a
/// parameter and never as concatenated SQL, and the text search configuration is the deployment's validated setting
/// rather than anything the request carried. Both are what keep a caller's operators and metacharacters data instead of
/// syntax, and neither can be checked from the application side, which is why the assertion that proves them lives
/// where the generated command is observable.
/// </para>
/// </remarks>
public interface IEmailSearchIndexReader
{
    /// <summary>Reads the highest-ranked emails matching a query, among those the structured filters select.</summary>
    /// <param name="selection">Which emails are eligible before the text is considered.</param>
    /// <param name="queryText">The validated free text to match against the index.</param>
    /// <param name="snippetBounds">How many extracts one result may carry, and how long each may be.</param>
    /// <param name="limit">The greatest number of ranked results to return, at least one.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>At most <paramref name="limit" /> matches, most relevant first, empty when nothing matched.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="selection" />, <paramref name="queryText" />, or <paramref name="snippetBounds" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="limit" /> is below one.</exception>
    /// <remarks>
    /// <para>
    /// The order is deterministic. Full-text rank alone produces ties — several messages carrying one uncommon word
    /// score identically — so an implementation orders by rank descending and then by
    /// <see cref="EmailTimelinePosition" /> in its newest-first direction, which is total. Two identical requests over an
    /// unchanged index therefore return the same sequence.
    /// </para>
    /// <para>
    /// Matching nothing is an empty result rather than a failure. A search that reported the difference between "no such
    /// mail" and "no such folder" would answer questions about accounts and folders the caller was never told about.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<EmailSearchMatch>> ReadRankedMatchesAsync(
        MailboxEmailSelection selection,
        EmailSearchQueryText queryText,
        EmailSearchSnippetBounds snippetBounds,
        int limit,
        CancellationToken cancellationToken);
}
