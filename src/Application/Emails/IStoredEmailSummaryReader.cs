// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails;

/// <summary>Reads one locally stored email's summary by its stable local identity.</summary>
/// <remarks>
/// <para>
/// The port is separate from <see cref="IStoredEmailTimelineReader" /> because the two operations are asked different
/// questions and are indexed differently: one walks a filtered timeline in keyset order, the other looks one row up by
/// primary key. Folding the lookup into the paging contract would mean expressing an identity as a filter, and every
/// caller of the lookup would then have to be trusted not to page.
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
}
