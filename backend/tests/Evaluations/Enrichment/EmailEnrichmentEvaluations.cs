// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.AI.Chat;
using MailFathom.Application.Emails.Enrichment;
using MailFathom.Evaluations.Costing;
using MailFathom.Evaluations.Judging;
using MailFathom.Evaluations.Providers;
using MailFathom.Evaluations.Reporting;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;
using xRetry.v3;
using Xunit;

namespace MailFathom.Evaluations.Enrichment;

/// <summary>Measures the enrichment agent under every declared model, and is the worked example a new scenario starts from.</summary>
/// <remarks>
/// <para>
/// One test over the whole model list rather than a theory per model, because the list is read from the run's
/// declaration and a theory's data is read before the skip condition is: a run nobody asked for would fail on a list
/// nobody declared. Each model is still its own result in the store, filed under its name.
/// </para>
/// <para>
/// The models are measured at the same time rather than one after another, because a run costs whatever the slowest
/// model takes to answer and a list of six measured in sequence is six answers' worth of waiting. Each carries its own
/// clients, its own spend meters, and its own handle on the store, so nothing is shared between two models but the
/// directory their results are filed in — and the store files a result per scenario and iteration.
/// </para>
/// <para>
/// A model that falls short is collected rather than failing the run, so one weak model never hides what the others
/// did; the test fails at the end, naming every model that fell short and why.
/// </para>
/// <para>
/// Retried like every test that reaches a real provider, and cheaper to retry than one: whatever an attempt was already
/// answered is read back from the cache, so a second attempt pays only for what the first did not reach.
/// </para>
/// </remarks>
public sealed class EmailEnrichmentEvaluations
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
    public async Task Derive_AnInvoiceFollowUp_EveryDeclaredModelWritesReadingsTheMessageSupports()
    {
        // Arrange
        var judge = JudgeDeclaration.Read();
        var apiKey = ModelsUnderTest.ApiKey();

        // Act
        var outcomes = await Task.WhenAll([.. ModelsUnderTest.Plans().Select(plan => MeasureAsync(judge, plan, apiKey))]);

        // Assert
        Assert.Empty(outcomes.SelectMany(ShortfallsOf));
    }

    /// <summary>Measures one model over clients, meters, and a store handle of its own, which is what lets the models run at once.</summary>
    private static async Task<EmailEnrichmentOutcome> MeasureAsync(
        JudgeDeclaration judge,
        ChatGenerationPlan plan,
        string apiKey)
    {
        var modelSpend = new SpendMeter();
        var judgeSpend = new SpendMeter();

        using var model = ProviderChatClient.Open(plan.Endpoint, apiKey, plan.RequestTimeout, modelSpend);
        using var judgeClient = judge.Open(judgeSpend);

        var reporting = EvaluationStore.Open(judgeClient, judge.CachingKey, EmailEnrichmentScenario.Evaluators);

        return await EmailEnrichmentScenario.RunAsync(
            reporting,
            model,
            plan,
            modelSpend,
            judgeSpend,
            TestContext.Current.CancellationToken);
    }

    /// <summary>Names what one model's outcome falls short on, in words a failed run can be read by.</summary>
    private static IEnumerable<string> ShortfallsOf(EmailEnrichmentOutcome outcome)
    {
        if (!outcome.Marks.Any(static mark => mark.Aspect is EmailEnrichmentAspect.Sense))
        {
            yield return $"{outcome.Model}: no reading of what the message is about survived.";
        }

        var groundedness = outcome.Verdict.Get<NumericMetric>(GroundednessEvaluator.GroundednessMetricName);

        if (groundedness.Interpretation is not { Failed: false })
        {
            yield return $"{outcome.Model}: groundedness {groundedness.Value?.ToString("0.#", CultureInfo.InvariantCulture) ?? "was not rated"} — {groundedness.Interpretation?.Reason ?? groundedness.Reason}";
        }
    }
}
