// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Search;

/// <summary>The two orderings one semantic ranking produces, separated by who the words behind a match belong to.</summary>
/// <remarks>
/// <para>
/// One eligible set, two rankings, because
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0030-describing-an-image-attachment-in-words-and-ranking-a-depicted-match-below-a-written-one.md">ADR 0030</see>
/// expresses the floor as a partition of the result rather than as a weight inside it. Only
/// <see cref="Written" /> enters reciprocal rank fusion, so nothing about fusion changes and no constant is introduced;
/// <see cref="Depicted" /> is reduced to what the fused result does not already carry and appended after it whole.
/// </para>
/// <para>
/// A message appears in <see cref="Written" /> at the place its nearest <em>written</em> passage earned it rather than
/// its nearest passage of any kind. Ordering by the nearest passage overall and labelling the result afterwards would
/// let a description decide where a message sits while the result claimed a body placed it — the floor would appear to
/// hold in the labels and fail in the order.
/// </para>
/// <para>
/// A message may appear in both, and that is not a duplicate to remove here: it means the message has a written passage
/// and a described picture that are each near the query, and the partition keeps it once, at the place the written one
/// earned.
/// </para>
/// </remarks>
/// <param name="Written">Messages ordered by their nearest passage of something a person wrote — message text, or a document attachment's own words.</param>
/// <param name="Depicted">Messages ordered by their nearest passage of a model's description of a picture.</param>
public sealed record SemanticEmailRankings(
    IReadOnlyList<RankedEmailCandidate> Written,
    IReadOnlyList<RankedEmailCandidate> Depicted)
{
    /// <summary>The rankings of a query nothing was near, which is what an instance with no embedded mail produces.</summary>
    public static SemanticEmailRankings Empty { get; } = new([], []);
}
