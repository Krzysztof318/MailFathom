// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.AI.Retrieval;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace MailFathom.Evaluations.RelevanceFilter;

/// <summary>Turns what the filter kept under each threshold into metrics, and holds the deployment's default against them.</summary>
/// <remarks>
/// <para>
/// No model is asked anything here. The filter is a classifier and the labels are its ground truth, so what is measured is
/// agreement — how many passages that answer survived and how many that do not — rather than a quality a judge would
/// grade. That is also why the store this is filed in is opened without a judge at all.
/// </para>
/// <para>
/// The floor is held at <see cref="PassageRelevanceFilterPlan.DefaultMinimumRelevance" /> alone, because that is the
/// threshold a deployment declaring none runs with; the other thresholds are reported so the effect of moving it is
/// visible rather than assumed, and fail nothing.
/// </para>
/// </remarks>
internal sealed class RelevanceFilterEvaluator : IEvaluator
{
    /// <summary>The name of the metric naming the threshold that separates the labelled set best.</summary>
    public const string BestThresholdMetricName = "Best threshold";

    /// <summary>The least share of answering passages a model has to keep at the default threshold.</summary>
    /// <remarks>
    /// Losing an answer is the costlier mistake: a passage the filter drops is evidence no later step can recover, while
    /// one it keeps wrongly costs the answering model some of its budget. So the floor lets one answer in five go and no
    /// more.
    /// </remarks>
    public const double MinimumAnsweringKeptShare = 0.8;

    /// <summary>The greatest share of passages that do not answer a model may keep at the default threshold.</summary>
    /// <remarks>
    /// Looser than the floor above for the reason it gives, and still tight enough that a model keeping most of what it
    /// was shown — which is a filter doing nothing while being paid per candidate — falls under it.
    /// </remarks>
    public const double MaximumNotAnsweringKeptShare = 0.25;

    /// <summary>Gets the thresholds the filter is run under, which include the deployment's default.</summary>
    public static IReadOnlyList<int> Thresholds { get; } = [10, 20, 30, 40, 50, 60, 70, 80, 90];

    /// <inheritdoc />
    public IReadOnlyCollection<string> EvaluationMetricNames { get; } =
        [.. Thresholds.SelectMany(static threshold => new[] { AnsweringKeptName(threshold), NotAnsweringKeptName(threshold) }), BestThresholdMetricName];

    /// <summary>Names the metric counting the answering passages kept under a threshold.</summary>
    /// <param name="threshold">The threshold.</param>
    /// <returns>The metric's name.</returns>
    public static string AnsweringKeptName(int threshold) =>
        string.Create(CultureInfo.InvariantCulture, $"Answering kept at {threshold}");

    /// <summary>Names the metric counting the passages that do not answer kept under a threshold.</summary>
    /// <param name="threshold">The threshold.</param>
    /// <returns>The metric's name.</returns>
    public static string NotAnsweringKeptName(int threshold) =>
        string.Create(CultureInfo.InvariantCulture, $"Not answering kept at {threshold}");

