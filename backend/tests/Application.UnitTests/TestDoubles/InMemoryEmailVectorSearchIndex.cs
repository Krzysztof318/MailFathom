// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>An in-memory stand-in for the vector index, holding the emails and distances a test arranged.</summary>
/// <remarks>
/// Distances are arranged rather than computed. What a distance is belongs to pgvector and to the embedding model, and
/// a fake that measured one would test a reimplementation of both; what this proves is everything the distance feeds —
/// that the nearest-first order reaches the fusion, that the filters are forwarded, and that a search of an instance
/// with vectors ranks differently from one without.
/// </remarks>
internal sealed class InMemoryEmailVectorSearchIndex : IEmailVectorSearchIndexReader
{
    private readonly List<NearEmail> indexed = [];

    private readonly List<ReadNearestCandidatesCall> calls = [];

    /// <summary>Gets what each call to the port asked for, in order.</summary>
    public IReadOnlyList<ReadNearestCandidatesCall> Calls => this.calls;

    /// <summary>Adds one email whose nearest passage is something a person wrote.</summary>
    /// <param name="summary">The email a candidate stands for.</param>
    /// <param name="distance">How far its nearest passage sits from any query vector, smaller being nearer.</param>
    /// <returns>This index, so arrangement reads as one statement.</returns>
    public InMemoryEmailVectorSearchIndex With(EmailSummary summary, float distance)
    {
        this.indexed.Add(new NearEmail(new InMemoryStoredEmail(summary, []), distance, Depicted: false));

        return this;
    }

    /// <summary>Adds one email whose nearest passage is a model's description of an attached picture.</summary>
    /// <param name="summary">The email a candidate stands for.</param>
    /// <param name="distance">How far that description sits from any query vector, smaller being nearer.</param>
    /// <returns>This index, so arrangement reads as one statement.</returns>
    /// <remarks>
    /// An email can be arranged into both rankings, which is a message carrying a written passage and a described
    /// picture that are each near the query — the case the floor has to place once, at the written passage's place.
    /// </remarks>
    public InMemoryEmailVectorSearchIndex WithDepiction(EmailSummary summary, float distance)
    {
        this.indexed.Add(new NearEmail(new InMemoryStoredEmail(summary, []), distance, Depicted: true));

        return this;
    }

    /// <inheritdoc />
    public Task<SemanticEmailRankings> ReadNearestCandidatesAsync(
        MailboxEmailSelection selection,
        RegisteredEmbeddingProfile profile,
        EmbeddingVector queryVector,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(queryVector);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        cancellationToken.ThrowIfCancellationRequested();

        this.calls.Add(new ReadNearestCandidatesCall(selection, profile, queryVector, limit));

        return Task.FromResult(new SemanticEmailRankings(
            this.Ranked(selection, limit, depicted: false),
            this.Ranked(selection, limit, depicted: true)));
    }

    /// <summary>Ranks one of the two kinds of passage the port separates.</summary>
    private IReadOnlyList<RankedEmailCandidate> Ranked(
        MailboxEmailSelection selection,
        int limit,
        bool depicted) =>
        [
            .. this.indexed
                .Where(candidate => candidate.Depicted == depicted && candidate.Email.Matches(selection))
                .Select(candidate => new RankedEmailCandidate(candidate.Email.Summary.Position, candidate.Distance))
                .Order(Comparer<RankedEmailCandidate>.Create(NearestThenTimeline))
                .Take(limit),
        ];

    /// <summary>Orders as the port promises: nearest first, then the newest-first timeline order.</summary>
    private static int NearestThenTimeline(RankedEmailCandidate left, RankedEmailCandidate right)
    {
        var byDistance = left.Score.CompareTo(right.Score);

        return byDistance is not 0
            ? byDistance
            : EmailTimelinePosition.NewestFirst.Compare(left.Position, right.Position);
    }

    /// <summary>What one call to the port asked for.</summary>
    /// <param name="Selection">The validated structural filters the use case built.</param>
    /// <param name="Profile">The profile the caller established both sides belong to.</param>
    /// <param name="QueryVector">Where the caller placed the query.</param>
    /// <param name="Limit">How many candidates the caller asked for.</param>
    internal sealed record ReadNearestCandidatesCall(
        MailboxEmailSelection Selection,
        RegisteredEmbeddingProfile Profile,
        EmbeddingVector QueryVector,
        int Limit);

    private sealed record NearEmail(InMemoryStoredEmail Email, float Distance, bool Depicted);
}
