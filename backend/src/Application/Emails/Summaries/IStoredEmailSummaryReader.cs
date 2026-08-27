// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.Summaries;

/// <summary>Reads locally stored email summaries by their stable local identity.</summary>
/// <remarks>
/// <para>
/// The port is separate from <see cref="IStoredEmailTimelineReader" /> because the two operations are asked different
/// questions and are indexed differently: one walks a filtered timeline in keyset order, the other looks rows up by
/// primary key. Folding the lookup into the paging contract would mean expressing an identity as a filter, and every
/// caller of the lookup would then have to be trusted not to page.
/// </para>
/// <para>
/// The two methods are one question asked about one row and about several: a caller that already holds the identities
/// of a page — a conversation's, in the order that conversation has — reads them together rather than one round trip at
/// a time, and the set it names is bounded by whatever produced it.
/// </para>
/// <para>
/// It returns the same <see cref="EmailSummary" /> projection a listing does, and for the same reason: the query names
/// the columns a summary publishes and cannot reach the stored raw MIME, which is a privacy control before it is a
/// performance one. The content itself is read through its own port by a caller that has established it may.
/// </para>
/// </remarks>
public interface IStoredEmailSummaryReader
{
    /// <summary>Finds one stored email.</summary>
    /// <param name="storedEmailId">The email's stable local identity.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The summary, or <see langword="null" /> when the local mailbox copy holds no such email.</returns>
    /// <remarks>
    /// An unknown identifier is an ordinary answer rather than a failure here, so the caller decides what absence
    /// means. It is the caller, not the query, that knows whether a missing row is a refusal to publish or a state a
    /// walk simply steps over.
    /// </remarks>
    Task<EmailSummary?> FindAsync(StoredEmailId storedEmailId, CancellationToken cancellationToken);

    /// <summary>Reads the summaries of the emails one page names.</summary>
    /// <param name="storedEmailIds">The emails to read, as one page named them.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The summary of each email the local mailbox copy still holds, keyed by identity.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="storedEmailIds" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// An identity the copy no longer holds is absent from the answer rather than reported, on the same terms
    /// <see cref="FindAsync" /> answers with nothing: what a gap means belongs to the caller, which knows whether a
    /// message that disappeared between two reads is a row to drop or a refusal. The order of the answer means nothing;
    /// the caller holds the page whose order does.
    /// </remarks>
    Task<IReadOnlyDictionary<StoredEmailId, EmailSummary>> ReadSummariesAsync(
        IReadOnlyList<StoredEmailId> storedEmailIds,
        CancellationToken cancellationToken);
}
