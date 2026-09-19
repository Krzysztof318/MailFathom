// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using Microsoft.Extensions.AI.Evaluation;

namespace MailFathom.Evaluations.RelevanceFilter;

/// <summary>What one model's judgements made of the labelled set, under every threshold measured.</summary>
/// <remarks>
/// Handed to <see cref="RelevanceFilterEvaluator" /> as the context it reads rather than computed inside it, because an
/// evaluator is given a conversation and an answer, and what the filter did is neither: it is a count over many lookups
/// the scenario ran. The store keeps the context beside the result, so the counts are readable in the report as well.
/// </remarks>
internal sealed class RelevanceFilterMeasurement : EvaluationContext
{
    /// <summary>The name the context is filed under.</summary>
    public const string ContextName = "Labelled candidates kept";

    /// <summary>Initializes the measurement.</summary>
    /// <param name="tallies">What was kept under each threshold, in ascending order of threshold.</param>
    /// <param name="answeringCount">How many labelled passages answer their lookup.</param>
    /// <param name="notAnsweringCount">How many labelled passages do not.</param>
    public RelevanceFilterMeasurement(
        IReadOnlyList<RelevanceFilterTally> tallies,
        int answeringCount,
        int notAnsweringCount)
        : this(tallies, answeringCount, notAnsweringCount, Describe(tallies, answeringCount, notAnsweringCount))
    {
    }

    private RelevanceFilterMeasurement(
        IReadOnlyList<RelevanceFilterTally> tallies,
        int answeringCount,
        int notAnsweringCount,
        string summary)
        : base(ContextName, summary)
    {
        this.Summary = summary;
        this.Tallies = tallies;
        this.AnsweringCount = answeringCount;
        this.NotAnsweringCount = notAnsweringCount;
    }

    /// <summary>Gets what was kept under each threshold, one line per threshold, as the report shows it.</summary>
    public string Summary { get; }

    /// <summary>Gets what was kept under each threshold, in ascending order of threshold.</summary>
    public IReadOnlyList<RelevanceFilterTally> Tallies { get; }

    /// <summary>Gets how many labelled passages answer their lookup.</summary>
    public int AnsweringCount { get; }

    /// <summary>Gets how many labelled passages do not.</summary>
    public int NotAnsweringCount { get; }

    private static string Describe(
        IReadOnlyList<RelevanceFilterTally> tallies,
        int answeringCount,
        int notAnsweringCount) =>
        string.Join(
            '\n',
            tallies.Select(tally => string.Create(
                CultureInfo.InvariantCulture,
                $"At {tally.MinimumRelevance}: {tally.AnsweringKept} of {answeringCount} answering and {tally.NotAnsweringKept} of {notAnsweringCount} not answering kept.")));
}