    /// <inheritdoc />
    public ValueTask<EvaluationResult> EvaluateAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        ChatConfiguration? chatConfiguration = null,
        IEnumerable<EvaluationContext>? additionalContext = null,
        CancellationToken cancellationToken = default)
    {
        var measurement = additionalContext?.OfType<RelevanceFilterMeasurement>().SingleOrDefault()
            ?? throw new InvalidOperationException(
                $"{nameof(RelevanceFilterEvaluator)} reads what the filter kept from a {nameof(RelevanceFilterMeasurement)}, and none was given.");

        EvaluationMetric[] metrics =
        [
            .. measurement.Tallies.SelectMany(tally => KeptMetrics(tally, measurement)),
            BestThreshold(measurement),
        ];

        return ValueTask.FromResult(new EvaluationResult(metrics));
    }

    private static IEnumerable<NumericMetric> KeptMetrics(RelevanceFilterTally tally, RelevanceFilterMeasurement measurement)
    {
        var answering = new NumericMetric(
            AnsweringKeptName(tally.MinimumRelevance),
            tally.AnsweringKept,
            Kept(tally.AnsweringKept, measurement.AnsweringCount, "answer", tally.MinimumRelevance));
        var notAnswering = new NumericMetric(
            NotAnsweringKeptName(tally.MinimumRelevance),
            tally.NotAnsweringKept,
            Kept(tally.NotAnsweringKept, measurement.NotAnsweringCount, "do not answer", tally.MinimumRelevance));

        if (tally.MinimumRelevance is PassageRelevanceFilterPlan.DefaultMinimumRelevance)
        {
            answering.Interpretation = tally.LookupsFellBack > 0
                ? FellBack(tally)
                : AnsweringFloor(tally, measurement.AnsweringCount);
            notAnswering.Interpretation = tally.LookupsFellBack > 0
                ? FellBack(tally)
                : NotAnsweringCeiling(tally, measurement.NotAnsweringCount);
        }

        return [answering, notAnswering];
    }

    /// <summary>Fails both metrics of a tally the model left partly unjudged, for the one reason they share.</summary>
    /// <remarks>
    /// A lookup the filter fell back on keeps every candidate, so both counts read the ranking rather than the filter;
    /// judging either against its bound would give the report a second, misleading explanation of the same failure.
    /// </remarks>
    private static EvaluationMetricInterpretation FellBack(RelevanceFilterTally tally) =>
        new(
            EvaluationRating.Unacceptable,
            failed: true,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{tally.LookupsFellBack} lookup(s) were handed over unjudged because the model did not answer, so these counts are the ranking's rather than the filter's."));

    private static EvaluationMetricInterpretation AnsweringFloor(RelevanceFilterTally tally, int answeringCount)
    {
        var share = (double)tally.AnsweringKept / answeringCount;
        var failed = share < MinimumAnsweringKeptShare;

        return new EvaluationMetricInterpretation(
            failed ? EvaluationRating.Unacceptable : EvaluationRating.Good,
            failed,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Kept {share:P0} of the passages that answer, against a floor of {MinimumAnsweringKeptShare:P0}."));
    }

    private static EvaluationMetricInterpretation NotAnsweringCeiling(RelevanceFilterTally tally, int notAnsweringCount)
    {
        var share = (double)tally.NotAnsweringKept / notAnsweringCount;
        var failed = share > MaximumNotAnsweringKeptShare;

        return new EvaluationMetricInterpretation(
            failed ? EvaluationRating.Unacceptable : EvaluationRating.Good,
            failed,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Kept {share:P0} of the passages that do not answer, against a ceiling of {MaximumNotAnsweringKeptShare:P0}."));
    }

    /// <summary>Names the thresholds that best separate what answers from what does not, and where the default stands.</summary>
    /// <remarks>
    /// Separation is the share of answering passages kept less the share of the others kept, compared as whole numbers
    /// over a common denominator so two thresholds that separate equally are never told apart by rounding. The lowest of
    /// the best is the value, because between two equal separations the one dropping less is the safer. It fails nothing:
    /// a default that separates worse than another threshold is a finding for an issue, not a reason to fail a model.
    /// </remarks>
    private static NumericMetric BestThreshold(RelevanceFilterMeasurement measurement)
    {
        long Separation(RelevanceFilterTally tally) =>
            ((long)tally.AnsweringKept * measurement.NotAnsweringCount)
            - ((long)tally.NotAnsweringKept * measurement.AnsweringCount);

        var bestSeparation = measurement.Tallies.Max(Separation);
        var best = measurement.Tallies.Where(tally => Separation(tally) == bestSeparation).ToArray();
        var atDefault = measurement.Tallies.Single(static tally =>
            tally.MinimumRelevance is PassageRelevanceFilterPlan.DefaultMinimumRelevance);
        var defaultIsBest = best.Contains(atDefault);

        var standing = defaultIsBest
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"The default of {atDefault.MinimumRelevance} is among them.")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"The default of {atDefault.MinimumRelevance} keeps {atDefault.AnsweringKept} and {atDefault.NotAnsweringKept}, which separates worse.");

        var metric = new NumericMetric(
            BestThresholdMetricName,
            best[0].MinimumRelevance,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{string.Join(", ", best.Select(static tally => tally.MinimumRelevance))} separate the labelled set best, keeping {best[0].AnsweringKept} of {measurement.AnsweringCount} passages that answer and {best[0].NotAnsweringKept} of {measurement.NotAnsweringCount} that do not. {standing}"))
        {
            Interpretation = new EvaluationMetricInterpretation(
                defaultIsBest ? EvaluationRating.Good : EvaluationRating.Inconclusive,
                failed: false),
        };

        return metric;
    }

    private static string Kept(int kept, int count, string answers, int threshold) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{kept} of the {count} passages that {answers} their lookup survived a threshold of {threshold}.");
}
