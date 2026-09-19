// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using Microsoft.Extensions.AI.Evaluation;

namespace MailFathom.Evaluations.Costing;

/// <summary>What one scenario run cost, in the report beside what it scored.</summary>
/// <remarks>
/// <para>
/// A metric rather than a line in a log, because the question it answers has the same shape as a score's: what this run
/// cost against what the run before it cost, per model, over the same scenario. The report renders it beside the
/// verdicts and compares it across runs on its own.
/// </para>
/// <para>
/// The figure is the provider's own charge, read off each answer, so a run that spent nothing because every answer came
/// from the store's cache reports nothing rather than repeating what the same scenario cost yesterday. A provider that
/// reports no charge leaves the metric unrated and its tokens stated, which is the posture the evaluators take towards
/// an answer they cannot grade.
/// </para>
/// </remarks>
internal static class EvaluationCost
{
    /// <summary>The name the cost is recorded and compared under.</summary>
    public const string MetricName = "Cost (USD)";

    /// <summary>Records what a scenario run paid, for the model under test and for the judge that graded it.</summary>
    /// <param name="verdict">The result the metric is added to, before the run is written to the store.</param>
    /// <param name="model">The model under test.</param>
    /// <param name="modelSpend">What reaching that model cost, answers the cache served excluded.</param>
    /// <param name="judgeSpend">What reaching the judge cost, verdicts the cache served excluded, and nothing for a scenario no judge grades.</param>
    public static void Record(EvaluationResult verdict, string model, PaidUsage modelSpend, PaidUsage judgeSpend)
    {
        var metric = new NumericMetric(
            MetricName,
            !(modelSpend.IsFree && judgeSpend.IsFree) && ChargeOf(modelSpend) + ChargeOf(judgeSpend) is { } charged
                ? (double)charged
                : null,
            Describe(model, modelSpend, judgeSpend));

        metric.AddOrUpdateMetadata("paid-calls", Invariant(modelSpend.Calls + judgeSpend.Calls));
        metric.AddOrUpdateMetadata("paid-input-tokens", Invariant(modelSpend.InputTokens + judgeSpend.InputTokens));
        metric.AddOrUpdateMetadata("paid-output-tokens", Invariant(modelSpend.OutputTokens + judgeSpend.OutputTokens));

        verdict.Metrics[metric.Name] = metric;
    }

    private static string Describe(string model, PaidUsage modelSpend, PaidUsage judgeSpend)
    {
        if (modelSpend.IsFree && judgeSpend.IsFree)
        {
            return "Nothing reached a provider: every answer and every verdict came from the store's cache.";
        }

        var judgeShare = judgeSpend.IsFree
            ? string.Empty
            : string.Create(
                CultureInfo.InvariantCulture,
                $", and the judge {judgeSpend.InputTokens} and {judgeSpend.OutputTokens} over {judgeSpend.Calls}");
        var spend = string.Create(
            CultureInfo.InvariantCulture,
            $"{model} was sent {modelSpend.InputTokens} input and {modelSpend.OutputTokens} output tokens over {modelSpend.Calls} call(s){judgeShare}.");

        return (ChargeOf(modelSpend), ChargeOf(judgeSpend)) switch
        {
            (null, null) => $"{spend} Neither answer carried a charge, so what that cost is the provider's to state.",
            (null, _) => $"{spend} The model's answers carried no charge, so the total is left unstated rather than reported as the judge's share alone.",
            (_, null) => $"{spend} The judge's answers carried no charge, so the total is left unstated rather than reported as the model's share alone.",
            _ => spend,
        };
    }

    /// <summary>Reads what a client was charged, which is nothing at all where no call of its reached a provider.</summary>
    /// <remarks>
    /// A client that made no call is not a client whose provider stated no charge: a judge whose verdicts all came from
    /// the cache, or a scenario no judge grades, adds nothing to the total rather than leaving it unstated.
    /// </remarks>
    private static decimal? ChargeOf(PaidUsage spend) => spend.IsFree ? 0m : spend.Cost;

    private static string Invariant(long value) => value.ToString(CultureInfo.InvariantCulture);
}
