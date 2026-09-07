// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.Search;

/// <summary>Composes one published ordering out of a lexical ranking and the two rankings meaning produces.</summary>
/// <remarks>
/// <para>
/// The one step a surface calls, rather than a rule each of them remembers.
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0030-describing-an-image-attachment-in-words-and-ranking-a-depicted-match-below-a-written-one.md">ADR 0030</see>
/// requires exactly that: a guarantee that has to be remembered in two places is one that holds in one of them after the
/// next change to the other. <c>search_emails</c> and an answering run's retrieval publish through it today, both
/// reaching it through <c>MailboxSearchReader</c>. The client's search route does not yet: <c>MailSearchBrowser</c>
/// fuses the written ranking alone and withholds the depicted tail, because a client row a description put there needs
/// a way to say so and <see href="https://github.com/Krzysztof318/MailFathom/issues/1559">#1559</see> owns that shape.
/// It joins this step when it gains one.
/// </para>
/// <para>
/// What it guarantees is stronger than "a picture never outranks words" and simpler to test: <b>a picture never improves
/// a message's place, and only ever adds a message that would not have been in the result at all.</b> The written
/// ranking is fused with the lexical one exactly as the single semantic ranking was, so nothing about fusion changes;
/// the depicted ranking is then reduced to what the fused result does not carry and appended after it.
/// </para>
/// <para>
/// Both sections share one depth, and it is the depth the surface already had. A caller's limit applies to the
/// concatenation, so the depicted section occupies what the written results leave of it and nothing more — giving the
/// tail a depth of its own would publish a list past a bound that exists to limit how much of a mailbox one query walks
/// out of the deployment.
/// </para>
/// </remarks>
public static class HybridSearchRanking
{
    /// <summary>How far below the least fused score the first depicted result is placed.</summary>
    /// <remarks>
    /// Halfway rather than adjacent, so every depicted score is comfortably clear of the fused section under the
    /// single-precision arithmetic the sequence is compared in. The value decides nothing about the ordering — the
    /// partition has already placed the whole section below, unconditionally and without reference to any score — and is
    /// therefore a representation rather than the weighting constant ADR 0030 refused: no change of embedding model can
    /// invalidate it, because no distance survives into it.
    /// </remarks>
    private const float DepictedHeadroom = 0.5f;

    /// <summary>Fuses the written rankings and appends what a description alone found, as one bounded ordering.</summary>
    /// <param name="lexicalCandidates">The lexical ranking, best first.</param>
    /// <param name="semanticRankings">The two orderings meaning produced, each nearest first.</param>
    /// <param name="limit">The greatest number of results the concatenation may carry, at least one.</param>
    /// <returns>The published ordering, and which of its members a description alone put there.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="lexicalCandidates" /> or <paramref name="semanticRankings" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="limit" /> is below one.</exception>
    /// <remarks>
    /// Every score the result carries is finite, positive, and descending, which is what a keyset cursor into this
    /// sequence requires and what a boundary composed rather than ranked would break. A fused score is a small positive
    /// sum of reciprocals, so the depicted section is scaled beneath the least of them rather than subtracted from it:
    /// subtracting would yield a negative boundary and refuse every page that ended in the tail.
    /// </remarks>
    public static RankedSearchSequence Compose(
        IReadOnlyList<RankedEmailCandidate> lexicalCandidates,
        SemanticEmailRankings semanticRankings,
        int limit)
    {
        ArgumentNullException.ThrowIfNull(lexicalCandidates);
        ArgumentNullException.ThrowIfNull(semanticRankings);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var fused = ReciprocalRankFusion.Fuse(lexicalCandidates, semanticRankings.Written, limit);

        var written = lexicalCandidates
            .Concat(semanticRankings.Written)
            .Select(static candidate => candidate.StoredEmailId)
            .ToHashSet();

        var appended = Appended(fused, semanticRankings.Depicted, written, limit);
        if (appended.Count is 0)
        {
            return RankedSearchSequence.Written(fused);
        }

        return new RankedSearchSequence(
            [.. fused, .. appended],
            appended.Select(static candidate => candidate.StoredEmailId).ToHashSet());
    }

    /// <summary>Reduces the depicted ranking to what no written ranking reached and scores it beneath the fused result.</summary>
    /// <remarks>
    /// <para>
    /// The exclusion is read from the two written rankings themselves rather than from the fused list they produced,
    /// which is already cut to the caller's limit while its inputs were read to several times that depth. Reading it
    /// from the cut list would give the same answer today, but only by arithmetic: a written candidate is dropped from
    /// the fusion exactly when the union filled the limit, and the section below is then empty anyway. That leaves what
    /// <see cref="EmailSearchMatch.IsDepictedMatch" /> promises — never true of a message a written passage reached —
    /// resting on the two uses of <c>limit</c> in <see cref="Compose" /> being the same number, which is a coincidence
    /// a later change to either could end without any of this reading as wrong.
    /// </para>
    /// <para>
    /// A candidate the depicted ranking repeats is taken at its first place, on the same rule fusion accumulates by: a
    /// duplicate would otherwise occupy two of the places the shared depth leaves for messages nothing else found.
    /// </para>
    /// </remarks>
    private static List<RankedEmailCandidate> Appended(
        IReadOnlyList<RankedEmailCandidate> fused,
        IReadOnlyList<RankedEmailCandidate> depicted,
        HashSet<StoredEmailId> alreadyPlaced,
        int limit)
    {
        var remaining = limit - fused.Count;
        if (remaining <= 0 || depicted.Count is 0)
        {
            return [];
        }

        var admitted = new List<RankedEmailCandidate>(remaining);

        // A loop rather than a filtered projection: admitting a candidate is what records that it has been admitted, so
        // the decision mutates the set it reads and a LINQ predicate would be the place that side effect happens.
        foreach (var candidate in depicted)
        {
            if (admitted.Count == remaining)
            {
                break;
            }

            if (alreadyPlaced.Add(candidate.StoredEmailId))
            {
                admitted.Add(candidate with { Score = DepictedScore(fused, admitted.Count) });
            }
        }

        return admitted;
    }

    /// <summary>Scores one depicted result by its own place, mapped strictly between zero and the least fused score.</summary>
    /// <remarks>
    /// Where the fused result is empty the depicted section is the whole list and carries its reciprocal ranks unmapped,
    /// those being positive and descending already. Where it is not, the section is scaled so its first member sits at
    /// <see cref="DepictedHeadroom" /> of the least fused score: every value stays positive and finite, and the whole
    /// published list descends under <see cref="RankedEmailCandidate.BestFirst" />.
    /// </remarks>
    private static float DepictedScore(IReadOnlyList<RankedEmailCandidate> fused, int index)
    {
        var reciprocalRank = ReciprocalRank(index);

        return fused.Count is 0
            ? reciprocalRank
            : fused[^1].Score * DepictedHeadroom * (reciprocalRank / ReciprocalRank(0));
    }

    /// <summary>The contribution a place makes under the same formula fusion scores by, counting ranks from one.</summary>
    private static float ReciprocalRank(int index) => 1f / (ReciprocalRankFusion.RankConstant + index + 1);
}
