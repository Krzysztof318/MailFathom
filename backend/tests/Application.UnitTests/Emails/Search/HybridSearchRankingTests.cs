// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Search;
using MailFathom.Domain.Emails;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Search;

/// <summary>
/// Covers the partition ADR 0030 requires, against known rankings. Nothing here reaches a provider or a database,
/// because what the step promises is a function of where three rankings placed a message and of nothing else: no
/// distance survives into the published sequence, so a test needing vectors to state the guarantee would be stating a
/// different one.
/// </summary>
public sealed class HybridSearchRankingTests
{
    private static readonly DateTimeOffset FirstJuly = new(2026, 7, 1, 8, 0, 0, TimeSpan.Zero);

    /// <summary>The guarantee itself: a description adds a message to the result and never moves one already in it.</summary>
    [Fact]
    public void Compose_MessageBothTheWrittenAndTheDepictedRankingPlaced_KeepsThePlaceTheWrittenOneEarned()
    {
        // Arrange
        var written = CandidateAt(1, score: 0.9f);
        var pictured = CandidateAt(2, score: 0.2f);
        var withoutDepiction = HybridSearchRanking.Compose([written], new SemanticEmailRankings([pictured], []), limit: 4);

        // Act
        var withDepiction = HybridSearchRanking.Compose(
            [written],
            new SemanticEmailRankings([pictured], [pictured]),
            limit: 4);

        // Assert
        Assert.Equal(
            withoutDepiction.Candidates.Select(candidate => candidate.StoredEmailId),
            withDepiction.Candidates.Select(candidate => candidate.StoredEmailId));
        Assert.Empty(withDepiction.DepictedOnly);
    }

    /// <summary>A picture reaches a result only where nothing written did, and then only after every written result.</summary>
    [Fact]
    public void Compose_MessageOnlyTheDepictedRankingPlaced_IsAppendedAfterEveryWrittenResult()
    {
        // Arrange
        var lexical = CandidateAt(1, score: 0.9f);
        var meaning = CandidateAt(2, score: 0.4f);
        var depicted = CandidateAt(3, score: 0.01f);

        // Act
        var composed = HybridSearchRanking.Compose(
            [lexical],
            new SemanticEmailRankings([meaning], [depicted]),
            limit: 5);

        // Assert
        Assert.Equal(depicted.StoredEmailId, composed.Candidates[^1].StoredEmailId);
        Assert.Equal([depicted.StoredEmailId], composed.DepictedOnly);
    }

    /// <summary>
    /// A keyset cursor pages this sequence, and it refuses a boundary that is not finite and positive. The depicted
    /// section is therefore scaled beneath the least fused score rather than subtracted from it.
    /// </summary>
    [Fact]
    public void Compose_AnyDepictedTail_PublishesFinitePositiveScoresThatDescendAcrossBothSections()
    {
        // Arrange
        RankedEmailCandidate[] lexical = [.. Enumerable.Range(1, 3).Select(day => CandidateAt(day, 0.5f))];
        RankedEmailCandidate[] depicted = [.. Enumerable.Range(4, 3).Select(day => CandidateAt(day, 0.02f))];

        // Act
        var composed = HybridSearchRanking.Compose(lexical, new SemanticEmailRankings([], depicted), limit: 6);

        // Assert
        Assert.All(composed.Candidates, candidate => Assert.True(float.IsFinite(candidate.Score) && candidate.Score > 0f));
        Assert.Equal(
            composed.Candidates.Select(candidate => candidate.StoredEmailId),
            composed.Candidates
                .OrderBy(candidate => candidate, RankedEmailCandidate.BestFirst)
                .Select(candidate => candidate.StoredEmailId));
    }

