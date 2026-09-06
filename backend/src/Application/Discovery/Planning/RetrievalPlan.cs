// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Retrieval;

namespace MailFathom.Application.Discovery.Planning;

/// <summary>How a question is looked for: the lookups to run, in order, and the point at which enough has been found.</summary>
/// <remarks>
/// <para>
/// The plan decides how retrieval is used and implements none of it. Each lookup is an ordinary
/// <see cref="EmailKnowledgeQuery" />, so the lexical, semantic, and hybrid paths this deployment already ranks with
/// are what runs — including whatever those paths admit from an attachment, which a plan neither re-derives nor
/// re-ranks around.
/// </para>
/// <para>
/// Several lookups rather than one, because a question worth asking rarely matches one wording. Ordered rather than
/// unordered, because the first lookup is the one a run pays for before it knows whether it needed the rest, and
/// <see cref="SufficientPassages" /> is what lets it stop.
/// </para>
/// </remarks>
public sealed record RetrievalPlan
{
    /// <summary>The greatest number of lookups one plan may run.</summary>
    /// <remarks>
    /// Each lookup is a ranked read over a mailbox, so the bound is what stops a plan from turning one question into an
    /// unbounded sweep. It is generous against what a question needs — a wording per language a mailbox plausibly
    /// holds, plus a narrowing or two — so meeting it means a plan enumerated rather than chose.
    /// </remarks>
    public const int MaximumLookups = 6;

    private RetrievalPlan(IReadOnlyList<EmailKnowledgeQuery> lookups, int sufficientPassages)
    {
        this.Lookups = lookups;
        this.SufficientPassages = sufficientPassages;
    }

    /// <summary>Gets the lookups to run, in the order they are worth running.</summary>
    public IReadOnlyList<EmailKnowledgeQuery> Lookups { get; }

    /// <summary>Gets the number of distinct passages at which the plan has found enough and stops running lookups.</summary>
    /// <remarks>
    /// A ceiling on what one question reads rather than a target to reach. A plan that finds enough in its first lookup
    /// leaves the rest unrun, and one that never reaches this number runs every lookup and answers from what it found.
    /// </remarks>
    public int SufficientPassages { get; }

    /// <summary>Composes the plan a run retrieves by.</summary>
    /// <param name="retrievalBounds">What this deployment's retrieval will return at most, which bounds what enough can mean.</param>
    /// <param name="lookups">The lookups to run, in order, at least one and at most <see cref="MaximumLookups" />.</param>
    /// <param name="sufficientPassages">The number of distinct passages at which the plan stops.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="ArgumentException">No lookup was given, more than <see cref="MaximumLookups" /> were, or one of them was <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sufficientPassages" /> is below one or above what <paramref name="retrievalBounds" /> returns.</exception>
    public static RetrievalPlan Create(
        EmailKnowledgeBounds retrievalBounds,
        IReadOnlyList<EmailKnowledgeQuery> lookups,
        int sufficientPassages)
    {
        ArgumentNullException.ThrowIfNull(retrievalBounds);
        ArgumentNullException.ThrowIfNull(lookups);

        if (lookups.Count is 0)
        {
            throw new ArgumentException("A retrieval plan that runs no lookup retrieves nothing.", nameof(lookups));
        }

        if (lookups.Count > MaximumLookups)
        {
            throw new ArgumentException(
                $"A retrieval plan runs at most {MaximumLookups} lookups.",
                nameof(lookups));
        }

        if (lookups.Any(static lookup => lookup is null))
        {
            throw new ArgumentException("A retrieval plan holds no absent lookup.", nameof(lookups));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(sufficientPassages, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sufficientPassages, retrievalBounds.MaximumPassages);

        return new RetrievalPlan([.. lookups], sufficientPassages);
    }

    /// <inheritdoc />
    public override string ToString() => string.Format(
        CultureInfo.InvariantCulture,
        "{0} lookups, enough at {1} passages",
        this.Lookups.Count,
        this.SufficientPassages);
}
