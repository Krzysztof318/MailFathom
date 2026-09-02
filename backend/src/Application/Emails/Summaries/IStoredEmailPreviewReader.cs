// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.Summaries;

/// <summary>Reads the opening of the derived text of the emails one page names.</summary>
/// <remarks>
/// <para>
/// The port is read-only and joins no transaction, per
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0001-application-owned-repositories-for-persistence-ports.md">ADR 0001</see>.
/// </para>
/// <para>
/// It is a second read beside the timeline rather than a column on it, because the text a preview is cut from lives in
/// a table of its own — one written by extraction, read until now by search alone, and deliberately absent from every
/// ordinary mailbox query. Reaching it by identity keeps that true: the timeline query stays the projection it was, and the text is
/// fetched for the rows one page actually returned rather than joined to every row a filter considered.
/// </para>
/// <para>
/// What it answers is already bounded. An implementation asks PostgreSQL for at most
/// <see cref="EmailPreview.MaximumCharacters" /> characters of each message, so a body never crosses this boundary at
/// all, and it publishes the trimmed reading of the text rather than the raw one — quoted history and signatures are
/// what a reply's first two hundred characters would otherwise be.
/// </para>
/// </remarks>
public interface IStoredEmailPreviewReader
{
    /// <summary>Reads the bounded opening of each named email's text.</summary>
    /// <param name="storedEmailIds">The emails to read previews for, as one page named them.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>
    /// The preview of each email that has one, keyed by identity. An email whose text has never been extracted, or whose
    /// extraction yielded nothing, is absent rather than present with an empty value.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="storedEmailIds" /> is <see langword="null" />.</exception>
    /// <remarks>The order of the answer means nothing; the caller holds the page whose order does.</remarks>
    Task<IReadOnlyDictionary<StoredEmailId, string>> ReadPreviewsAsync(
        IReadOnlyList<StoredEmailId> storedEmailIds,
        CancellationToken cancellationToken);
}
