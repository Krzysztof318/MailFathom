// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Retrieval;

namespace MailFathom.AI.Retrieval;

/// <summary>The validated declaration the second retrieval pass runs on: how many passages one question may pay to have judged, and how relevant one has to be to survive.</summary>
/// <remarks>
/// <para>
/// Built once, at startup, from configuration that has already been proved usable, so the filter itself never
/// revalidates and holds no defaulting logic. It exists only where a deployment turned the pass on: an instance that did
/// not registers no plan and no filter, and its retrieval is the fused ranking exactly as it was.
/// </para>
/// <para>
/// Two numbers rather than one, because they bound different things. The candidate count is what a question may spend —
/// one provider call per candidate — and the threshold is what that spend buys. A deployment that lowers the count buys
/// a weaker filter rather than a shorter result, because a passage nobody judged was never found irrelevant.
/// </para>
/// </remarks>
public sealed class PassageRelevanceFilterPlan
{
    /// <summary>The relevance of an extract with nothing to do with the query.</summary>
    /// <remarks>Published here rather than beside the reading of a judgement, because this is the type a composition root builds and the scale is what it has to state a threshold on.</remarks>
    public const int LeastRelevance = 0;

    /// <summary>The relevance of an extract that answers the query.</summary>
    public const int GreatestRelevance = 100;

    /// <summary>Gets the greatest candidate count a deployment may declare, which is everything one retrieval can hand over.</summary>
    /// <remarks>
    /// Read from the retrieval bounds rather than restated, so the two move together: the day the passage count becomes
    /// a setting, the ceiling on what may be judged follows it instead of staying at a number that was true once.
    /// </remarks>
    public static int GreatestCandidates => EmailKnowledgeBounds.Default.MaximumPassages;

    private PassageRelevanceFilterPlan(int maximumCandidates, int minimumRelevance)
    {
        this.MaximumCandidates = maximumCandidates;
        this.MinimumRelevance = minimumRelevance;
    }

    /// <summary>Gets the greatest number of passages one retrieval puts to the model.</summary>
    /// <remarks>
    /// The ceiling on what one question costs, and the reason it is stated at all: judging is a provider call per
    /// passage, so a candidate list bounded only by what a retrieval happened to return is a bill bounded by the same
    /// thing. Passages past this count keep the position the fused ranking gave them.
    /// </remarks>
    public int MaximumCandidates { get; }

    /// <summary>Gets the least relevance a judged passage may carry and still be handed over.</summary>
    /// <remarks>
    /// Stated on the same scale the model answers on, which is a whole number from <see cref="LeastRelevance" /> to
    /// <see cref="GreatestRelevance" />. How high to set it is a deployment's judgement about its own mail rather than
    /// something this system can decide: a mailbox of long threads needs a higher one than a mailbox of short
    /// exchanges.
    /// </remarks>
    public int MinimumRelevance { get; }

    /// <summary>Builds a plan, refusing a declaration no filter could run under.</summary>
    /// <param name="maximumCandidates">The greatest number of passages one retrieval puts to the model.</param>
    /// <param name="minimumRelevance">The least relevance a judged passage may carry and still be handed over.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when either value is outside the range this type accepts.</exception>
    /// <remarks>
    /// The candidate count is capped by what one retrieval hands over, which is
    /// <see cref="EmailKnowledgeBounds.MaximumPassages" /> and is a narrower number than what a search can rank. A
    /// higher count would state a ceiling no question could reach: there is never a ninth passage to judge, so a
    /// deployment writing one would be told it had widened a filter that had not moved.
    /// The threshold starts at one rather than at zero, because a threshold of zero pays for a judgement that can drop
    /// nothing.
    /// </remarks>
    public static PassageRelevanceFilterPlan Create(int maximumCandidates, int minimumRelevance)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCandidates, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumCandidates, GreatestCandidates);
        ArgumentOutOfRangeException.ThrowIfLessThan(minimumRelevance, LeastRelevance + 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minimumRelevance, GreatestRelevance);

        return new PassageRelevanceFilterPlan(maximumCandidates, minimumRelevance);
    }

    /// <inheritdoc />
    public override string ToString() => string.Format(
        CultureInfo.InvariantCulture,
        "at most {0} candidates, each judged at least {1}",
        this.MaximumCandidates,
        this.MinimumRelevance);
}
