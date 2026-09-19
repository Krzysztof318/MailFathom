// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.AI.Chat;
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

    /// <summary>Turns a model's name into a name the store can file a result under.</summary>
    /// <param name="modelName">The routed name of the model under test.</param>
    /// <returns>The name, readable as the model's.</returns>
    /// <remarks>
    /// The store files an iteration as a directory, and a routed name such as one naming its vendor carries a separator;
    /// the name stays readable as the model's with that character replaced.
    /// </remarks>
    public static string IterationNameFor(string modelName) =>
        string.Concat(modelName.Select(static character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));

    /// <summary>Puts the run's response cache in front of the model under test, filed under the model and its address.</summary>
    /// <param name="reporting">The run's store.</param>
    /// <param name="model">The model under test's client, which stays the caller's.</param>
    /// <param name="plan">The plan the model is measured with.</param>
    /// <param name="scenarioName">The scenario the answers are filed under.</param>
    /// <param name="iterationName">The iteration the answers are filed under.</param>
    /// <param name="cancellationToken">Withdraws the run.</param>
    /// <returns>The cached client.</returns>
    /// <remarks>
    /// The wrapper is not disposed, and owns nothing that would need it: disposing a
    /// <see cref="DelegatingChatClient" /> disposes the client it wraps, and the model under test belongs to the caller
    /// that opened it. The cache itself is the run's, and the store closes it.
    /// </remarks>
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Disposing the caching wrapper would dispose the caller's model client, which the scenario does not own.")]
    public static async Task<IChatClient> CacheOverAsync(
        ReportingConfiguration reporting,
        IChatClient model,
        ChatGenerationPlan plan,
        string scenarioName,
        string iterationName,
        CancellationToken cancellationToken)
    {
        var cache = await reporting.ResponseCacheProvider!.GetCacheAsync(scenarioName, iterationName, cancellationToken);

        return new DistributedCachingChatClient(model, cache)
        {
            CacheKeyAdditionalValues = [plan.Endpoint.RoutedModelName, plan.Endpoint.Address?.AbsoluteUri ?? string.Empty],
        };
    }
}
