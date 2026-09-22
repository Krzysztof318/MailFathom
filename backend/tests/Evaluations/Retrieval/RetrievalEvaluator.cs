// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace MailFathom.Evaluations.Retrieval;

/// <summary>Turns where the evidence landed into recall at three depths and a mean reciprocal rank, and holds the deepest recall to a floor.</summary>
/// <remarks>
/// <para>
/// Each figure is the mean over cases rather than over pieces of evidence, so a question resting on two facts weighs as
/// much as one resting on one, and a case's recall is the share of its evidence reached. Twenty is the depth held to a
/// floor because it is how many results a search answers with when the caller names no limit; five and ten are what a
/// person reads first, and are reported without failing anything.
/// </para>
/// <para>
/// No model is asked: every metric is computed from the ranks, so this evaluator needs no chat configuration.
/// </para>
/// </remarks>
internal sealed class RetrievalEvaluator : IEvaluator
{
    /// <summary>The name the mean reciprocal rank is reported under.</summary>
    public const string MeanReciprocalRankMetricName = "Mean reciprocal rank";

    /// <summary>The depth whose recall is held to the floor.</summary>
    public const int FlooredDepth = 20;

    /// <summary>The lowest recall at <see cref="FlooredDepth" /> a model may reach.</summary>
    public const double MinimumRecall = 0.8;

    /// <summary>Gets the depths recall is reported at.</summary>
    public static IReadOnlyList<int> Depths { get; } = [5, 10, FlooredDepth];

    /// <inheritdoc />
    public IReadOnlyCollection<string> EvaluationMetricNames { get; } =
        [.. Depths.Select(RecallMetricName), MeanReciprocalRankMetricName];

    /// <summary>Names the recall reported at a depth.</summary>
    /// <param name="depth">The depth.</param>
    /// <returns>The metric's name.</returns>
    public static string RecallMetricName(int depth) =>
        string.Create(CultureInfo.InvariantCulture, $"Recall at {depth}");

    /// <inheritdoc />
    public ValueTask<EvaluationResult> EvaluateAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        ChatConfiguration? chatConfiguration = null,
        IEnumerable<EvaluationContext>? additionalContext = null,
        CancellationToken cancellationToken = default)
    {
        var measurement = additionalContext?.OfType<RetrievalMeasurement>().SingleOrDefault()
            ?? throw new InvalidOperationException(
                $"{nameof(RetrievalEvaluator)} reads where the evidence landed from a {nameof(RetrievalMeasurement)}, and none was given.");

        EvaluationMetric[] metrics =
        [
            .. Depths.Select(depth => Recall(measurement, depth)),
            new NumericMetric(
                MeanReciprocalRankMetricName,
                measurement.Cases.Average(static ranks => ranks.ReciprocalRank),
                "The mean over cases of one over the rank the first piece of evidence was reached at."),
        ];

        return ValueTask.FromResult(new EvaluationResult(metrics));
    }

    private static NumericMetric Recall(RetrievalMeasurement measurement, int depth)
    {
        var recall = measurement.Cases.Average(ranks => ranks.RecallAt(depth));
        var metric = new NumericMetric(
            RecallMetricName(depth),
            recall,
            string.Create(CultureInfo.InvariantCulture, $"The mean over cases of the share of their evidence ranked within the first {depth} messages."));

        if (depth is FlooredDepth)
        {
            metric.Interpretation = new EvaluationMetricInterpretation(
                recall >= MinimumRecall ? EvaluationRating.Good : EvaluationRating.Unacceptable,
                failed: recall < MinimumRecall,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{recall:P0} against a floor of {MinimumRecall:P0}; evidence missed: {Missed(measurement, depth)}."));
        }

        return metric;
    }

    private static string Missed(RetrievalMeasurement measurement, int depth)
    {
        var missed = measurement.Cases.Where(ranks => ranks.RecallAt(depth) < 1).Select(static ranks => ranks.CaseName).ToArray();

        return missed.Length > 0 ? string.Join(", ", missed) : "none";
    }
}
