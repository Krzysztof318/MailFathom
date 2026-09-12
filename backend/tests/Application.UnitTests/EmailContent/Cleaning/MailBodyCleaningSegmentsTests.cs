// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Cleaning;
using Xunit;

namespace MailFathom.Application.UnitTests.EmailContent.Cleaning;

/// <summary>
/// Covers the one reading that decides whether a proposed cleaning is usable at all. It is the whole of the fidelity
/// guarantee: an answer that does not partition the document's own block indices is refused here, so nothing downstream
/// has to judge whether the blocks it was handed are the blocks the reduction produced.
/// </summary>
public sealed class MailBodyCleaningSegmentsTests
{
    [Fact]
    public void Read_SegmentsCoveringEveryBlockExactlyOnce_AnswersTheKeptIndices()
    {
        // Arrange
        MailBodyCleaningSegment[] segments =
        [
            new(0, 1, Keep: false),
            new(2, 4, Keep: true),
            new(5, 5, Keep: false),
        ];

        // Act
        var kept = MailBodyCleaningSegments.Read(segments, blockCount: 6);

        // Assert
        Assert.Equal([2, 3, 4], kept);
    }

    /// <summary>A single range over the whole document is a cleaning that dropped nothing, which is an answer rather than a refusal.</summary>
    [Fact]
    public void Read_OneSegmentKeepingEveryBlock_AnswersEveryIndex()
    {
        // Act
        var kept = MailBodyCleaningSegments.Read([new MailBodyCleaningSegment(0, 2, Keep: true)], blockCount: 3);

        // Assert
        Assert.Equal([0, 1, 2], kept);
    }

    /// <summary>
    /// The shape a producer writes when it answers about what it found interesting rather than about the document: the
    /// blocks between two ranges are named by nothing, so the answer states no decision about them.
    /// </summary>
    [Fact]
    public void Read_SegmentsLeavingAGapBetweenThem_IsRefused()
    {
        // Arrange
        MailBodyCleaningSegment[] segments =
        [
            new(0, 1, Keep: true),
            new(3, 4, Keep: true),
        ];

        // Act
        var kept = MailBodyCleaningSegments.Read(segments, blockCount: 5);

        // Assert
        Assert.Null(kept);
    }

    /// <summary>Two ranges naming one block say two things about it, and neither is the answer.</summary>
    [Fact]
    public void Read_SegmentsOverlappingOneAnother_IsRefused()
    {
        // Arrange
        MailBodyCleaningSegment[] segments =
        [
            new(0, 2, Keep: true),
            new(2, 3, Keep: false),
        ];

        // Act
        var kept = MailBodyCleaningSegments.Read(segments, blockCount: 4);

        // Assert
        Assert.Null(kept);
    }

    /// <summary>An index outside the document names no block, so the answer reaches past what it was asked about.</summary>
    [Fact]
    public void Read_ASegmentReachingPastTheLastBlock_IsRefused()
    {
        // Arrange
        MailBodyCleaningSegment[] segments =
        [
            new(0, 1, Keep: false),
            new(2, 7, Keep: true),
        ];

        // Act
        var kept = MailBodyCleaningSegments.Read(segments, blockCount: 3);

        // Assert
        Assert.Null(kept);
    }

    /// <summary>
    /// The renumbered answer: a producer that dropped the blocks it did not want and then numbered what was left from
    /// zero. Every range is well formed and the set is ascending and disjoint, and it still describes a shorter document
    /// than the one asked about — which is why covering the last index is asserted rather than assumed.
    /// </summary>
    [Fact]
    public void Read_SegmentsNumberingASurvivingDocumentRatherThanTheOneAsked_IsRefused()
    {
        // Arrange
        MailBodyCleaningSegment[] segments =
        [
            new(0, 0, Keep: false),
            new(1, 2, Keep: true),
        ];

        // Act
        var kept = MailBodyCleaningSegments.Read(segments, blockCount: 6);

        // Assert
        Assert.Null(kept);
    }

    /// <summary>A range written backwards names nothing, and a reading that let it through would keep no index from it.</summary>
    [Fact]
    public void Read_ASegmentWhoseEndPrecedesItsStart_IsRefused()
    {
        // Act
        var kept = MailBodyCleaningSegments.Read([new MailBodyCleaningSegment(2, 0, Keep: true)], blockCount: 4);

        // Assert
        Assert.Null(kept);
    }

    /// <summary>An answer stating no segment at all describes a document of no blocks, and nothing else.</summary>
    [Fact]
    public void Read_NoSegmentsForADocumentThatHasBlocks_IsRefused()
    {
        // Act
        var kept = MailBodyCleaningSegments.Read([], blockCount: 2);

        // Assert
        Assert.Null(kept);
    }

    [Fact]
    public void Read_NoSegmentsForADocumentOfNoBlocks_AnswersNothingKept()
    {
        // Act
        var kept = MailBodyCleaningSegments.Read([], blockCount: 0);

        // Assert
        Assert.Empty(kept!);
    }

    /// <summary>Dropping everything partitions the document, so it is the pass above that refuses to draw nothing rather than this reading.</summary>
    [Fact]
    public void Read_SegmentsDroppingEveryBlock_AnswersNothingKept()
    {
        // Act
        var kept = MailBodyCleaningSegments.Read([new MailBodyCleaningSegment(0, 3, Keep: false)], blockCount: 4);

        // Assert
        Assert.Empty(kept!);
    }
}
