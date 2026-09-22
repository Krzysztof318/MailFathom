// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.Search;

/// <summary>Combines two rankings of the same emails into one, reading only where each ranking placed them.</summary>
/// <remarks>
/// <para>
/// Reciprocal Rank Fusion is the method, and the reason it is the method is that the two inputs
/// are not on one scale and never will be. A full-text rank is a function of term frequency and document length; a
/// vector distance is a function of a model's geometry. Adding, averaging, or min-max normalizing them would ask which
/// number is worth more, and every answer to that would be a constant that a change of embedding model silently
/// invalidates. Fusion by rank asks nothing of the numbers at all — only of the order they put the documents in — so
/// changing the model changes which documents are found and never how the two findings are weighed.
/// </para>
/// <para>
/// The formula is the published one: a document scores <c>1 / (k + rank)</c> in each ranking that returned it, counting
/// ranks from one, and its fused score is the sum over both. A document only one ranking found therefore still scores,
/// which is what lets a semantic match with no shared word rank at all — and a document both found outranks either of
/// them, which is what keeps an exact phrase from being displaced by a merely related message.
/// </para>
/// <para>
/// The result is deterministic for a given pair of inputs. Equal fused scores are not rare here and are not a
/// tie-breaking detail: two documents at symmetric places — first lexically and fifth semantically against fifth and
/// first — score identically by construction. The timeline order settles those, which is the same order that settles a
/// tie in the lexical ranking alone, so a search returns one sequence rather than whichever the sums happened to
/// produce.
/// </para>
/// </remarks>
public static class ReciprocalRankFusion
{
    /// <summary>The constant that flattens the difference between the very top places.</summary>
    /// <remarks>
    /// Sixty, from the paper the method comes from, and a constant rather than a setting. Its role is to keep one
    /// ranking's first place from dominating the sum: at <c>k = 60</c> the gap between rank one and rank two is small
    /// enough that agreement between the two rankings outweighs a single confident first place, which is the behavior
    /// hybrid retrieval is wanted for. A deployment-tunable value would be a second way to make two instances disagree
    /// about what "most relevant" means while both reported the same retrieval mode.
    /// </remarks>
    public const int RankConstant = 60;

    /// <summary>Fuses a lexical ranking and a semantic ranking into one bounded ordering.</summary>
    /// <param name="lexicalCandidates">The lexical ranking, best first.</param>
    /// <param name="semanticCandidates">The semantic ranking, nearest first.</param>
    /// <param name="limit">The greatest number of fused results to return, at least one.</param>
    /// <returns>At most <paramref name="limit" /> candidates, best fused score first, each carrying that score.</returns>
    /// <exception cref="ArgumentNullException">Thrown when either ranking is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="limit" /> is below one.</exception>
    /// <remarks>
    /// The place a candidate holds is its index in the sequence it arrived in, so a caller passes each ranking in the
    /// order its producer returned rather than sorting it first. A candidate appearing twice within one ranking is
    /// scored at its best place there, because a duplicate would otherwise let one ranking cast two votes.
    /// </remarks>
    public static IReadOnlyList<RankedEmailCandidate> Fuse(
        IReadOnlyList<RankedEmailCandidate> lexicalCandidates,
        IReadOnlyList<RankedEmailCandidate> semanticCandidates,
        int limit)
    {
        ArgumentNullException.ThrowIfNull(lexicalCandidates);
        ArgumentNullException.ThrowIfNull(semanticCandidates);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var positions = new Dictionary<StoredEmailId, EmailTimelinePosition>();

        // Both producers derive a position from the same stored columns, so the two agree; taking the first keeps that
        // agreement from depending on which ranking is read last.
        foreach (var candidate in lexicalCandidates.Concat(semanticCandidates))
        {
            _ = positions.TryAdd(candidate.StoredEmailId, candidate.Position);
        }

        var fusedScores = Score(
            [.. lexicalCandidates.Select(static candidate => candidate.StoredEmailId)],
            [.. semanticCandidates.Select(static candidate => candidate.StoredEmailId)]);

        return
        [
            .. fusedScores
                .Select(scored => new RankedEmailCandidate(positions[scored.Key], scored.Value))
                .Order(RankedEmailCandidate.BestFirst)
                .Take(limit),
        ];
    }

    /// <summary>Scores every document either ranking placed by the places it holds in them, and orders nothing.</summary>
    /// <typeparam name="TDocument">What the two rankings rank, compared by its own equality.</typeparam>
    /// <param name="lexicalRanking">The lexical ranking, best first.</param>
    /// <param name="semanticRanking">The semantic ranking, nearest first.</param>
    /// <returns>Each document either ranking returned, with its fused score.</returns>
    /// <exception cref="ArgumentNullException">Thrown when either ranking is <see langword="null" />.</exception>
    /// <remarks>
    /// The arithmetic <see cref="Fuse" /> applies to mail, published for a ranking of anything else. Ordering is left to
    /// the caller, because what settles a tie is a property of what is ranked rather than of fusion: the timeline for
    /// mail, a conversation's own recency for the Agent's history. A document repeated within one ranking scores once,
    /// at its best place there.
    /// </remarks>
    public static IReadOnlyDictionary<TDocument, float> Score<TDocument>(
        IReadOnlyList<TDocument> lexicalRanking,
        IReadOnlyList<TDocument> semanticRanking)
        where TDocument : notnull
    {
        ArgumentNullException.ThrowIfNull(lexicalRanking);
        ArgumentNullException.ThrowIfNull(semanticRanking);

        var fusedScores = new Dictionary<TDocument, float>();

        Accumulate(lexicalRanking, fusedScores);
        Accumulate(semanticRanking, fusedScores);

        return fusedScores;
    }

    /// <summary>Adds one ranking's reciprocal contributions to the running fused scores.</summary>
    /// <remarks>The first appearance of a document decides its place, which is what makes a repeated one score once.</remarks>
    private static void Accumulate<TDocument>(IReadOnlyList<TDocument> ranking, Dictionary<TDocument, float> fusedScores)
        where TDocument : notnull
    {
        var seen = new HashSet<TDocument>();

        for (var index = 0; index < ranking.Count; index++)
        {
            var document = ranking[index];

            if (!seen.Add(document))
            {
                continue;
            }

            fusedScores[document] = fusedScores.GetValueOrDefault(document) + (1f / (RankConstant + index + 1));
        }
    }
}
