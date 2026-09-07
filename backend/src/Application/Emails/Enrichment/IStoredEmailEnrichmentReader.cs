// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.Enrichment;

/// <summary>Reads what was derived about the emails one page names.</summary>
/// <remarks>
/// <para>
/// The port is read-only and joins no transaction, per
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0001-application-owned-repositories-for-persistence-ports.md">ADR 0001</see>.
/// </para>
/// <para>
/// A second read beside the timeline rather than a join onto it, for the reason the preview is one: the marks live in
/// tables of their own, they are sparse — a deployment with enrichment off has none at all — and reaching them by
/// identity keeps the timeline query the bounded projection it was. Nothing here reads a message body, a search
/// document, or raw MIME; a mark and its evidence are what the derivation already wrote down.
/// </para>
/// </remarks>
public interface IStoredEmailEnrichmentReader
{
    /// <summary>Reads what was derived about each named email.</summary>
    /// <param name="storedEmailIds">The emails to read enrichment for, as one page named them.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>
    /// The enrichment of each email that has one, keyed by identity. An email no derivation has reached is absent, which
    /// is what a client draws as not derived yet; an email whose derivation found nothing to say is present with no
    /// marks.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="storedEmailIds" /> is <see langword="null" />.</exception>
    /// <remarks>The order of the answer means nothing; the caller holds the page whose order does.</remarks>
    Task<IReadOnlyDictionary<StoredEmailId, EmailEnrichment>> ReadEnrichmentsAsync(
        IReadOnlyList<StoredEmailId> storedEmailIds,
        CancellationToken cancellationToken);
}
