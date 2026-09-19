// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using MailFathom.AI.Retrieval;
using MailFathom.Evaluations.Costing;
using MailFathom.Evaluations.Enrichment;
using MailFathom.Evaluations.Reporting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Xunit;

namespace MailFathom.Evaluations.RelevanceFilter;

/// <summary>
/// Proves, without calling any provider, that the labels name real passages, that the filter's counts are read correctly
/// under every threshold, that a run asks the model once per candidate and no judge at all, and where the floor fails.
/// </summary>
/// <remarks>
/// Free, and therefore not gated on the run switch. Like the enrichment scenario's tests, these write a real store to a
/// temporary directory, because the scenario files its measurement there and caches its answers there.
/// </remarks>
public sealed class RelevanceFilterScenarioTests : IDisposable
{
    private const string ModelUnderTest = "model-under-test";

    private readonly DirectoryInfo store = Directory.CreateTempSubdirectory("mailfathom-evaluations-");

    [Fact]
    public void Lookups_EveryLabelledCandidate_ResolvesToAPassageOfItsMessage()
    {
        // Act
        var resolved = LabelledCandidates.Lookups
            .SelectMany(static lookup => lookup.Candidates)
            .Select(static candidate => (candidate, passage: candidate.Resolve()))
            .ToArray();

        // Assert
        Assert.All(resolved, static pair => Assert.Contains(pair.candidate.Evidence, pair.passage.Text, StringComparison.Ordinal));
        Assert.All(LabelledCandidates.Lookups, static lookup =>
        {
            Assert.Contains(lookup.Candidates, static candidate => candidate.Answers);
            Assert.Contains(lookup.Candidates, static candidate => !candidate.Answers);
        });
    }

    [Fact]
    public void Thresholds_Measured_IncludeTheDeploymentDefault()
    {
        // Assert
        Assert.Contains(PassageRelevanceFilterPlan.DefaultMinimumRelevance, RelevanceFilterEvaluator.Thresholds);
    }

    [Fact]
    public async Task RunAsync_AModelScoringEveryAnswerAboveEverythingElse_CountsWhatEachThresholdKept()
    {
        // Arrange
        using var model = new LabelReadingChatClient(answeringScore: "80", otherScore: "30");

        // Act
        var verdict = await this.RunScenarioAsync(model);

        // Assert
        int?[] answeringKept = [.. RelevanceFilterEvaluator.Thresholds.Select(threshold => KeptAt(verdict, RelevanceFilterEvaluator.AnsweringKeptName(threshold)))];
        int?[] notAnsweringKept = [.. RelevanceFilterEvaluator.Thresholds.Select(threshold => KeptAt(verdict, RelevanceFilterEvaluator.NotAnsweringKeptName(threshold)))];

        Assert.Equal([12, 12, 12, 12, 12, 12, 12, 12, 0], answeringKept);
        Assert.Equal([25, 25, 25, 0, 0, 0, 0, 0, 0], notAnsweringKept);
        Assert.Equal(40, verdict.Get<NumericMetric>(RelevanceFilterEvaluator.BestThresholdMetricName).Value);
        Assert.DoesNotContain(verdict.Metrics.Values, static metric => metric.Interpretation is { Failed: true });
    }

    [Fact]
    public async Task RunAsync_OverEveryThreshold_AsksTheModelOncePerCandidateAndReportsNothingSpent()
    {
        // Arrange
        using var model = new LabelReadingChatClient(answeringScore: "80", otherScore: "30");

        // Act
        var verdict = await this.RunScenarioAsync(model);

        // Assert
        Assert.Equal(LabelledCandidates.Lookups.Sum(static lookup => lookup.Candidates.Count), model.Requests);
        Assert.Equal("0", verdict.Get<NumericMetric>(EvaluationCost.MetricName).Metadata?["paid-calls"]);
    }

    [Fact]
    public async Task RunAsync_AModelScoringEverythingAsAnswering_FailsTheCeilingAtTheDefault()
    {
        // Arrange
        using var model = new LabelReadingChatClient(answeringScore: "80", otherScore: "80");

        // Act
        var verdict = await this.RunScenarioAsync(model);

        // Assert
        var failed = verdict.Metrics.Values
            .Where(static metric => metric.Interpretation is { Failed: true })
            .Select(static metric => metric.Name);

        Assert.Equal([RelevanceFilterEvaluator.NotAnsweringKeptName(PassageRelevanceFilterPlan.DefaultMinimumRelevance)], failed);
    }

    [Fact]
    public async Task RunAsync_AModelAnsweringNothing_FailsBothBoundsAsCountsTheFilterNeverMade()
    {
        // Arrange
        using var model = new ScriptedChatClient(" ", new ChatClientMetadata("scripted", defaultModelId: ModelUnderTest));

        // Act
        var verdict = await this.RunScenarioAsync(model);

        // Assert
        NumericMetric[] atDefault =
        [
            verdict.Get<NumericMetric>(RelevanceFilterEvaluator.AnsweringKeptName(PassageRelevanceFilterPlan.DefaultMinimumRelevance)),
            verdict.Get<NumericMetric>(RelevanceFilterEvaluator.NotAnsweringKeptName(PassageRelevanceFilterPlan.DefaultMinimumRelevance)),
        ];

        Assert.All(atDefault, static metric =>
        {
            Assert.True(metric.Interpretation?.Failed);
            Assert.Contains("unjudged", metric.Interpretation?.Reason, StringComparison.Ordinal);
        });
    }

    public void Dispose() => this.store.Delete(recursive: true);

    private static int? KeptAt(EvaluationResult verdict, string metricName) =>
        (int?)verdict.Get<NumericMetric>(metricName).Value;

    private static ChatGenerationPlan PlanFor(string model) =>
        ChatGenerationPlan.Create(
            new ChatEndpoint("evaluation", Address: null, model, ChatProviderApi.ChatCompletions, PublishedModelName: string.Empty),
            maximumOutputTokens: 16,
            temperature: null,
            topP: null,
            reasoningEffort: null,
            maximumMessagesPerRequest: 8,
            maximumRequestCharacters: 64_000,
            maximumRequestImageOctets: 1024,
            requestTimeout: TimeSpan.FromSeconds(30));

    private async Task<EvaluationResult> RunScenarioAsync(IChatClient model)
    {
        var reporting = EvaluationStore.OpenUnjudgedAt(
            this.store.FullName,
            "only",
            RelevanceFilterScenario.Evaluators);

        return await RelevanceFilterScenario.RunAsync(
            reporting,
            model,
            PlanFor(ModelUnderTest),
            repetition: 1,
            new SpendMeter(),
            TestContext.Current.CancellationToken);
    }

    /// <summary>A model that reads the labels: it scores a candidate one way when it answers the lookup it is judged against, and another way when it does not.</summary>
    /// <remarks>
    /// A judgement turn carries the lookup and the candidate's text, so the label is recovered from both — the lookup's
    /// text and one of its answering candidates' evidence — rather than from the candidate alone, because the same
    /// passage answers one lookup and not another.
    /// </remarks>
    private sealed class LabelReadingChatClient(string answeringScore, string otherScore) : IChatClient
    {
        public int Requests { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            this.Requests++;

            var turn = messages.Last().Text;
            var answers = LabelledCandidates.Lookups.Any(lookup =>
                turn.Contains(lookup.QueryText, StringComparison.Ordinal)
                && lookup.Candidates.Any(candidate => candidate.Answers && turn.Contains(candidate.Evidence, StringComparison.Ordinal)));

            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, answers ? answeringScore : otherScore)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Nothing here streams.");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
