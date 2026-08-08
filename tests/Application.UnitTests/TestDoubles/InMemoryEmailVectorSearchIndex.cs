// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
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

    /// <summary>Adds one embedded email to the index.</summary>
    /// <param name="summary">The email a candidate stands for.</param>
    /// <param name="distance">How far its nearest passage sits from any query vector, smaller being nearer.</param>
    /// <returns>This index, so arrangement reads as one statement.</returns>
    public InMemoryEmailVectorSearchIndex With(EmailSummary summary, float distance)
    {
        this.indexed.Add(new NearEmail(new InMemoryStoredEmail(summary, []), distance));

        return this;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RankedEmailCandidate>> ReadNearestCandidatesAsync(
        MailboxEmailSelection selection,
        ActiveEmbeddingProfile profile,
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

        IReadOnlyList<RankedEmailCandidate> ranking =
        [
            .. this.indexed
                .Where(candidate => candidate.Email.Matches(selection))
                .Select(candidate => new RankedEmailCandidate(candidate.Email.Summary.Position, candidate.Distance))
                .Order(Comparer<RankedEmailCandidate>.Create(NearestThenTimeline))
                .Take(limit),
        ];

        return Task.FromResult(ranking);
    }

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
        ActiveEmbeddingProfile Profile,
        EmbeddingVector QueryVector,
        int Limit);

    private sealed record NearEmail(InMemoryStoredEmail Email, float Distance);
}
