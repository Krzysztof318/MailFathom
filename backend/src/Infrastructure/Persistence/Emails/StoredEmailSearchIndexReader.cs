// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Emails.Summaries;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence.Connections;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>Ranks matching mail against free text in the PostgreSQL lexical index, and reads the window a ranking chose.</summary>
/// <remarks>
/// <para>
/// The query text reaches PostgreSQL as a parameter and nothing else. <c>websearch_to_tsquery</c> parses it into a
/// query on the server, so a caller's quotation marks, <c>OR</c>, and leading minus signs are operators that function
/// understands and every other metacharacter is ordinary text — while nothing at any point concatenates the value into
/// SQL. That function is chosen over <c>to_tsquery</c> for the same reason: it accepts whatever a person types instead
/// of raising a syntax error at the boundary for an unbalanced bracket.
/// </para>
/// <para>
/// The text search configuration is the deployment's validated setting, taken from the same value the index was built
/// with and never from the request. Querying under a different configuration than the vector was generated with would
/// stem the query into forms the index does not hold, which shows up as missing results rather than as an error.
/// </para>
/// <para>
/// The snippets are cut by PostgreSQL rather than by this process, which is what keeps a message's body inside the
/// database: the projection reads <c>ts_headline</c> output, so the column holding the body text is never part of a
/// result set that crosses this boundary.
/// </para>
/// <para>
/// A message ranks on the words of its body <em>and</em> on the words of the documents attached to it, because a person
/// searching for a clause by the letters it is written in is doing a lexical search and an attachment reachable only by
/// meaning is half-found. The two are added rather than compared, so a message carrying the query's words in both
/// places outranks one carrying them in either — the same agreement rule fusion applies between rankings — and the
/// attachment side takes its best file rather than the sum of them, since a term repeated across three files makes a
/// message no more relevant than a term in one. A description of a picture takes no part in this at all: the generated
/// column it would have been indexed in is null by construction, which is
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0030-describing-an-image-attachment-in-words-and-ranking-a-depicted-match-below-a-written-one.md">ADR 0030</see>
/// enforced by the database rather than remembered here.
/// </para>
/// <para>
/// The extracts stay the body's. What an attachment contributed is read separately, by
/// <see cref="EmailAttachmentMatchReader" />, because a headline cut from a file and published as a message's own
/// snippet would quote words the body never carried and leave a reader unable to say which file they came from.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class StoredEmailSearchIndexReader(
    MailFathomDbContext dbContext,
    PostgresTextSearchConfiguration textSearchConfiguration) : IEmailSearchIndexReader
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<RankedEmailCandidate>> ReadRankedCandidatesAsync(
        MailboxEmailSelection selection,
        EmailSearchQueryText queryText,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(queryText);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var hits = await this.RankedHitsQuery(selection, queryText, limit)
            .ToArrayAsync(cancellationToken);

        return
        [
            .. hits.Select(static hit => new RankedEmailCandidate(
                new EmailTimelinePosition(hit.ReceivedAt, StoredEmailId.Create(hit.StoredEmailId)),
                hit.RelevanceRank)),
        ];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EmailSearchMatch>> ReadMatchesAsync(
        MailboxEmailSelection selection,
        EmailSearchQueryText queryText,
        EmailSearchSnippetBounds snippetBounds,
        IReadOnlyList<RankedEmailCandidate> rankedCandidates,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(queryText);
        ArgumentNullException.ThrowIfNull(snippetBounds);
        ArgumentNullException.ThrowIfNull(rankedCandidates);

        if (rankedCandidates.Count is 0)
        {
            return [];
        }

        Guid[] rankedIds = [.. rankedCandidates.Select(static candidate => candidate.StoredEmailId.Value)];

        var summariesById = await this.SummariesByIdAsync(selection, rankedIds, cancellationToken);

        // Nothing is worth a second statement once the first found nothing eligible, and the headline query is the
        // expensive one: it cuts a highlighted extract out of a message body per row.
        var headlinesById = summariesById.Count is 0
            ? []
            : await this.HeadlinesByIdAsync(selection, queryText, snippetBounds, rankedIds, cancellationToken);

        // The candidates' order is the result's order, so the two lookups are keyed rather than re-sorted. A candidate
        // neither query returned is dropped: its email was deleted, or a run committed between the statements and left
        // it outside the filter this search was issued for. Publishing it either way would put a row in the result that
        // contradicts the request that produced it.
        return
        [
            .. rankedCandidates
                .Where(candidate => summariesById.ContainsKey(candidate.StoredEmailId.Value))
                .Select(candidate => new EmailSearchMatch(
                    summariesById[candidate.StoredEmailId.Value],
                    candidate.Score,
                    SearchHeadlineText.Extracts(
                        headlinesById.GetValueOrDefault(candidate.StoredEmailId.Value),
                        snippetBounds))),
        ];
    }

    /// <summary>Composes the query that ranks the matching emails.</summary>
    /// <param name="selection">The validated structural filters.</param>
    /// <param name="queryText">The validated free text.</param>
    /// <param name="limit">The greatest number of ranked candidates to return.</param>
    /// <returns>The composed query, which nothing has executed yet.</returns>
    /// <remarks>
    /// Exposed rather than inlined because the command this composes is itself the contract: that the query text arrives
    /// as a parameter cannot be observed from the application side, and a test asserting it against anything but the
    /// generated SQL would pass whatever this method did. Reading the composed query is the only place that claim is
    /// checkable without a database.
    /// </remarks>
    internal IQueryable<StoredEmailSearchHitRow> RankedHitsQuery(
        MailboxEmailSelection selection,
        EmailSearchQueryText queryText,
        int limit)
    {
        var configuration = textSearchConfiguration.Value;
        var text = queryText.Value;

        var matching = StoredEmailSelectionPredicate
            .Matching(dbContext.StoredEmails.AsNoTracking(), selection)
            .Where(email =>
                (email.SearchDocument != null
                    && email.SearchDocument.SearchVector.Matches(
                        EF.Functions.WebSearchToTsQuery(configuration, text)))
                || email.AttachmentTexts.Any(attachment => attachment.SearchVector != null
                    && attachment.SearchVector.Matches(EF.Functions.WebSearchToTsQuery(configuration, text))));

        // Rank first, then the timeline order from the ordering contract. Ranking alone ties whenever several messages
        // carry the query's words equally often, and an unbroken tie leaves the server free to return either order — so
        // two identical requests would disagree about what the most relevant results were.
        return matching
            .OrderByDescending(email =>
                ((float?)email.SearchDocument!.SearchVector.Rank(
                    EF.Functions.WebSearchToTsQuery(configuration, text)) ?? 0f)
                + (email.AttachmentTexts
                    .Where(attachment => attachment.SearchVector != null)
                    .Max(attachment => (float?)attachment.SearchVector!.Rank(
                        EF.Functions.WebSearchToTsQuery(configuration, text))) ?? 0f))
            .ThenBy(email => email.ReceivedAt == null)
            .ThenByDescending(email => email.ReceivedAt)
            .ThenByDescending(email => email.Id)
            .Take(limit)
            .Select(email => new StoredEmailSearchHitRow(
                email.Id,
                email.ReceivedAt,
                ((float?)email.SearchDocument!.SearchVector.Rank(
                    EF.Functions.WebSearchToTsQuery(configuration, text)) ?? 0f)
                + (email.AttachmentTexts
                    .Where(attachment => attachment.SearchVector != null)
                    .Max(attachment => (float?)attachment.SearchVector!.Rank(
                        EF.Functions.WebSearchToTsQuery(configuration, text))) ?? 0f)));
    }

    /// <summary>Composes the query that cuts the snippets of an already ranked window.</summary>
    /// <param name="selection">The validated structural filters.</param>
    /// <param name="queryText">The validated free text the extracts are cut around.</param>
    /// <param name="snippetBounds">How many extracts one result may carry, and how long each may be.</param>
    /// <param name="rankedIds">The window's identities.</param>
    /// <returns>The composed query, which nothing has executed yet.</returns>
    /// <remarks>
    /// <para>
    /// Exposed for the reason the ranking query is: that the query text reaches <c>ts_headline</c> as a parameter and
    /// the option list is composed from validated deployment configuration alone are both claims about the generated
    /// command rather than about anything observable from the application side.
    /// </para>
    /// <para>
    /// It runs over the window rather than over everything that matched, which is what keeps a broad query from costing
    /// a pass over every matching message body. A candidate that ranked semantically and carries none of the query's
    /// words yields a fragment with no highlight marker, and the caller drops it — the same treatment a lexical match on
    /// a subject alone already receives.
    /// </para>
    /// </remarks>
    internal IQueryable<StoredEmailHeadlineRow> HeadlinesQuery(
        MailboxEmailSelection selection,
        EmailSearchQueryText queryText,
        EmailSearchSnippetBounds snippetBounds,
        IReadOnlyList<Guid> rankedIds)
    {
        var configuration = textSearchConfiguration.Value;
        var text = queryText.Value;
        var headlineOptions = SearchHeadlineText.Options(snippetBounds);
        var identities = rankedIds.ToArray();

        return StoredEmailSelectionPredicate
            .Matching(dbContext.StoredEmails.AsNoTracking(), selection)
            .Where(email => identities.Contains(email.Id) && email.SearchDocument != null)
            .Select(email => new StoredEmailHeadlineRow(
                email.Id,
                EF.Functions.WebSearchToTsQuery(configuration, text)
                    .GetResultHeadline(configuration, email.SearchDocument!.BodyText!, headlineOptions)));
    }

    /// <summary>Reads the summaries of the ranked emails through the projection every read model publishes them by.</summary>
    /// <remarks>
    /// <para>
    /// A query of its own rather than a wider ranking one. The summary projection is the control that decides what a
    /// mailbox read can return at all, and restating its columns beside the ranking expressions would put a second copy
    /// of that control in the codebase. This one is keyed by at most a window's worth of identifiers, so what it costs
    /// is one index lookup per result.
    /// </para>
    /// <para>
    /// It narrows by the selection as well as by those identifiers, which is what keeps two statements from publishing
    /// one self-contradicting result. PostgreSQL reads each statement under its own snapshot, so a run committing
    /// between them — the extraction backfill setting an attachment count, reconciliation setting a flag — could
    /// otherwise return a summary that fails the filter its own rank was computed under. Re-applying the predicate makes
    /// such a row absent rather than wrong, on the same terms as one that was deleted.
    /// </para>
    /// </remarks>
    private async Task<Dictionary<Guid, EmailSummary>> SummariesByIdAsync(
        MailboxEmailSelection selection,
        IReadOnlyList<Guid> rankedIds,
        CancellationToken cancellationToken)
    {
        var identities = rankedIds.ToArray();

        var rows = await StoredEmailSelectionPredicate
            .Matching(dbContext.StoredEmails.AsNoTracking(), selection)
            .Where(email => identities.Contains(email.Id))
            .Select(StoredEmailSummaryRow.Projection)
            .ToArrayAsync(cancellationToken);

        return rows.ToDictionary(
            static row => row.Id,
            static row => row.ToSummary());
    }

    /// <summary>Reads the highlighted extracts of the ranked emails, which PostgreSQL rather than this process cuts.</summary>
    private async Task<Dictionary<Guid, string?>> HeadlinesByIdAsync(
        MailboxEmailSelection selection,
        EmailSearchQueryText queryText,
        EmailSearchSnippetBounds snippetBounds,
        IReadOnlyList<Guid> rankedIds,
        CancellationToken cancellationToken)
    {
        var rows = await this.HeadlinesQuery(selection, queryText, snippetBounds, rankedIds)
            .ToArrayAsync(cancellationToken);

        return rows.ToDictionary(
            static row => row.StoredEmailId,
            static row => row.Headline);
    }
}
