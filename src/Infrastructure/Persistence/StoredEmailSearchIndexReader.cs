// Copyright © 2026 Krzysztof Kasprowicz

using System.Globalization;
using MailMcp.Application.Emails;
using MailMcp.CodeCoverage;
using Microsoft.EntityFrameworkCore;

namespace MailMcp.Infrastructure.Persistence;

/// <summary>Reads bounded, ranked windows of matching mail out of the PostgreSQL lexical index.</summary>
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
    MailMcpDbContext dbContext,
    PostgresTextSearchConfiguration textSearchConfiguration) : IEmailSearchIndexReader
{
    /// <summary>Marks both ends of a matched run of words inside a snippet.</summary>
    /// <remarks>
    /// Emphasis a client can render, rather than PostgreSQL's default <c>&lt;b&gt;</c>: a snippet is text cut from
    /// untrusted mail, and handing it back wrapped in markup invites a consumer to treat the rest of it as markup too.
    /// The marker is the only thing MailMcp adds to the extract, and a body that already contains the same characters
    /// is the one case where a reader cannot tell the two apart.
    /// </remarks>
    private const string HighlightMarker = "**";

    /// <summary>Separates the extracts PostgreSQL returns as one value, so they can be split back apart.</summary>
    /// <remarks>
    /// A unit separator rather than the default ellipsis, because the default is punctuation that mail also contains and
    /// splitting on it would cut a snippet in half wherever somebody wrote one. It is the same control character the
    /// filter fingerprint separates its fields with, and for the same reason: no prose carries it.
    /// </remarks>
    private const string SnippetSeparator = "\u001f";

    /// <inheritdoc />
    public async Task<IReadOnlyList<EmailSearchMatch>> ReadRankedMatchesAsync(
        MailboxEmailSelection selection,
        EmailSearchQueryText queryText,
        EmailSearchSnippetBounds snippetBounds,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(queryText);
        ArgumentNullException.ThrowIfNull(snippetBounds);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var hits = await this.RankedHitsQuery(selection, queryText, snippetBounds, limit)
            .ToArrayAsync(cancellationToken);

        if (hits.Length is 0)
        {
            return [];
        }

        var summariesById = await this.SummariesByIdAsync(hits, cancellationToken);

        // The ranking query's order is the result's order, so the summaries are looked up rather than re-sorted. A hit
        // whose email was deleted between the two queries is dropped: it is mail the caller is no longer entitled to
        // read, and a placeholder result would publish that it had once existed.
        return
        [
            .. hits
                .Where(hit => summariesById.ContainsKey(hit.StoredEmailId))
                .Select(hit => new EmailSearchMatch(
                    summariesById[hit.StoredEmailId],
                    hit.RelevanceRank,
                    SnippetsFrom(hit.Headline, snippetBounds))),
        ];
    }

    /// <summary>Composes the query that ranks the matching emails and cuts their snippets.</summary>
    /// <param name="selection">The validated structural filters.</param>
    /// <param name="queryText">The validated free text.</param>
    /// <param name="snippetBounds">How many extracts one result may carry, and how long each may be.</param>
    /// <param name="limit">The greatest number of ranked results to return.</param>
    /// <returns>The composed query, which nothing has executed yet.</returns>
    /// <remarks>
    /// <para>
    /// Exposed rather than inlined because the command this composes is itself the contract: that the query text arrives
    /// as a parameter cannot be observed from the application side, and a test asserting it against anything but the
    /// generated SQL would pass whatever this method did. Reading the composed query is the only place that claim is
    /// checkable without a database.
    /// </para>
    /// <para>
    /// The window is closed before the snippets are cut, which is why <c>Take</c> precedes the projection: cutting a
    /// highlighted extract costs a pass over a message body, and doing it for every match of a common word rather than
    /// for the results being returned would make a broad query arbitrarily expensive.
    /// </para>
    /// </remarks>
    internal IQueryable<StoredEmailSearchHitRow> RankedHitsQuery(
        MailboxEmailSelection selection,
        EmailSearchQueryText queryText,
        EmailSearchSnippetBounds snippetBounds,
        int limit)
    {
        var configuration = textSearchConfiguration.Value;
        var text = queryText.Value;
        var headlineOptions = HeadlineOptions(snippetBounds);

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
                email.SearchDocument!.SearchVector.Rank(EF.Functions.WebSearchToTsQuery(configuration, text)),
                EF.Functions.WebSearchToTsQuery(configuration, text)
                    .GetResultHeadline(configuration, email.SearchDocument!.BodyText!, headlineOptions)));
    }

    /// <summary>Reads the summaries of the ranked emails through the projection every read model publishes them by.</summary>
    /// <remarks>
    /// A second query rather than a wider first one. The summary projection is the control that decides what a mailbox
    /// read can return at all, and restating its columns beside the ranking expressions would put a second copy of that
    /// control in the codebase. This one is keyed by at most a window's worth of identifiers, so what it costs is one
    /// index lookup per result.
    /// </remarks>
    private async Task<Dictionary<Guid, EmailSummary>> SummariesByIdAsync(
        IReadOnlyList<StoredEmailSearchHitRow> hits,
        CancellationToken cancellationToken)
    {
        var rankedIds = hits.Select(static hit => hit.StoredEmailId).ToArray();

        var rows = await dbContext.StoredEmails
            .AsNoTracking()
            .Where(email => rankedIds.Contains(email.Id))
            .Select(StoredEmailSummaryProjection.Row)
            .ToArrayAsync(cancellationToken);

        return rows.ToDictionary(
            static row => row.Id,
            StoredEmailSummaryProjection.ToSummary);
    }

    /// <summary>Writes the bounds as the option list <c>ts_headline</c> reads them.</summary>
    /// <remarks>
    /// Every value here comes from validated deployment configuration or from a constant in this file, so the list is
    /// composed rather than parameterized. Nothing a request carries reaches it, which is what keeps composing it safe.
    /// </remarks>
    private static string HeadlineOptions(EmailSearchSnippetBounds snippetBounds) => string.Format(
        CultureInfo.InvariantCulture,
        "StartSel=\"{0}\", StopSel=\"{1}\", MaxFragments={2}, MaxWords={3}, MinWords={4}, FragmentDelimiter=\"{5}\"",
        HighlightMarker,
        HighlightMarker,
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
    /// matched. The count is bounded again here rather than trusted from the option list, because the bound is the
    /// privacy control and a result must not depend on the server having honored it.
    /// </remarks>
    private static IReadOnlyList<string> SnippetsFrom(string? headline, EmailSearchSnippetBounds snippetBounds) =>
        headline is null
            ? []
            :
            [
                .. headline
                    .Split(SnippetSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(static fragment => fragment.Contains(HighlightMarker, StringComparison.Ordinal))
                    .Take(snippetBounds.SnippetsPerEmail),
            ];
}
