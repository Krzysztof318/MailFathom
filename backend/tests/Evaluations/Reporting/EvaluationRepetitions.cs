// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Evaluations.Costing;
using MailFathom.Evaluations.Providers;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Reporting;

namespace MailFathom.Evaluations.Reporting;

/// <summary>Asks one case of one model as many times as the run declared, and holds the model to the share that passed.</summary>
/// <remarks>
/// <para>
/// A model's answer varies between calls, so one call says whether a model passed this time rather than how often it
/// passes, and what a deployment experiences is the rate. Each repetition is its own iteration in the store and so its
/// own cached answer: a repeated run over an unchanged prompt reads every repetition back, and the rate it reports is
/// the rate the answers it paid for gave.
/// </para>
/// <para>
/// A case keeps its answers cached whatever its verdict, so a later run replays the sample an earlier one drew, passed
/// or failed. Forgetting a case that fell short would re-ask it until one sample passed and then keep that one, so a run
/// read from the cache would report nearly every case passing whatever the model's own rate is — measured at 0.4% of
/// cached answers failing against 12% of fresh ones on the same code. A new sample is drawn where the key changes, or
/// where a run discards the cache.
/// </para>
/// <para>
/// Every repetition therefore also records how many of its answers were read from the cache and how many were asked
/// afresh, because a rate replayed from the cache and a rate just measured read the same in a report and are not
/// comparable.
/// </para>
/// </remarks>
internal static class EvaluationRepetitions
{
    /// <summary>The name the share of repetitions that passed is recorded under.</summary>
    public const string PassingShareMetricName = "Passing share";

    /// <summary>The name the count of a repetition's answers read from the cache is recorded under.</summary>
    /// <remarks><c>scripts/run-ai-evaluations.sh</c> sums it over a run by this name.</remarks>
    public const string CachedAnswersMetricName = "Answers read from the cache";

    /// <summary>The name the count of a repetition's answers asked of the model is recorded under.</summary>
    /// <remarks><c>scripts/run-ai-evaluations.sh</c> sums it over a run by this name.</remarks>
    public const string AskedAnswersMetricName = "Answers asked afresh";

    /// <summary>The share of repetitions a model has to pass.</summary>
    /// <remarks>Every one, which is the threshold each scenario held a single answer to, expressed as a share.</remarks>
    public const double RequiredShare = 1;

    /// <summary>The most repetitions a run may declare, since each one is paid for under every model and every case.</summary>
    private const int MostRepetitions = 20;

    /// <summary>Reads how many times the run asks each case of each model.</summary>
    /// <returns>The declared count, or one where the run declared none.</returns>
    /// <exception cref="InvalidOperationException">Thrown, naming the variable, when the declaration is not a count the run accepts.</exception>
    public static int Declared() => EvaluationDeclaration.Read().Repetitions;

    /// <summary>Reads a declared count of repetitions.</summary>
    /// <param name="declared">The <c>Repetitions</c> the run's block declares, or <see langword="null" /> where it declares none.</param>
    /// <returns>The count, which is one where nothing was declared.</returns>
    /// <exception cref="InvalidOperationException">Thrown, naming the variable, when the declaration is not a count the run accepts.</exception>
    public static int Parse(string? declared)
    {
        if (declared is null)
        {
            return 1;
        }

        return int.TryParse(declared.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var count)
            && count is >= 1 and <= MostRepetitions
            ? count
            : throw new InvalidOperationException(
                $"{EvaluationDeclaration.Variable} declares Repetitions that is not a whole number from 1 to {MostRepetitions}.");
    }

    /// <summary>Asks one case of one model once per repetition, records the share that passed, and names the shortfall.</summary>
    /// <param name="reporting">The store the case is filed in.</param>
    /// <param name="scenarioName">The case, as the store files it.</param>
    /// <param name="modelName">The routed name of the model under test.</param>
    /// <param name="repetitions">How many times the case is asked.</param>
    /// <param name="measure">Asks the case once, as the numbered repetition, and names what that answer fell short on.</param>
    /// <param name="cancellationToken">Withdraws the run.</param>
    /// <returns>One line naming the case, the model, how many repetitions fell short, and how many of its answers were read from the cache, followed by why each fell short; none where the share holds.</returns>
    /// <remarks>
    /// The repetitions run one after another, because a model's spend meter is read once per answer and two answers
    /// running at once would each be charged with the other's calls.
    /// </remarks>
    public static async Task<IReadOnlyList<string>> MeasureAsync(
        ReportingConfiguration reporting,
        string scenarioName,
        string modelName,
        int repetitions,
        Func<int, Task<IReadOnlyList<string>>> measure,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reporting);
        ArgumentNullException.ThrowIfNull(measure);

