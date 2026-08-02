// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.UnitTests;

/// <summary>An in-memory stand-in for the lexical index, holding the emails and ranks a test arranged.</summary>
/// <remarks>
/// <para>
/// The fake applies the structural filters, the ordering contract, and the result bound itself, which is what lets a
/// test assert what a window contains rather than only which values were forwarded. The relevance rank is arranged
/// rather than computed: what a rank means is PostgreSQL's decision, and reproducing a ranking function here would test
/// a copy of it. What this fake proves is everything downstream of the rank — that ties break the way the contract says,
/// that the window is bounded, and that the use case forwards a validated selection.
/// </para>
/// <para>
/// Snippets are arranged for the same reason and cannot be proven here at all: they are cut by <c>ts_headline</c>, and a
/// fake that returned bounded extracts would report a bound the real adapter might not apply. That claim belongs to the
/// suites that observe the generated command and run it against a real database.
/// </para>
/// </remarks>
internal sealed class InMemoryEmailSearchIndex : IEmailSearchIndexReader
{
    private readonly List<IndexedEmail> indexed = [];

    private readonly List<ReadRankedMatchesCall> calls = [];

    /// <summary>Gets what each call to the port asked for, in order.</summary>
    public IReadOnlyList<ReadRankedMatchesCall> Calls => this.calls;

    /// <summary>Adds one email to the index.</summary>
    /// <param name="summary">The summary a match would return.</param>
    /// <param name="relevanceRank">What the ranking would score this email against any query it matches.</param>
    /// <param name="matchedText">The text this email matches, compared without regard to case, or <see langword="null" /> to match every query.</param>
    /// <param name="snippets">The extracts a match would carry.</param>
    /// <returns>This index, so arrangement reads as one statement.</returns>
    public InMemoryEmailSearchIndex With(
        EmailSummary summary,
        float relevanceRank = 0.5f,
        string? matchedText = null,
        params string[] snippets)
    {
        this.indexed.Add(new IndexedEmail(new InMemoryStoredEmail(summary, []), relevanceRank, matchedText, snippets));

        return this;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<EmailSearchMatch>> ReadRankedMatchesAsync(
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
        cancellationToken.ThrowIfCancellationRequested();

        this.calls.Add(new ReadRankedMatchesCall(selection, queryText, snippetBounds, limit));

        var timelineOrder = EmailTimelinePosition.ComparerFor(EmailTimelineDirection.NewestFirst);

        IReadOnlyList<EmailSearchMatch> window =
        [
            .. this.indexed
                .Where(candidate => candidate.Matches(selection, queryText))
                .Select(candidate => new EmailSearchMatch(
                    candidate.Email.Summary,
                    candidate.RelevanceRank,
                    candidate.Snippets))
                .Order(new RankThenTimelineComparer(timelineOrder))
                .Take(limit),
        ];

        return Task.FromResult(window);
    }

    /// <summary>What one call to the port asked for.</summary>
    /// <param name="Selection">The validated structural filters the use case built.</param>
    /// <param name="QueryText">The validated free text the use case built.</param>
    /// <param name="SnippetBounds">The bounds the use case applied.</param>
    /// <param name="Limit">How many ranked results the use case asked for.</param>
    internal sealed record ReadRankedMatchesCall(
        MailboxEmailSelection Selection,
        EmailSearchQueryText QueryText,
        EmailSearchSnippetBounds SnippetBounds,
        int Limit);

    private sealed record IndexedEmail(
        InMemoryStoredEmail Email,
        float RelevanceRank,
        string? MatchedText,
        IReadOnlyList<string> Snippets)
    {
        public bool Matches(MailboxEmailSelection selection, EmailSearchQueryText queryText) =>
            this.Email.Matches(selection)
            && (this.MatchedText is not { } text
                || text.Contains(queryText.Value, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Orders as the port promises: rank descending, then the newest-first timeline order.</summary>
    private sealed class RankThenTimelineComparer(IComparer<EmailTimelinePosition> timelineOrder)
        : IComparer<EmailSearchMatch>
    {
        public int Compare(EmailSearchMatch? x, EmailSearchMatch? y)
        {
            var byRank = y!.RelevanceRank.CompareTo(x!.RelevanceRank);

            return byRank is not 0 ? byRank : timelineOrder.Compare(x.Position, y.Position);
        }
    }
}
