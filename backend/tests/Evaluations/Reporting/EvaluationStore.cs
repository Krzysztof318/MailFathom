// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Reporting;
using Microsoft.Extensions.AI.Evaluation.Reporting.Storage;

namespace MailFathom.Evaluations.Reporting;

/// <summary>Where a run's verdicts and every answer it paid for are kept, so the next run compares against them and re-asks none.</summary>
/// <remarks>
/// <para>
/// One directory holds both halves. The result store is what <c>dotnet aieval report</c> renders, one column per run,
/// which is only a comparison while earlier runs' results are still there; the response cache is what makes a re-run
/// that changed neither a prompt nor a model cost nothing. <c>scripts/run-ai-evaluations.sh</c> names the directory and
/// the run, and the <c>AI evaluations</c> workflow carries the directory from one run to the next.
/// </para>
/// <para>
/// Everything written here is published with the workflow's artefacts, which is why the judge reaches it only through
/// <see cref="Judging.AnonymousJudgeChatClient" /> and why every input is synthetic mail.
/// </para>
/// </remarks>
internal static class EvaluationStore
{
    private const string RootVariable = "MAILFATHOM_AI_EVALUATIONS_STORE";
    private const string ExecutionVariable = "MAILFATHOM_AI_EVALUATIONS_EXECUTION";

    /// <summary>How long an answer stays reusable.</summary>
    /// <remarks>
    /// Long rather than the library's fourteen days: an entry is keyed by the prompt, the model, and the parameters, so a
    /// hit is always the answer to exactly the request about to be sent, and letting one expire only pays for it again.
    /// What the expiry still does is let an entry no scenario asks for any more fall away when the script prunes the store.
    /// </remarks>
    private static readonly TimeSpan AnswerLifetime = TimeSpan.FromDays(365);

    /// <summary>Opens the store a requested run was pointed at.</summary>
    /// <param name="judge">The judge, already anonymous.</param>
    /// <param name="judgeCachingKey">The key the judge's verdicts are filed under.</param>
    /// <param name="evaluators">What every scenario in the run is judged on.</param>
    /// <returns>The configuration each scenario opens its run from.</returns>
    /// <exception cref="InvalidOperationException">Thrown, naming the variable, when the run was not given a store or a name.</exception>
    public static ReportingConfiguration Open(
        IChatClient judge,
        string judgeCachingKey,
        IEnumerable<IEvaluator> evaluators) =>
        OpenAt(
            AiEvaluationRun.Required(RootVariable),
            AiEvaluationRun.Required(ExecutionVariable),
            judge,
            judgeCachingKey,
            evaluators);

    /// <summary>Opens a store at a given directory, under a given run name.</summary>
    /// <param name="root">The directory holding the results and the cache.</param>
    /// <param name="executionName">The name this run's results are filed and compared under.</param>
    /// <param name="judge">The judge, already anonymous.</param>
    /// <param name="judgeCachingKey">The key the judge's verdicts are filed under.</param>
    /// <param name="evaluators">What every scenario in the run is judged on.</param>
    /// <returns>The configuration each scenario opens its run from.</returns>
    public static ReportingConfiguration OpenAt(
        string root,
        string executionName,
        IChatClient judge,
        string judgeCachingKey,
        IEnumerable<IEvaluator> evaluators) =>
        DiskBasedReportingConfiguration.Create(
            root,
            evaluators,
            new ChatConfiguration(judge),
            enableResponseCaching: true,
            AnswerLifetime,
            [judgeCachingKey],
            executionName);
}
