// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Cleaning;

/// <summary>Decides whether a set of proposed ranges describes one document, and which blocks it keeps.</summary>
/// <remarks>
/// <para>
/// <b>It accepts nothing it cannot check and repairs nothing.</b> Every other reading of a producer's answer in this
/// system drops what it cannot back and keeps the rest, because a statement is independent of the statements beside it. A
/// cleaning is not: the ranges are a partition of the document, so a missing range is a block whose fate nobody stated
/// and an overlapping pair is two contradictory statements about one block. Repairing either would mean this build
/// deciding what to show a reader and reporting it as a model's decision.
/// </para>
/// <para>
/// So the bar is that the ranges are ascending, disjoint, and cover every index exactly once. A set that is not is
/// refused whole, and what the caller does about that is ask again once and then serve the ordinary reduced document.
/// The two shapes this catches most often are a producer that renumbered the document as it went and one that answered
/// about the blocks it found interesting, and neither is distinguishable from a correct answer by reading one range.
/// </para>
/// </remarks>
public static class MailBodyCleaningSegments
{
    /// <summary>Reads a proposal as the blocks it keeps, or refuses it.</summary>
    /// <param name="segments">The ranges as a producer wrote them.</param>
    /// <param name="blockCount">How many top-level blocks the document holds, which the ranges have to cover exactly.</param>
    /// <returns>The indices to keep, in ascending order, or <see langword="null" /> where the ranges do not describe the document.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="segments" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="blockCount" /> is negative.</exception>
    public static IReadOnlyList<int>? Read(IReadOnlyList<MailBodyCleaningSegment> segments, int blockCount)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentOutOfRangeException.ThrowIfNegative(blockCount);

        // A document of no blocks is covered by no range, and by nothing else: a range over an empty document names an
        // index that does not exist, which the walk below refuses on its own.
        if (segments.Count == 0)
        {
            return blockCount == 0 ? [] : null;
        }

        var kept = new List<int>();
        var expected = 0;

        foreach (var segment in segments)
        {
            // One condition covers the gap, the overlap, the descending range and the index out of range at once,
            // because all four are the same fact: the next range has to start where the last one ended. Naming them
            // separately would be four readings of one invariant, and a producer is told nothing either way.
            if (segment.From != expected || segment.To < segment.From || segment.To >= blockCount)
            {
                return null;
            }

            if (segment.Keep)
            {
                kept.AddRange(Enumerable.Range(segment.From, segment.To - segment.From + 1));
            }

            expected = segment.To + 1;
        }

        return expected == blockCount ? kept : null;
    }
}
