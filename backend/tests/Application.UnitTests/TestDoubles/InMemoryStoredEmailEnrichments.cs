// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Enrichment;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>An in-memory stand-in for the enrichment reader, holding what a test arranged for each email.</summary>
/// <remarks>
/// It answers only for the emails it was given, exactly as the port promises: a message no derivation has reached is
/// absent from the answer, while one whose derivation found nothing to say is present with no marks. What each call
/// asked for is recorded, because a page that read enrichment for rows it did not return would be reading about a
/// message the caller never sees.
/// </remarks>
internal sealed class InMemoryStoredEmailEnrichments : IStoredEmailEnrichmentReader
{
    private readonly Dictionary<StoredEmailId, EmailEnrichment> enrichments = [];

    private readonly List<IReadOnlyList<StoredEmailId>> calls = [];

    /// <summary>Gets which emails each call to the port asked about, in order.</summary>
    public IReadOnlyList<IReadOnlyList<StoredEmailId>> Calls => this.calls;

    /// <summary>Records what a derivation concluded about one email.</summary>
    /// <param name="storedEmailId">The email the derivation is about.</param>
    /// <param name="marks">What it concluded, which may be empty.</param>
    /// <returns>This reader, so arrangement reads as one statement.</returns>
    public InMemoryStoredEmailEnrichments With(
        StoredEmailId storedEmailId,
        params EmailEnrichmentMark[] marks)
    {
        this.enrichments[storedEmailId] = new EmailEnrichment(
            storedEmailId,
            marks,
            DateTimeOffset.UnixEpoch);

        return this;
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<StoredEmailId, EmailEnrichment>> ReadEnrichmentsAsync(
        IReadOnlyList<StoredEmailId> storedEmailIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(storedEmailIds);
        cancellationToken.ThrowIfCancellationRequested();

        this.calls.Add([.. storedEmailIds]);

        IReadOnlyDictionary<StoredEmailId, EmailEnrichment> found = storedEmailIds
            .Where(this.enrichments.ContainsKey)
            .ToDictionary(storedEmailId => storedEmailId, storedEmailId => this.enrichments[storedEmailId]);

        return Task.FromResult(found);
    }
}
