// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.Search;

/// <summary>One email an ordering placed, before anything has been read about it beyond where it placed.</summary>
/// <remarks>
/// <para>
/// A candidate is what a ranking produces and what fusion consumes. It deliberately carries no summary and no snippet:
/// combining two orderings needs the identity and the place, and reading a message's projection for a candidate that
/// the fusion then drops would be a body cut and a row projected for a result nobody receives.
/// </para>
/// <para>
/// The identity travels inside <see cref="Position" /> rather than beside it, because the position already holds the
/// stable local identifier — it is the tie-breaker that makes the timeline order total. Carrying it twice would allow
/// two answers to the same question.
/// </para>
/// <para>
/// <see cref="Score" /> belongs to the ordering that produced it and to nothing else. A lexical ranking scores higher
/// for a better match, a vector search scores lower for a nearer one, and a fusion scores in units of neither; the only
/// property every producer shares is that the sequence it returns is already in its own best-first order. Nothing
/// compares a score across two orderings, which is exactly the calibration Reciprocal Rank Fusion exists to avoid.
/// </para>
/// </remarks>
/// <param name="Position">Where the email sits in the timeline order, which is also its stable local identity.</param>
/// <param name="Score">What the producing ordering scored this email, meaningful only within that one ordering.</param>
public sealed record RankedEmailCandidate(EmailTimelinePosition Position, float Score)
{
    /// <summary>Gets the order a ranked result is published in: best score first, ties settled by the timeline.</summary>
    /// <remarks>
    /// <para>
    /// The order lives on the candidate rather than on any one producer because two of them need the same answer. A
    /// fusion sorts the scores it computed into it, and a paged search continues a walk through it — and a boundary
    /// compared under one order while the sequence was built under another would repeat a result at one page edge and
    /// skip one at the next.
    /// </para>
    /// <para>
    /// Equal scores are not rare and are not a detail. Two documents at symmetric places in a fusion score identically
    /// by construction, and a full-text rank ties whenever several messages carry an uncommon word equally often; the
    /// timeline order settles both, which is what makes the sequence total and therefore pageable.
    /// </para>
    /// <para>
    /// It orders candidates of one ranking. Nothing here compares a score across two of them, which is the calibration
    /// <see cref="ReciprocalRankFusion" /> exists to avoid.
    /// </para>
    /// </remarks>
    public static IComparer<RankedEmailCandidate> BestFirst { get; } = new BestFirstComparer();

    /// <summary>Gets the stable local identity of the candidate email.</summary>
    public StoredEmailId StoredEmailId => this.Position.StoredEmailId;

    /// <summary>Orders candidates by score, with the timeline settling a tie.</summary>
    private sealed class BestFirstComparer : IComparer<RankedEmailCandidate>
    {
        public int Compare(RankedEmailCandidate? x, RankedEmailCandidate? y)
        {
            var byScore = y!.Score.CompareTo(x!.Score);

            return byScore is not 0
                ? byScore
                : EmailTimelinePosition.NewestFirst.Compare(x.Position, y.Position);
        }
    }
}
