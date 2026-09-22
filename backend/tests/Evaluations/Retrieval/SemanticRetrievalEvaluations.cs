// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Evaluations.Costing;
using MailFathom.Evaluations.Providers;
using MailFathom.Evaluations.Reporting;
using xRetry.v3;
using Xunit;

namespace MailFathom.Evaluations.Retrieval;

/// <summary>Measures how well every declared embedding model ranks the mail that answers each question, against the labelled evidence.</summary>
/// <remarks>
/// <para>
/// Shaped like <see cref="RelevanceFilter.RelevanceFilterEvaluations" /> — one test over the whole model list, the models
/// measured at the same time, every shortfall collected before the test fails, and no judge declared or opened.
/// </para>
/// <para>
/// A model is measured once however many repetitions the run declares. A repetition exists because a chat model's
/// answer varies between calls; an embedding model answers the same text with the same vector, so a second measurement
/// would read the same ranks back and report the same figures.
/// </para>
/// </remarks>
public sealed class SemanticRetrievalEvaluations
{
    /// <summary>How many times the test is run before its failure is reported.</summary>
    private const int MaxAttempts = 3;

    /// <summary>How long to wait before running it again, sized for a rate limit or a momentary overload to clear.</summary>
    private const int DelayBetweenAttemptsMs = 5000;

    /// <summary>Gets whether an evaluation run was explicitly asked for.</summary>
    /// <remarks>Public and static because that is the shape xUnit reads a skip condition from.</remarks>
    public static bool EvaluationsRequested => AiEvaluationRun.Requested;

    [RetryFact(
        MaxAttempts,
        DelayBetweenAttemptsMs,
        Skip = AiEvaluationRun.SkipReason,
        SkipUnless = nameof(EvaluationsRequested))]
    public async Task RunAsync_LabelledQuestions_EveryDeclaredEmbeddingModelRanksTheEvidenceWithinTheFloor()
    {
        // Arrange
        var apiKey = EmbeddingModelsUnderTest.ApiKey();

        // Act
        var shortfalls = await Task.WhenAll([.. EmbeddingModelsUnderTest.Declared().Select(model => MeasureAsync(model, apiKey))]);

        // Assert
        AiEvaluationRun.AssertNoShortfalls(shortfalls.SelectMany(static modelShortfalls => modelShortfalls));
    }

    /// <summary>Measures one model over a generator, a meter, and a store handle of its own, which is what lets the models run at once.</summary>
    private static async Task<IReadOnlyList<string>> MeasureAsync(EmbeddingModelUnderTest model, string apiKey)
    {
        var modelSpend = new SpendMeter();

        using var generator = ProviderEmbeddingGenerator.Open(model, apiKey, modelSpend);

        var verdict = await SemanticRetrievalScenario.RunAsync(
            EvaluationStore.OpenUnjudged(SemanticRetrievalScenario.Evaluators),
            generator,
            model,
            modelSpend,
            TestContext.Current.CancellationToken);

        return [.. EvaluationMetrics.ShortfallsOf(verdict).Select(shortfall => $"{SemanticRetrievalScenario.Name} under {model.ReportedName}: {shortfall}")];
    }
}
