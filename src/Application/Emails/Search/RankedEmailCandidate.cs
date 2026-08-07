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
    /// <summary>Gets the stable local identity of the candidate email.</summary>
    public StoredEmailId StoredEmailId => this.Position.StoredEmailId;
}
