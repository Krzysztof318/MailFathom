// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using BenchmarkDotNet.Attributes;
using MailFathom.Application.Emails.Search;
using MailFathom.Domain.Emails;

namespace MailFathom.Benchmarks;

/// <summary>How long fusing a lexical and a semantic ranking takes, and what it allocates.</summary>
/// <remarks>
/// The one part of a hybrid search that happens in this process rather than in PostgreSQL, and it runs on every search
/// a reader waits on. The two rankings overlap deliberately, because the fusion's cost is in the candidates that appear
/// in both and a pair of disjoint rankings would measure the cheap half of it.
/// </remarks>
public class RankingBenchmarks
{
    /// <summary>How many candidates each ranking carries, which is a window a retrieval mode actually reads.</summary>
    private const int CandidatesPerRanking = 200;

    /// <summary>How many fused candidates the caller keeps.</summary>
    private const int FusedLimit = 50;

    /// <summary>How far the semantic ranking's identifiers are shifted, so half of each ranking is shared.</summary>
    private const int SemanticOffset = CandidatesPerRanking / 2;

    private IReadOnlyList<RankedEmailCandidate> lexicalCandidates = [];
    private IReadOnlyList<RankedEmailCandidate> semanticCandidates = [];

    /// <summary>Builds the two rankings every iteration fuses, outside what is measured.</summary>
    [GlobalSetup]
    public void BuildRankings()
    {
        this.lexicalCandidates = Ranking(firstOrdinal: 0);
        this.semanticCandidates = Ranking(SemanticOffset);
    }

    /// <summary>Fuses the two rankings into one bounded window.</summary>
    /// <returns>The fused window, returned so nothing about the fusion can be optimized away.</returns>
    [Benchmark]
    public IReadOnlyList<RankedEmailCandidate> Fuse() =>
        ReciprocalRankFusion.Fuse(this.lexicalCandidates, this.semanticCandidates, FusedLimit);

    /// <summary>Builds one ranking of consecutively identified candidates, best first.</summary>
    /// <remarks>
    /// The identifiers are derived from the ordinal rather than drawn at random, so two runs of this benchmark fuse the
    /// same candidates and the number it reports is about the fusion rather than about which identifiers it met.
    /// </remarks>
    private static RankedEmailCandidate[] Ranking(int firstOrdinal) =>
    [
        .. Enumerable.Range(firstOrdinal, CandidatesPerRanking).Select(ordinal => new RankedEmailCandidate(
            new EmailTimelinePosition(
                new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero).AddMinutes(-ordinal),
                StoredEmailId.Create(new Guid(ordinal + 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0))),
            1f / (ordinal + 1))),
    ];
}
