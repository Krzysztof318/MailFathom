// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using MailFathom.AI.Embeddings;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Evaluations.Costing;
using MailFathom.Evaluations.Providers;
using MailFathom.Evaluations.Reporting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Reporting;

namespace MailFathom.Evaluations.Retrieval;

/// <summary>Embeds the mailbox and every case's question under one model, ranks the mailbox per question, and files where the evidence landed.</summary>
/// <remarks>
/// <para>
/// A passage is the one a deployment embeds — the corpus cuts its bodies with the production chunker and rules — and
/// both passages and questions go through the preparation a deployment applies, so what is measured is the model on
/// the text it would be sent. What the scenario leaves out is everything that decides whether a call happens rather
/// than what it returns, for the reason <see cref="ProviderEmbeddingGenerator" /> gives, and the lexical half of a
/// hybrid search, whose in-memory stand-in here is not PostgreSQL's ranking.
/// </para>
/// <para>
/// Every vector goes through the run's response cache, one entry per text, so an unchanged mailbox and unchanged
/// questions cost nothing on the next run. A declared width is asked of the provider and any wider answer is cut to it
/// the way a deployment trimming vectors cuts it; an answer narrower than the width asked for fails the run, since no
/// cut can widen it.
/// </para>
/// </remarks>
internal static class SemanticRetrievalScenario
{
    /// <summary>The name the scenario is filed and reported under.</summary>
    public const string Name = "Retrieval.Semantic";

    /// <summary>How many texts one request carries: the most a deployment sends by default.</summary>
    private const int TextsPerRequest = 64;

    /// <summary>Gets what every run of this scenario is measured on.</summary>
    public static IReadOnlyList<IEvaluator> Evaluators => [new RetrievalEvaluator()];

    /// <summary>Runs the scenario under one model and files the measurement in the run's store.</summary>
    /// <param name="reporting">The run's store, opened without a judge.</param>
    /// <param name="generator">The model under test's generator, which stays the caller's.</param>
    /// <param name="model">The model, whose reported name is what the result is filed under.</param>
    /// <param name="modelSpend">What reaching that model has cost, which is the meter its generator is opened over.</param>
    /// <param name="cancellationToken">Withdraws the run.</param>
    /// <returns>The metrics the measurement produced.</returns>
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Disposing the caching wrapper would dispose the caller's generator, which this scenario does not own.")]
    public static async Task<EvaluationResult> RunAsync(
        ReportingConfiguration reporting,
        IEmbeddingGenerator<string, Embedding<float>> generator,
        EmbeddingModelUnderTest model,
        SpendMeter modelSpend,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reporting);
        ArgumentNullException.ThrowIfNull(model);

        var iterationName = EvaluationStore.IterationNameFor(model.ReportedName, repetition: 1);

        await using var scenarioRun = await reporting.CreateScenarioRunAsync(Name, iterationName, cancellationToken: cancellationToken);

        var cache = await reporting.ResponseCacheProvider!.GetCacheAsync(Name, iterationName, cancellationToken);
        var cachedGenerator = new DistributedCachingEmbeddingGenerator<string, Embedding<float>>(generator, cache)
        {
            CacheKeyAdditionalValues = [model.RoutedModelName, model.Address?.AbsoluteUri ?? string.Empty],
        };

        var cases = RetrievalCases.All;
        var vectors = await EmbedAsync(
            cachedGenerator,
            model,
            [.. RetrievalCases.Mailbox.SelectMany(static message => message.Passages.Select(static passage => passage.Text)), .. cases.Select(static retrievalCase => retrievalCase.Question)],
            cancellationToken);

        var mailbox = RetrievalCases.Mailbox
            .Select(message => (message, (IReadOnlyList<EmbeddingVector>)[.. message.Passages.Select(passage => vectors[passage.Text])]))
            .ToArray();
        var measurement = new RetrievalMeasurement(
            [.. cases.Select(retrievalCase => SemanticRanking.RanksOf(retrievalCase, SemanticRanking.Rank(mailbox, vectors[retrievalCase.Question])))]);

        var verdict = await scenarioRun.EvaluateAsync(
            [new ChatMessage(ChatRole.User, string.Join('\n', cases.Select(static retrievalCase => $"{retrievalCase.Name}: {retrievalCase.Question}")))],
            new ChatResponse(new ChatMessage(ChatRole.Assistant, measurement.Summary)) { ModelId = model.ReportedName },
            [measurement],
            cancellationToken);

        EvaluationCost.Record(verdict, model.ReportedName, modelSpend.Take(), judgeSpend: default);

        return verdict;
    }

    /// <summary>Embeds every distinct text once, in requests of a deployment's size, and reads each answer into the space the run measures.</summary>
    private static async Task<Dictionary<string, EmbeddingVector>> EmbedAsync(
        DistributedCachingEmbeddingGenerator<string, Embedding<float>> generator,
        EmbeddingModelUnderTest model,
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken)
    {
        var options = new EmbeddingGenerationOptions { Dimensions = model.Dimension };
        var vectors = new Dictionary<string, EmbeddingVector>(StringComparer.Ordinal);

        foreach (var batch in texts.Distinct(StringComparer.Ordinal).Chunk(TextsPerRequest))
        {
            var answered = await generator.GenerateAsync(
                [.. batch.Select(static text => EmbeddingPassagePreparation.Prepare(text, EmbeddingModelUnderTest.Preparation))],
                options,
                cancellationToken);

            if (answered.Count != batch.Length)
            {
                throw new InvalidOperationException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{model.RoutedModelName} answered {answered.Count} vectors for {batch.Length} texts."));
            }

            foreach (var (text, embedding) in batch.Zip(answered))
            {
                vectors[text] = InSpaceOf(model, embedding);
            }
        }

        return vectors;
    }

    private static EmbeddingVector InSpaceOf(EmbeddingModelUnderTest model, Embedding<float> embedding)
    {
        var vector = EmbeddingVector.Create(embedding.Vector.Span);

        return model.Dimension switch
        {
            null => vector,
            { } dimension when dimension <= vector.Dimension => vector.Shorten(dimension),
            { } dimension => throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"{model.RoutedModelName} answered {vector.Dimension} dimensions where {dimension} were asked for, and no cut can widen a vector.")),
        };
    }
}
