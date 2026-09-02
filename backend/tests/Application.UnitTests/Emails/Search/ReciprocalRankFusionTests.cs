// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Search;
using MailFathom.Domain.Emails;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Search;

/// <summary>
/// Covers the fusion itself against known rank inputs. Nothing here reaches a provider or a database, which is the
/// point: what the method promises is a function of where two rankings placed a document and of nothing else, so a test
/// that needed vectors to state it would be testing the wrong thing.
/// </summary>
public sealed class ReciprocalRankFusionTests
{
    private static readonly DateTimeOffset FirstJuly = new(2026, 7, 1, 8, 0, 0, TimeSpan.Zero);

    /// <summary>A document both rankings placed scores in both, which is what makes agreement outrank a single first place.</summary>
    [Fact]
    public void Fuse_DocumentBothRankingsPlaced_OutranksOneEitherRankingPlacedFirst()
    {
        // Arrange
        var agreedUpon = CandidateAt(1, score: 0.4f);
        var lexicalFirst = CandidateAt(2, score: 0.9f);
        var semanticFirst = CandidateAt(3, score: 0.05f);

        // Act
        var fused = ReciprocalRankFusion.Fuse(
            [lexicalFirst, agreedUpon],
            [semanticFirst, agreedUpon],
            limit: 3);

        // Assert
        Assert.Equal(agreedUpon.StoredEmailId, fused[0].StoredEmailId);
    }

    /// <summary>A document only one ranking found still scores, which is how a semantic match with no shared word appears at all.</summary>
    [Fact]
    public void Fuse_DocumentOnlyOneRankingPlaced_IsStillReturned()
    {
        // Arrange
        var lexicalOnly = CandidateAt(1, score: 0.9f);
        var semanticOnly = CandidateAt(2, score: 0.1f);

        // Act
        var fused = ReciprocalRankFusion.Fuse([lexicalOnly], [semanticOnly], limit: 10);

        // Assert: both placed first in their own ranking, so the timeline tiebreaker puts the newer one ahead.
        Assert.Equal(
            [semanticOnly.StoredEmailId, lexicalOnly.StoredEmailId],
            fused.Select(candidate => candidate.StoredEmailId));
    }

    /// <summary>The published score is the fused one, so a caller never reads a rank from one side of the fusion.</summary>
    [Fact]
    public void Fuse_AnyRankings_PublishesTheFusedScoreRatherThanEitherInput()
    {
        // Arrange
        var candidate = CandidateAt(1, score: 0.9f);
        var expected = (1f / (ReciprocalRankFusion.RankConstant + 1)) * 2;

        // Act
        var fused = ReciprocalRankFusion.Fuse([candidate], [candidate], limit: 1);

        // Assert
        Assert.Equal(expected, Assert.Single(fused).Score, tolerance: 1e-6f);
    }

    /// <summary>
    /// Symmetric places produce identical sums by construction, so the fused order would otherwise depend on dictionary
    /// enumeration. The timeline order settles them, which is the same order that settles a lexical rank tie.
    /// </summary>
    [Fact]
    public void Fuse_SymmetricallyPlacedDocuments_AreOrderedByTheTimelineTiebreaker()
    {
        // Arrange
        var older = CandidateAt(1, score: 0.5f);
        var newer = CandidateAt(2, score: 0.5f);

        // Act
        var fused = ReciprocalRankFusion.Fuse([older, newer], [newer, older], limit: 2);

        // Assert
        Assert.Equal(
            [newer.StoredEmailId, older.StoredEmailId],
            fused.Select(candidate => candidate.StoredEmailId));
    }

    /// <summary>A ranking that repeated an identifier would otherwise cast two votes for it.</summary>
    [Fact]
    public void Fuse_RankingRepeatingOneDocument_ScoresItAtItsBestPlaceOnly()
    {
        // Arrange
        var repeated = CandidateAt(1, score: 0.5f);
        var other = CandidateAt(2, score: 0.5f);

        // Act
        var fused = ReciprocalRankFusion.Fuse([repeated, other, repeated], [], limit: 2);

        // Assert
        Assert.Equal(1f / (ReciprocalRankFusion.RankConstant + 1), fused[0].Score, tolerance: 1e-6f);
    }

    /// <summary>The fused window is bounded like every other result a mailbox read publishes.</summary>
    [Fact]
    public void Fuse_MoreDocumentsThanTheLimit_ReturnsOnlyThatMany()
    {
        // Arrange
        RankedEmailCandidate[] lexical = [.. Enumerable.Range(1, 10).Select(day => CandidateAt(day, 0.5f))];

        // Act
        var fused = ReciprocalRankFusion.Fuse(lexical, [], limit: 4);

        // Assert
        Assert.Equal(4, fused.Count);
    }

    /// <summary>Two identical inputs produce one sequence, which is what "deterministic for a given index state" means.</summary>
    [Fact]
    public void Fuse_TheSameRankingsTwice_ProducesTheSameSequence()
    {
        // Arrange
        RankedEmailCandidate[] lexical = [.. Enumerable.Range(1, 6).Select(day => CandidateAt(day, 0.5f))];
        RankedEmailCandidate[] semantic = [.. lexical.Reverse()];

        // Act
        var first = ReciprocalRankFusion.Fuse(lexical, semantic, limit: 6);
        var second = ReciprocalRankFusion.Fuse(lexical, semantic, limit: 6);

        // Assert
        Assert.Equal(
            first.Select(candidate => candidate.StoredEmailId),
            second.Select(candidate => candidate.StoredEmailId));
    }

    [Fact]
    public void Fuse_LimitBelowOne_IsRejected()
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => ReciprocalRankFusion.Fuse([], [], limit: 0));
    }

    /// <summary>Builds a candidate whose timeline place is decided by the day it was received on.</summary>
    private static RankedEmailCandidate CandidateAt(int dayOffset, float score) => new(
        new EmailTimelinePosition(
            FirstJuly.AddDays(dayOffset),
            StoredEmailId.Create(new Guid(dayOffset, 0, 0, [0, 0, 0, 0, 0, 0, 0, 0]))),
        score);
}