    /// <summary>Both sections share the depth the surface already had, so a full written result leaves no room at all.</summary>
    [Fact]
    public void Compose_WrittenResultsFillingTheLimit_AppendsNoDepictedResult()
    {
        // Arrange
        RankedEmailCandidate[] lexical = [.. Enumerable.Range(1, 4).Select(day => CandidateAt(day, 0.5f))];
        RankedEmailCandidate[] depicted = [.. Enumerable.Range(5, 4).Select(day => CandidateAt(day, 0.02f))];

        // Act
        var composed = HybridSearchRanking.Compose(lexical, new SemanticEmailRankings([], depicted), limit: 2);

        // Assert
        Assert.Equal(2, composed.Candidates.Count);
        Assert.Empty(composed.DepictedOnly);
    }

    /// <summary>The tail is bounded by what the written section leaves, which is what keeps one query's walk bounded.</summary>
    [Fact]
    public void Compose_MoreDepictedResultsThanTheLimitLeaves_AppendsOnlyThatMany()
    {
        // Arrange
        var lexical = CandidateAt(1, score: 0.9f);
        RankedEmailCandidate[] depicted = [.. Enumerable.Range(2, 5).Select(day => CandidateAt(day, 0.02f))];

        // Act
        var composed = HybridSearchRanking.Compose([lexical], new SemanticEmailRankings([], depicted), limit: 3);

        // Assert
        Assert.Equal(3, composed.Candidates.Count);
        Assert.Equal(2, composed.DepictedOnly.Count);
    }

    /// <summary>A repeated identifier would otherwise occupy two of the places the shared depth leaves.</summary>
    [Fact]
    public void Compose_DepictedRankingRepeatingOneMessage_AppendsItOnce()
    {
        // Arrange
        var repeated = CandidateAt(1, score: 0.02f);
        var other = CandidateAt(2, score: 0.01f);

        // Act
        var composed = HybridSearchRanking.Compose([], new SemanticEmailRankings([], [repeated, other, repeated]), limit: 5);

        // Assert
        Assert.Equal(
            [repeated.StoredEmailId, other.StoredEmailId],
            composed.Candidates.Select(candidate => candidate.StoredEmailId));
    }

    /// <summary>Where nothing written matched at all, the pictures are the whole answer and still descend.</summary>
    [Fact]
    public void Compose_NothingWrittenMatched_PublishesTheDepictedRankingAsTheWholeResult()
    {
        // Arrange
        RankedEmailCandidate[] depicted = [.. Enumerable.Range(1, 3).Select(day => CandidateAt(day, 0.02f))];

        // Act
        var composed = HybridSearchRanking.Compose([], new SemanticEmailRankings([], depicted), limit: 5);

        // Assert
        Assert.Equal(
            depicted.Select(candidate => candidate.StoredEmailId),
            composed.Candidates.Select(candidate => candidate.StoredEmailId));
        Assert.All(composed.Candidates, candidate => Assert.True(float.IsFinite(candidate.Score) && candidate.Score > 0f));
    }

    /// <summary>Nothing depicted means the ordering fusion already produced, unmarked and unchanged.</summary>
    [Fact]
    public void Compose_NoDepictedRanking_PublishesTheFusedOrderingWithNothingMarked()
    {
        // Arrange
        var lexical = CandidateAt(1, score: 0.9f);
        var meaning = CandidateAt(2, score: 0.4f);

        // Act
        var composed = HybridSearchRanking.Compose([lexical], new SemanticEmailRankings([meaning], []), limit: 5);

        // Assert
        Assert.Equal(
            ReciprocalRankFusion.Fuse([lexical], [meaning], limit: 5).Select(candidate => candidate.StoredEmailId),
            composed.Candidates.Select(candidate => candidate.StoredEmailId));
        Assert.Empty(composed.DepictedOnly);
    }

    [Fact]
    public void Compose_LimitBelowOne_IsRejected()
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(
            () => HybridSearchRanking.Compose([], SemanticEmailRankings.Empty, limit: 0));
    }

    /// <summary>Builds a candidate whose timeline place is decided by the day it was received on.</summary>
    private static RankedEmailCandidate CandidateAt(int dayOffset, float score) => new(
        new EmailTimelinePosition(
            FirstJuly.AddDays(dayOffset),
            StoredEmailId.Create(new Guid(dayOffset, 0, 0, [0, 0, 0, 0, 0, 0, 0, 0]))),
        score);
}
