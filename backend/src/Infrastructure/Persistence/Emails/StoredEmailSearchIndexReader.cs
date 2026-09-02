// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
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
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class StoredEmailSearchIndexReader(
    MailFathomDbContext dbContext,
    PostgresTextSearchConfiguration textSearchConfiguration) : IEmailSearchIndexReader
{
    /// <summary>Marks the start of a matched run of words in what PostgreSQL returns.</summary>
    /// <remarks>
    /// A control character rather than anything printable, because this marker is not decoration: whether a fragment
    /// carries it is what tells a genuine highlight from the opening words <c>ts_headline</c> falls back to when the
    /// query matched nothing inside the body. A printable marker cannot answer that — Markdown mail carrying
    /// <c>**</c> of its own would be read as highlighted and the fallback would be published as though it had matched.
    /// The indexed body cannot contain this character: text extraction drops every control character except the tab
    /// and the newline, so the distinction holds by construction rather than by improbability.
    /// </remarks>
    private const string HighlightStartMarker = "\u0002";

    /// <summary>Marks the end of a matched run of words in what PostgreSQL returns.</summary>
    /// <remarks>Distinct from the start marker so a fragment cut short by the character bound can be told to be unbalanced and closed.</remarks>
    private const string HighlightEndMarker = "\u0003";

    /// <summary>Marks both ends of a matched run of words in what a caller receives.</summary>
    /// <remarks>
    /// Emphasis a client can render, rather than PostgreSQL's default <c>&lt;b&gt;</c>: a snippet is text cut from
    /// untrusted mail, and handing it back wrapped in markup invites a consumer to treat the rest of it as markup too.
    /// It is substituted for the control markers after the fragment has been recognized as highlighted, so what a body
    /// happens to contain never takes part in that decision.
    /// </remarks>
    private const string PublishedHighlightMarker = "**";

    /// <summary>Marks an extract the character bound cut short.</summary>
    private const string TruncationMarker = "…";

    /// <summary>Separates the extracts PostgreSQL returns as one value, so they can be split back apart.</summary>
    /// <remarks>
    /// A unit separator rather than the default ellipsis, because the default is punctuation that mail also contains and
    /// splitting on it would cut a snippet in half wherever somebody wrote one. It is the same control character the
    /// filter fingerprint separates its fields with, and for the same reason: no prose carries it.
    /// </remarks>
    private const string SnippetSeparator = "\u001f";

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
                    SnippetsFrom(headlinesById.GetValueOrDefault(candidate.StoredEmailId.Value), snippetBounds))),
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
            .Where(email => email.SearchDocument != null
                && email.SearchDocument.SearchVector.Matches(EF.Functions.WebSearchToTsQuery(configuration, text)));

        // Rank first, then the timeline order from the ordering contract. Ranking alone ties whenever several messages
        // carry the query's words equally often, and an unbroken tie leaves the server free to return either order — so
        // two identical requests would disagree about what the most relevant results were.
        return matching
            .OrderByDescending(email =>
                email.SearchDocument!.SearchVector.Rank(EF.Functions.WebSearchToTsQuery(configuration, text)))
            .ThenBy(email => email.ReceivedAt == null)
            .ThenByDescending(email => email.ReceivedAt)
            .ThenByDescending(email => email.Id)
            .Take(limit)
            .Select(email => new StoredEmailSearchHitRow(
                email.Id,
                email.ReceivedAt,
                email.SearchDocument!.SearchVector.Rank(EF.Functions.WebSearchToTsQuery(configuration, text))));
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
        var headlineOptions = HeadlineOptions(snippetBounds);
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

    /// <summary>Writes the bounds as the option list <c>ts_headline</c> reads them.</summary>
    /// <remarks>
    /// Every value here comes from validated deployment configuration or from a constant in this file, so the list is
    /// composed rather than parameterized. Nothing a request carries reaches it, which is what keeps composing it safe.
    /// </remarks>
    private static string HeadlineOptions(EmailSearchSnippetBounds snippetBounds) => string.Format(
        CultureInfo.InvariantCulture,
        "StartSel=\"{0}\", StopSel=\"{1}\", MaxFragments={2}, MaxWords={3}, MinWords={4}, FragmentDelimiter=\"{5}\"",
        HighlightStartMarker,
        HighlightEndMarker,
        snippetBounds.SnippetsPerEmail,
        snippetBounds.WordsPerSnippet,
        MinimumWordsPerSnippet(snippetBounds),
        SnippetSeparator);

    /// <summary>Decides the shortest extract the server may return, which has to stay below the longest.</summary>
    /// <remarks>
    /// <c>ts_headline</c> rejects an option list whose minimum is not below its maximum, so the floor is derived from
    /// the configured length rather than configured beside it: a deployment cannot then write two numbers that make the
    /// query fail. A third of the maximum leaves room for a fragment that ends early without shrinking to a bare word.
    /// </remarks>
    private static int MinimumWordsPerSnippet(EmailSearchSnippetBounds snippetBounds) =>
        Math.Max(1, snippetBounds.WordsPerSnippet / 3);

    /// <summary>Splits what the server returned into the extracts a result publishes.</summary>
    /// <remarks>
    /// A fragment carrying no highlight marker is dropped. <c>ts_headline</c> falls back to the opening words of a
    /// document when the query matched nothing inside it — which happens whenever an email matched on its subject or a
    /// participant address — and returning that would publish the start of a message body while claiming it was what
    /// matched. Both bounds are applied again here rather than trusted from the option list, because they are the
    /// privacy control and a result must not depend on the server having honored them.
    /// </remarks>
    private static IReadOnlyList<string> SnippetsFrom(string? headline, EmailSearchSnippetBounds snippetBounds) =>
        headline is null
            ? []
            :
            [
                .. headline
                    .Split(SnippetSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(static fragment => fragment.Contains(HighlightStartMarker, StringComparison.Ordinal))
                    .Take(snippetBounds.SnippetsPerEmail)
                    .Select(fragment => Published(fragment, snippetBounds)),
            ];

    /// <summary>Bounds one extract by characters and puts its markers into the form a caller receives.</summary>
    /// <remarks>
    /// The character bound is what makes the word bound mean something. <c>MaxWords</c> counts words, and a word is
    /// whatever lies between two spaces, so a message carrying one enormous unbroken token beside a match — a URL, a
    /// base64 blob, a hash — satisfies a limit of a few words while publishing most of its body.
    /// </remarks>
    private static string Published(string fragment, EmailSearchSnippetBounds snippetBounds)
    {
        var bounded = BoundedToMessageCharacters(fragment, snippetBounds.MaximumCharacters);

        return Closed(bounded)
            .Replace(HighlightStartMarker, PublishedHighlightMarker, StringComparison.Ordinal)
            .Replace(HighlightEndMarker, PublishedHighlightMarker, StringComparison.Ordinal);
    }

    /// <summary>Cuts an extract once it has carried as many characters of the message as the bound allows.</summary>
    /// <remarks>
    /// The markers are not counted, because the bound exists to limit how much of a message one result publishes and a
    /// marker is MailFathom's own. Counting them would also make the bound depend on how often the query matched inside the
    /// extract, so the same setting would show less of a message the better it matched — which is the opposite of what
    /// a reader wants and tells an operator nothing about what the number protects.
    /// </remarks>
    private static string BoundedToMessageCharacters(string fragment, int maximumCharacters)
    {
        var messageCharacters = 0;
        var index = 0;

        while (index < fragment.Length && messageCharacters < maximumCharacters)
        {
            if (!IsMarker(fragment[index]))
            {
                messageCharacters++;
            }

            index++;
        }

        return index == fragment.Length ? fragment : string.Concat(fragment[..index], TruncationMarker);
    }

    private static bool IsMarker(char character) =>
        character == HighlightStartMarker[0] || character == HighlightEndMarker[0];

    /// <summary>Closes a highlight the character bound cut in half, so the published markers stay paired.</summary>
    /// <remarks>
    /// Truncation can land between the two control markers, and the published marker is the same string at both ends —
    /// so an unclosed run would leave a client emphasizing the rest of the extract rather than the words that matched.
    /// </remarks>
    private static string Closed(string fragment) =>
        fragment.Count(character => character == HighlightStartMarker[0])
        > fragment.Count(character => character == HighlightEndMarker[0])
            ? string.Concat(fragment, HighlightEndMarker)
            : fragment;
}
