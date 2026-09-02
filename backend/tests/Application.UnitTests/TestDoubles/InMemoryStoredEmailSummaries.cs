// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Summaries;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>An in-memory stand-in for the summary reader, holding the rows a test arranged.</summary>
/// <remarks>
/// It answers only for the emails it was given, exactly as the port promises: an identity the copy no longer holds is
/// absent from the answer rather than present as an empty row. What each call asked for is recorded, because a page that
/// read summaries for messages it does not return would be reading mail the caller never sees.
/// </remarks>
internal sealed class InMemoryStoredEmailSummaries : IStoredEmailSummaryReader
{
    private readonly Dictionary<StoredEmailId, EmailSummary> summaries = [];

    private readonly List<IReadOnlyList<StoredEmailId>> calls = [];

    /// <summary>Gets which emails each batch call to the port asked about, in order.</summary>
    public IReadOnlyList<IReadOnlyList<StoredEmailId>> Calls => this.calls;

    /// <summary>Records one email the local copy holds.</summary>
    /// <param name="summary">The summary storage would answer with.</param>
    /// <returns>This reader, so arrangement reads as one statement.</returns>
    public InMemoryStoredEmailSummaries With(EmailSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        this.summaries[summary.StoredEmailId] = summary;

        return this;
    }

    /// <summary>Records every email of a set the local copy holds.</summary>
    /// <param name="held">The summaries storage would answer with.</param>
    /// <returns>This reader, so arrangement reads as one statement.</returns>
    public InMemoryStoredEmailSummaries WithAll(IEnumerable<EmailSummary> held)
    {
        ArgumentNullException.ThrowIfNull(held);

        return held.Aggregate(this, static (reader, summary) => reader.With(summary));
    }

    /// <inheritdoc />
    public Task<EmailSummary?> FindAsync(StoredEmailId storedEmailId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(this.summaries.GetValueOrDefault(storedEmailId));
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<StoredEmailId, EmailSummary>> ReadSummariesAsync(
        IReadOnlyList<StoredEmailId> storedEmailIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(storedEmailIds);
        cancellationToken.ThrowIfCancellationRequested();

        this.calls.Add([.. storedEmailIds]);

        IReadOnlyDictionary<StoredEmailId, EmailSummary> found = storedEmailIds
            .Distinct()
            .Where(this.summaries.ContainsKey)
            .ToDictionary(storedEmailId => storedEmailId, storedEmailId => this.summaries[storedEmailId]);

        return Task.FromResult(found);
    }
}
