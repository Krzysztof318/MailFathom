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
/// unordered, because the order settles which lookup's passage goes first where two share a rank — but never which
/// lookup deserves the whole of <see cref="PassageAllowance" />, since each is given its share of it.
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

    /// <summary>How many passages every lookup may hand over, whatever number of passages the plan called enough.</summary>
    /// <remarks>
    /// A plan writes several wordings because it cannot know which one the mail uses, and the one that does rarely
    /// ranks the evidence first: the other messages sharing its words come before it. A model's judgement of how many
    /// extracts would answer is routinely one, which on its own would hand over the first lookup's best passage and
    /// never run the rest. Four is where the evaluation corpus stops losing evidence that a lookup of the plan reached,
    /// and at the passage lengths a deployment cuts, four passages for each of the most lookups a plan may hold stay far
    /// inside the characters one question may retrieve.
    /// </remarks>
    public const int PassagesAssuredPerLookup = 4;

    private RetrievalPlan(IReadOnlyList<EmailKnowledgeQuery> lookups, int sufficientPassages, int passageAllowance)
    {
        this.Lookups = lookups;
        this.SufficientPassages = sufficientPassages;
        this.PassageAllowance = passageAllowance;
    }

    /// <summary>Gets the lookups to run, in the order they are worth running.</summary>
    public IReadOnlyList<EmailKnowledgeQuery> Lookups { get; }

    /// <summary>Gets the number of distinct passages the planning judged would answer the question.</summary>
    /// <remarks>
    /// A judgement rather than the bound a run keeps to: <see cref="PassageAllowance" /> is that, and it only follows
    /// this number where the number asks for more than every lookup is assured anyway.
    /// </remarks>
    public int SufficientPassages { get; }

    /// <summary>Gets the number of distinct passages a run may hand over to answer from.</summary>
    /// <remarks>
    /// The larger of <see cref="SufficientPassages" /> and <see cref="PassagesAssuredPerLookup" /> for every lookup,
    /// within what one retrieval may return. It is divided between the lookups rather than spent by whichever runs
    /// first: each lookup is admitted up to its equal share, and what a lookup found beyond that fills only what the
    /// others left unspent. So every lookup runs, and one that never reaches this number answers from what it found.
    /// </remarks>
    public int PassageAllowance { get; }

    /// <summary>Composes the plan a run retrieves by.</summary>
    /// <param name="retrievalBounds">What this deployment's retrieval will return at most, which bounds what enough can mean.</param>
    /// <param name="lookups">The lookups to run, in order, at least one and at most <see cref="MaximumLookups" />.</param>
    /// <param name="sufficientPassages">The number of distinct passages the planning judged would answer the question.</param>
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

        return new RetrievalPlan(
            [.. lookups],
            sufficientPassages,
            Math.Min(retrievalBounds.MaximumPassages, Math.Max(sufficientPassages, lookups.Count * PassagesAssuredPerLookup)));
    }

    /// <inheritdoc />
    public override string ToString() => string.Format(
        CultureInfo.InvariantCulture,
        "{0} lookups, enough at {1} passages",
        this.Lookups.Count,
        this.SufficientPassages);
}
