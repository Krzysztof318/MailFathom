// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.Extensions.AI.Evaluation;

namespace MailFathom.Evaluations.Reporting;

/// <summary>How a scenario records a structural check beside the judge's ratings, holds a rating to its threshold, and names what failed.</summary>
/// <remarks>
/// Every check and every rating lands in the verdict as a metric carrying its own interpretation, so the report shows
/// each model's result on each of them and a failed run names the metric rather than the answer.
/// </remarks>
internal static class EvaluationMetrics
{
    /// <summary>Records a structural check in the verdict, failed where it did not hold.</summary>
    /// <param name="verdict">The verdict the check is recorded in.</param>
    /// <param name="name">The name the check is reported under.</param>
    /// <param name="held">Whether it held.</param>
    /// <param name="reason">What was found, in words a failed run can be read by.</param>
    public static void Record(EvaluationResult verdict, string name, bool held, string reason) =>
        verdict.Metrics[name] = new BooleanMetric(name, held, reason)
        {
            Interpretation = new EvaluationMetricInterpretation(
                held ? EvaluationRating.Good : EvaluationRating.Unacceptable,
                failed: !held,
                reason),
        };

    /// <summary>Holds one of the judge's ratings to the lowest score a scenario records for it.</summary>
    /// <param name="verdict">The verdict the rating is in.</param>
    /// <param name="metricName">The name the judge's evaluator reports the rating under.</param>
    /// <param name="minimum">The lowest rating, from one to five, a model may score.</param>
    /// <remarks>A rating the judge did not give fails, because a verdict nobody reached proves nothing about the answer.</remarks>
    public static void HoldToThreshold(EvaluationResult verdict, string metricName, int minimum)
    {
        var rating = verdict.Get<NumericMetric>(metricName);

        rating.Interpretation = rating.Value is { } value
            ? new EvaluationMetricInterpretation(
                value >= minimum ? EvaluationRating.Good : EvaluationRating.Unacceptable,
                failed: value < minimum,
                $"Rated {value} against the scenario's threshold of {minimum}.")
            : new EvaluationMetricInterpretation(EvaluationRating.Inconclusive, failed: true, "The judge did not rate it.");
    }

    /// <summary>Names every check and rating the verdict falls short on.</summary>
    /// <param name="scenarioName">The scenario the verdict is for.</param>
    /// <param name="verdict">The verdict one model's run of the scenario produced.</param>
    /// <returns>One line per shortfall, naming the scenario and the metric.</returns>
    public static IEnumerable<string> ShortfallsOf(string scenarioName, EvaluationResult verdict) =>
        verdict.Metrics.Values
            .Where(static metric => metric.Interpretation is { Failed: true })
            .Select(metric => $"{scenarioName}: {metric.Name} — {metric.Interpretation!.Reason ?? metric.Reason}");
}