        var shortfalls = new List<IReadOnlyList<string>>(repetitions);

        for (var repetition = 1; repetition <= repetitions; repetition++)
        {
            shortfalls.Add(await measure(repetition));
        }

        var passed = shortfalls.Count(static repetitionShortfalls => repetitionShortfalls.Count is 0);
        var holds = (double)passed / repetitions >= RequiredShare;
        CachedAnswerTally[] tallies =
        [
            .. Enumerable.Range(1, repetitions).Select(repetition =>
                EvaluationStore.TallyOf(reporting, scenarioName, EvaluationStore.IterationNameFor(modelName, repetition))),
        ];

        await RecordAsync(reporting, scenarioName, modelName, passed, holds, tallies, cancellationToken);

        if (holds)
        {
            return [];
        }

        var readFromCache = tallies.Sum(static tally => tally.Read);
        var answers = readFromCache + tallies.Sum(static tally => tally.Asked);

        return
        [
            $"{scenarioName} under {modelName}: {repetitions - passed} of {repetitions} repetition(s) fell short; {readFromCache} of its {answers} answer(s) were read from the cache.",
            .. shortfalls.SelectMany(static (repetitionShortfalls, index) =>
                repetitionShortfalls.Select(shortfall => $"  repetition {index + 1}: {shortfall}")),
        ];
    }

    /// <summary>Adds the share that passed and what the repetitions cost together to every repetition's filed result, and to each where its own answers came from.</summary>
    /// <remarks>
    /// Written into each repetition rather than a result of its own, so every row the report shows for the model carries
    /// the rate beside its own verdict, and no iteration exists in the store that no answer was given under.
    /// </remarks>
    private static async Task RecordAsync(
        ReportingConfiguration reporting,
        string scenarioName,
        string modelName,
        int passed,
        bool holds,
        CachedAnswerTally[] tallies,
        CancellationToken cancellationToken)
    {
        var repetitions = tallies.Length;
        List<(ScenarioRunResult Result, CachedAnswerTally Tally)> filed = [];

        for (var repetition = 1; repetition <= repetitions; repetition++)
        {
            await foreach (var result in reporting.ResultStore.ReadResultsAsync(
                reporting.ExecutionName,
                scenarioName,
                EvaluationStore.IterationNameFor(modelName, repetition),
                cancellationToken))
            {
                filed.Add((result, tallies[repetition - 1]));
            }
        }

        var reason = string.Create(
            CultureInfo.InvariantCulture,
            $"{passed} of {repetitions} repetition(s) passed, against a required share of {RequiredShare:0.##}.");
        var share = new NumericMetric(PassingShareMetricName, (double)passed / repetitions, reason)
        {
            Interpretation = new EvaluationMetricInterpretation(
                holds ? EvaluationRating.Good : EvaluationRating.Unacceptable,
                failed: !holds,
                reason),
        };
        var cost = EvaluationCost.SumOver([.. filed.Select(static filing => filing.Result.EvaluationResult)]);

        foreach (var (result, tally) in filed)
        {
            var origin = string.Create(
                CultureInfo.InvariantCulture,
                $"{tally.Read} of the {tally.Read + tally.Asked} answer(s) the model gave in this repetition were read from the cache, and {tally.Asked} asked afresh.");

            result.EvaluationResult.Metrics[share.Name] = share;
            result.EvaluationResult.Metrics[cost.Name] = cost;
            result.EvaluationResult.Metrics[CachedAnswersMetricName] = new NumericMetric(CachedAnswersMetricName, tally.Read, origin);
            result.EvaluationResult.Metrics[AskedAnswersMetricName] = new NumericMetric(AskedAnswersMetricName, tally.Asked, origin);
        }

        await reporting.ResultStore.WriteResultsAsync([.. filed.Select(static filing => filing.Result)], cancellationToken);
    }
}
