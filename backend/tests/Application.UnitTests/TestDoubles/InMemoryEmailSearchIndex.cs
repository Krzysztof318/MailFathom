// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.UnitTests.TestDoubles;

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
/// <para>
/// Reading matches ignores the query text when deciding which snippets a candidate carries, because the arrangement
/// already named them. What it does honor is the port's rule that a candidate the filters no longer admit is absent
/// from the result rather than published.
/// </para>
/// </remarks>
internal sealed class InMemoryEmailSearchIndex : IEmailSearchIndexReader
{
    private readonly List<IndexedEmail> indexed = [];

    private readonly List<ReadRankedCandidatesCall> rankedCandidatesCalls = [];

    private readonly List<ReadMatchesCall> matchesCalls = [];

    /// <summary>Gets what each ranking call asked for, in order.</summary>
    public IReadOnlyList<ReadRankedCandidatesCall> RankedCandidatesCalls => this.rankedCandidatesCalls;

    /// <summary>Gets what each window read asked for, in order.</summary>
    public IReadOnlyList<ReadMatchesCall> MatchesCalls => this.matchesCalls;

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
    public Task<IReadOnlyList<RankedEmailCandidate>> ReadRankedCandidatesAsync(
        MailboxEmailSelection selection,
        EmailSearchQueryText queryText,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(queryText);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        cancellationToken.ThrowIfCancellationRequested();

        this.rankedCandidatesCalls.Add(new ReadRankedCandidatesCall(selection, queryText, limit));

        IReadOnlyList<RankedEmailCandidate> ranking =
        [
            .. this.indexed
                .Where(candidate => candidate.Matches(selection, queryText))
                .Select(candidate => new RankedEmailCandidate(
                    candidate.Email.Summary.Position,
                    candidate.RelevanceRank))
                .Order(Comparer<RankedEmailCandidate>.Create(RankThenTimeline))
                .Take(limit),
        ];

        return Task.FromResult(ranking);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<EmailSearchMatch>> ReadMatchesAsync(
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
        cancellationToken.ThrowIfCancellationRequested();

        this.matchesCalls.Add(new ReadMatchesCall(selection, queryText, snippetBounds, rankedCandidates));

        var eligible = this.indexed
            .Where(candidate => candidate.Email.Matches(selection))
            .ToDictionary(candidate => candidate.Email.Summary.StoredEmailId);

        IReadOnlyList<EmailSearchMatch> matches =
        [
            .. rankedCandidates
                .Where(candidate => eligible.ContainsKey(candidate.StoredEmailId))
                .Select(candidate => new EmailSearchMatch(
                    eligible[candidate.StoredEmailId].Email.Summary,
                    candidate.Score,
                    eligible[candidate.StoredEmailId].Snippets)),
        ];

        return Task.FromResult(matches);
    }

    /// <summary>Orders as the port promises: rank descending, then the newest-first timeline order.</summary>
    private static int RankThenTimeline(RankedEmailCandidate left, RankedEmailCandidate right)
    {
        var byRank = right.Score.CompareTo(left.Score);

        return byRank is not 0
            ? byRank
            : EmailTimelinePosition.NewestFirst.Compare(left.Position, right.Position);
    }

    /// <summary>What one ranking call asked for.</summary>
    /// <param name="Selection">The validated structural filters the use case built.</param>
    /// <param name="QueryText">The validated free text the use case built.</param>
    /// <param name="Limit">How many ranked candidates the use case asked for.</param>
    internal sealed record ReadRankedCandidatesCall(
        MailboxEmailSelection Selection,
        EmailSearchQueryText QueryText,
        int Limit);

    /// <summary>What one window read asked for.</summary>
    /// <param name="Selection">The validated structural filters the use case built.</param>
    /// <param name="QueryText">The validated free text the use case built.</param>
    /// <param name="SnippetBounds">The bounds the use case applied.</param>
    /// <param name="RankedCandidates">The window the use case had already ranked.</param>
    internal sealed record ReadMatchesCall(
        MailboxEmailSelection Selection,
        EmailSearchQueryText QueryText,
        EmailSearchSnippetBounds SnippetBounds,
        IReadOnlyList<RankedEmailCandidate> RankedCandidates);

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
}
