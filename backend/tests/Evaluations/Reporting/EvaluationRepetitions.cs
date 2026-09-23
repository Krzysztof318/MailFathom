// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Evaluations.Costing;
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
/// A case whose share falls short forgets every repetition rather than only the ones that fell short, so the next
/// run draws a whole new sample. Forgetting only the misses would keep the lucky answers and re-ask the rest until
/// they passed, which reports a rate no deployment would see.
/// </para>
/// </remarks>
internal static class EvaluationRepetitions
{
    /// <summary>The variable carrying how many times each case is asked of each model, read beside the models.</summary>
    public const string RepetitionsVariable = "MAILFATHOM_EVALUATION_REPETITIONS";

    /// <summary>The name the share of repetitions that passed is recorded under.</summary>
    public const string PassingShareMetricName = "Passing share";

    /// <summary>The share of repetitions a model has to pass.</summary>
    /// <remarks>Every one, which is the threshold each scenario held a single answer to, expressed as a share.</remarks>
    public const double RequiredShare = 1;

    /// <summary>The most repetitions a run may declare, since each one is paid for under every model and every case.</summary>
    private const int MostRepetitions = 20;

    /// <summary>Reads how many times the run asks each case of each model.</summary>
    /// <returns>The declared count, or one where the run declared none.</returns>
    /// <exception cref="InvalidOperationException">Thrown, naming the variable, when the declaration is not a count the run accepts.</exception>
    public static int Declared() => Parse(AiEvaluationRun.Optional(RepetitionsVariable));

    /// <summary>Reads a declared count of repetitions.</summary>
    /// <param name="declared">The declaration, or <see langword="null" /> where the run made none.</param>
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
                $"{RepetitionsVariable} must be a whole number from 1 to {MostRepetitions}.");
    }

    /// <summary>Asks one case of one model once per repetition, records the share that passed, and names the shortfall.</summary>
    /// <param name="reporting">The store the case is filed in.</param>
    /// <param name="scenarioName">The case, as the store files it.</param>
    /// <param name="modelName">The routed name of the model under test.</param>
    /// <param name="repetitions">How many times the case is asked.</param>
    /// <param name="measure">Asks the case once, as the numbered repetition, and names what that answer fell short on.</param>
    /// <param name="cancellationToken">Withdraws the run.</param>
    /// <returns>One line naming the case, the model, and how many repetitions fell short, each followed by why; none where the share holds.</returns>
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

        await RecordAsync(reporting, scenarioName, modelName, repetitions, passed, holds, cancellationToken);

        if (holds)
        {
            return [];
        }

        for (var repetition = 1; repetition <= repetitions; repetition++)
        {
            await EvaluationStore.ForgetAsync(
                reporting,
                scenarioName,
                EvaluationStore.IterationNameFor(modelName, repetition),
                cancellationToken);
        }

        return
        [
            $"{scenarioName} under {modelName}: {repetitions - passed} of {repetitions} repetition(s) fell short.",
            .. shortfalls.SelectMany(static (repetitionShortfalls, index) =>
                repetitionShortfalls.Select(shortfall => $"  repetition {index + 1}: {shortfall}")),
        ];
    }

    /// <summary>Adds the share that passed and what the repetitions cost together to every repetition's filed result.</summary>
    /// <remarks>
    /// Written into each repetition rather than a result of its own, so every row the report shows for the model carries
    /// the rate beside its own verdict, and no iteration exists in the store that no answer was given under.
    /// </remarks>
    private static async Task RecordAsync(
        ReportingConfiguration reporting,
        string scenarioName,
        string modelName,
        int repetitions,
        int passed,
        bool holds,
        CancellationToken cancellationToken)
    {
        List<ScenarioRunResult> filed = [];

        for (var repetition = 1; repetition <= repetitions; repetition++)
        {
            await foreach (var result in reporting.ResultStore.ReadResultsAsync(
                reporting.ExecutionName,
                scenarioName,
                EvaluationStore.IterationNameFor(modelName, repetition),
                cancellationToken))
            {
                filed.Add(result);
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
        var cost = EvaluationCost.SumOver([.. filed.Select(static result => result.EvaluationResult)]);

        foreach (var result in filed)
        {
            result.EvaluationResult.Metrics[share.Name] = share;
            result.EvaluationResult.Metrics[cost.Name] = cost;
        }

        await reporting.ResultStore.WriteResultsAsync(filed, cancellationToken);
    }
}
