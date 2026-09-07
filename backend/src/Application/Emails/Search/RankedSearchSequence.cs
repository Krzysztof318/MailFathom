// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.Search;

/// <summary>One published ranking, and which of its members are in it only because a picture was near the query.</summary>
/// <remarks>
/// <para>
/// The sequence descends under <see cref="RankedEmailCandidate.BestFirst" /> across both of its sections, which is what
/// lets a keyset cursor walk it: the depicted tail is scored strictly between zero and the least fused score rather than
/// carrying a distance, because a fused score descends while a distance ascends and joining the two end to end would
/// produce a sequence monotone in neither direction.
/// </para>
/// <para>
/// <see cref="DepictedOnly" /> is what a result is marked from. It names the messages the fused ranking did not carry at
/// all, so a message with both a written passage and a described picture is absent from it — the picture contributed
/// nothing to where that message sits, and saying otherwise would report a photograph as the reason for a place a body
/// earned.
/// </para>
/// </remarks>
/// <param name="Candidates">The whole published ordering, best first, the depicted section last.</param>
/// <param name="DepictedOnly">The candidates a description alone put in the ordering.</param>
public sealed record RankedSearchSequence(
    IReadOnlyList<RankedEmailCandidate> Candidates,
    IReadOnlySet<StoredEmailId> DepictedOnly)
{
    /// <summary>The ordering of a search that placed nothing.</summary>
    public static RankedSearchSequence Empty { get; } = new([], new HashSet<StoredEmailId>());

    /// <summary>Wraps an ordering no depicted match took part in, which is every lexical-only ranking.</summary>
    /// <param name="candidates">The ordering, best first.</param>
    /// <returns>The sequence, with nothing marked as depicted.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="candidates" /> is <see langword="null" />.</exception>
    public static RankedSearchSequence Written(IReadOnlyList<RankedEmailCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        return new RankedSearchSequence(candidates, new HashSet<StoredEmailId>());
    }
}
