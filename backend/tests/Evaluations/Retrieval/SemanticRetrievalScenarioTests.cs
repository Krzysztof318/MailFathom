// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;
using MailFathom.Evaluations.Costing;
using MailFathom.Evaluations.Providers;
using MailFathom.Evaluations.Reporting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Xunit;

namespace MailFathom.Evaluations.Retrieval;

public sealed class SemanticRetrievalScenarioTests : IDisposable
{
    private const string ModelUnderTest = "model-under-test";

    private readonly DirectoryInfo store = Directory.CreateTempSubdirectory("mailfathom-evaluations-");

    [Fact]
    public void All_EveryCase_NamesEvidenceSomeMessageOfTheMailboxCarries()
    {
        // Act
        var cases = RetrievalCases.All;

        // Assert
        var mailbox = RetrievalCases.Mailbox.Select(static message => message.Id).ToHashSet();

        Assert.NotEmpty(cases);
        Assert.Equal(cases.Count, cases.Select(static retrievalCase => retrievalCase.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.All(cases, retrievalCase =>
        {
            Assert.NotEmpty(retrievalCase.Evidence);
            Assert.All(retrievalCase.Evidence, evidence => Assert.Subset(mailbox, evidence.Messages.ToHashSet()));
        });
    }

    [Fact]
    public async Task RunAsync_AModelPlacingEveryAnswerNearestItsQuestion_ReachesAllEvidenceFirst()
    {
        // Arrange
        using var generator = new LabelReadingGenerator();

        // Act
        var verdict = await this.RunScenarioAsync(generator, new EmbeddingModelUnderTest(ModelUnderTest, Address: null, Dimension: null));

        // Assert
        Assert.Equal(1, verdict.Get<NumericMetric>(RetrievalEvaluator.MeanReciprocalRankMetricName).Value);
        Assert.DoesNotContain(verdict.Metrics.Values, static metric => metric.Interpretation is { Failed: true });
    }

    [Fact]
    public async Task RunAsync_AModelPlacingEverythingAtOnePoint_FailsTheRecallFloorAlone()
    {
        // Arrange
        using var generator = new ConstantGenerator(width: 4);

        // Act
        var verdict = await this.RunScenarioAsync(generator, new EmbeddingModelUnderTest(ModelUnderTest, Address: null, Dimension: null));

        // Assert
        var failed = verdict.Metrics.Values
            .Where(static metric => metric.Interpretation is { Failed: true })
            .Select(static metric => metric.Name);

        Assert.Equal([RetrievalEvaluator.RecallMetricName(RetrievalEvaluator.FlooredDepth)], failed);
    }

    [Fact]
    public async Task RunAsync_ASecondRunOverTheSameStore_AsksTheModelNothingAndReportsNothingSpent()
    {
        // Arrange
        using var generator = new ConstantGenerator(width: 4);
        var model = new EmbeddingModelUnderTest(ModelUnderTest, Address: null, Dimension: null);

        await this.RunScenarioAsync(generator, model);
        var requestsOfFirstRun = generator.Requests;

        // Act
        var verdict = await this.RunScenarioAsync(generator, model);

        // Assert
        Assert.True(requestsOfFirstRun > 0);
        Assert.Equal(requestsOfFirstRun, generator.Requests);
        Assert.Equal("0", verdict.Get<NumericMetric>(EvaluationCost.MetricName).Metadata?["paid-calls"]);
    }

    [Fact]
    public async Task RunAsync_ADeclaredWidth_AsksForItAndCutsAWiderAnswerToIt()
    {
        // Arrange
        using var generator = new ConstantGenerator(width: 8);

        // Act
        var verdict = await this.RunScenarioAsync(generator, new EmbeddingModelUnderTest(ModelUnderTest, Address: null, Dimension: 4));

        // Assert
        Assert.All(generator.RequestedDimensions, static requested => Assert.Equal(4, requested));
        Assert.NotNull(verdict.Get<NumericMetric>(RetrievalEvaluator.MeanReciprocalRankMetricName).Value);
    }

    [Fact]
    public async Task RunAsync_AnAnswerNarrowerThanTheDeclaredWidth_FailsNamingBothWidths()
    {
        // Arrange
        using var generator = new ConstantGenerator(width: 4);

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            this.RunScenarioAsync(generator, new EmbeddingModelUnderTest(ModelUnderTest, Address: null, Dimension: 8)));

        // Assert
        Assert.Contains("answered 4 dimensions where 8 were asked for", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportedName_ADeclaredWidth_FilesTheModelUnderItsWidth()
    {
        // Assert
        Assert.Equal("vendor/model@256", new EmbeddingModelUnderTest("vendor/model", Address: null, Dimension: 256).ReportedName);
        Assert.Equal("vendor/model", new EmbeddingModelUnderTest("vendor/model", Address: null, Dimension: null).ReportedName);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("512", 512)]
    [InlineData(" 64 ", 64)]
    public void ParseDimension_AWholePositiveWidthOrNone_ReadsIt(string? declared, int? expected)
    {
        // Act
        var dimension = EmbeddingModelsUnderTest.ParseDimension(declared);

        // Assert
        Assert.Equal(expected, dimension);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-8")]
    [InlineData("wide")]
    [InlineData("1.5")]
    public void ParseDimension_AnythingElse_FailsNamingTheVariable(string declared)
    {
        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => EmbeddingModelsUnderTest.ParseDimension(declared));

        // Assert
        Assert.Contains(EmbeddingModelsUnderTest.DimensionVariable, failure.Message, StringComparison.Ordinal);
    }

    public void Dispose() => this.store.Delete(recursive: true);

    private async Task<EvaluationResult> RunScenarioAsync(
        IEmbeddingGenerator<string, Embedding<float>> generator,
        EmbeddingModelUnderTest model)
    {
        var reporting = EvaluationStore.OpenUnjudgedAt(this.store.FullName, "only", SemanticRetrievalScenario.Evaluators);

        return await SemanticRetrievalScenario.RunAsync(
            reporting,
            generator,
            model,
            new SpendMeter(),
            TestContext.Current.CancellationToken);
    }

    /// <summary>A model that reads the labels: a question points along its own case's axis, and a passage along the axis of every case its message is evidence for.</summary>
    /// <remarks>Every vector also carries a shared component, so a passage no case names still has a direction and ranks below every one that does.</remarks>
    private sealed class LabelReadingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        private readonly IReadOnlyList<RetrievalCase> cases = RetrievalCases.All;
        private readonly ILookup<string, StoredEmailId> messagesByPassage = RetrievalCases.Mailbox
            .SelectMany(static message => message.Passages.Select(passage => (passage.Text, message.Id)))
            .ToLookup(static pair => pair.Text, static pair => pair.Id, StringComparer.Ordinal);

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new GeneratedEmbeddings<Embedding<float>>([.. values.Select(value => new Embedding<float>(this.VectorOf(value)))]));

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }

        private float[] VectorOf(string text)
        {
            var vector = new float[this.cases.Count + 1];
            vector[^1] = 0.01f;

            var asked = this.cases.Select(static (retrievalCase, index) => (retrievalCase, index)).Where(pair => pair.retrievalCase.Question == text).ToArray();
            var carriers = this.messagesByPassage[text].ToHashSet();
            var answered = this.cases
                .Select(static (retrievalCase, index) => (retrievalCase, index))
                .Where(pair => pair.retrievalCase.Evidence.Any(evidence => evidence.Messages.Overlaps(carriers)));

            foreach (var (_, index) in asked.Length > 0 ? asked : answered)
            {
                vector[index] = 1;
            }

            return vector;
        }
    }

    /// <summary>A model answering every text with the same vector, counting what it was asked and at which width.</summary>
    private sealed class ConstantGenerator(int width) : IEmbeddingGenerator<string, Embedding<float>>
    {
        public int Requests { get; private set; }

        public List<int?> RequestedDimensions { get; } = [];

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            this.Requests++;
            this.RequestedDimensions.Add(options?.Dimensions);

            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(
                [.. values.Select(_ => new Embedding<float>(Enumerable.Repeat(1f, width).ToArray()))]));
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
