// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
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

    /// <summary>Opens the store a requested run was pointed at, for a scenario that no model judges.</summary>
    /// <param name="evaluators">What every scenario in the run is measured on, none of which asks a model.</param>
    /// <returns>The configuration each scenario opens its run from.</returns>
    /// <exception cref="InvalidOperationException">Thrown, naming the variable, when the run was not given a store or a name.</exception>
    /// <remarks>
    /// No judge is configured at all rather than one that is never asked, so an evaluator that did ask a model would fail
    /// the run instead of spending credit on a verdict the scenario said it does not need.
    /// </remarks>
    public static ReportingConfiguration OpenUnjudged(IEnumerable<IEvaluator> evaluators) =>
        OpenUnjudgedAt(
            AiEvaluationRun.Required(RootVariable),
            AiEvaluationRun.Required(ExecutionVariable),
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
        new(
            evaluators,
            new DiskBasedResultStore(root),
            new ChatConfiguration(judge),
            ResponseCacheAt(root),
            [judgeCachingKey],
            executionName);

    /// <summary>Opens a store at a given directory, under a given run name, for a scenario that no model judges.</summary>
    /// <param name="root">The directory holding the results and the cache.</param>
    /// <param name="executionName">The name this run's results are filed and compared under.</param>
    /// <param name="evaluators">What every scenario in the run is measured on, none of which asks a model.</param>
    /// <returns>The configuration each scenario opens its run from.</returns>
    /// <remarks>
    /// No judge means no judge's verdicts to cache, but the model under test's answers are what this store caches all the
    /// same.
    /// </remarks>
    public static ReportingConfiguration OpenUnjudgedAt(
        string root,
        string executionName,
        IEnumerable<IEvaluator> evaluators) =>
        new(
            evaluators,
            new DiskBasedResultStore(root),
            chatConfiguration: null,
            ResponseCacheAt(root),
            executionName: executionName);

    /// <summary>Removes every answer and every verdict a scenario cached under one iteration, so the next attempt asks again.</summary>
    /// <param name="reporting">The store the scenario ran in, opened here.</param>
    /// <param name="scenarioName">The scenario the answers are filed under.</param>
    /// <param name="iterationName">The iteration the answers are filed under, which names the model and the repetition.</param>
    /// <param name="cancellationToken">Withdraws the removal.</param>
    /// <returns>A task that completes once the entries are gone.</returns>
    /// <remarks>
    /// Called where a model fell short of the share <see cref="EvaluationRepetitions" /> holds it to. Left cached, the
    /// answers would be what every retry and every later run reads back, so one sample would decide them all however the
    /// model answers when asked again. The verdict already filed stays in the result store, so the report still shows the
    /// attempt that fell short.
    /// </remarks>
    public static Task ForgetAsync(
        ReportingConfiguration reporting,
        string scenarioName,
        string iterationName,
        CancellationToken cancellationToken) =>
        ((RecallingResponseCacheProvider)reporting.ResponseCacheProvider!).ForgetAsync(
            scenarioName,
            iterationName,
            cancellationToken);

    /// <summary>Turns a model's name and a repetition into a name the store can file a result under.</summary>
    /// <param name="modelName">The routed name of the model under test.</param>
    /// <param name="repetition">Which repetition of the case, counted from one.</param>
    /// <returns>The name, readable as the model's.</returns>
    /// <remarks>
    /// The store files an iteration as a directory, and a routed name such as one naming its vendor carries a separator;
    /// the name stays readable as the model's with that character replaced. The first repetition is filed under the
    /// model's name alone, so a run declaring none reads back every answer a run before repetitions existed paid for.
    /// </remarks>
    public static string IterationNameFor(string modelName, int repetition)
    {
        var model = string.Concat(modelName.Select(static character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));

        return repetition is 1 ? model : string.Create(CultureInfo.InvariantCulture, $"{model}#{repetition}");
    }

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

        // The effort travels in a request-options factory the cache key cannot read, so it joins the key explicitly — and
        // only where one is declared, so every answer cached by a run declaring none keeps the key it was filed under.
        string[] identity = [plan.Endpoint.RoutedModelName, plan.Endpoint.Address?.AbsoluteUri ?? string.Empty];

        return new DistributedCachingChatClient(model, cache)
        {
            CacheKeyAdditionalValues = plan.ReasoningEffort is { } effort ? [.. identity, effort] : identity,
        };
    }

    private static RecallingResponseCacheProvider ResponseCacheAt(string root) =>
        new(new DiskBasedResponseCacheProvider(root, AnswerLifetime));
}
