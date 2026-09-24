// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Emails.SearchEmails;
using MailFathom.Domain.Emails;
using MailFathom.Evaluations.Costing;
using MailFathom.Evaluations.Providers;
using MailFathom.Evaluations.Reporting;
using MailFathom.Host.Configuration.Embeddings;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Reporting;

namespace MailFathom.Evaluations.Retrieval;

/// <summary>Ranks every retrieval case through PostgreSQL — by the full-text index, by pgvector, and by the two fused — and files where the evidence landed.</summary>
/// <remarks>
/// <para>
/// The cases and the mailbox are <see cref="SemanticRetrievalScenario" />'s, so the in-memory semantic figure and the
/// one pgvector produces can be read side by side, and the lexical and hybrid figures are measured against the same
/// evidence. Each ranking is read at the depth a deployment's search reads it: the lexical ranking a lexical-only
/// instance serves at the window it returns, both halves of a fusion at the multiple of that window the search asks
/// them for, and the fusion itself by <see cref="HybridSearchRanking.Compose" /> into that window.
/// </para>
/// <para>
/// A question reaches the lexical ranking as written, which is what a person types into the search box, and every
/// word of it is required there. The lexical figure is therefore a baseline another lexical ranking is compared
/// against rather than a bar, and is reported without a floor; the semantic and hybrid figures are held to the floor
/// <see cref="RetrievalEvaluator" /> holds a ranking to.
/// </para>
/// </remarks>
internal static class PostgresRetrievalScenario
{
    /// <summary>The name the full-text ranking alone is filed and reported under.</summary>
    public const string LexicalName = "Retrieval.PostgreSQL.Lexical";

    /// <summary>The name pgvector's ranking alone is filed and reported under.</summary>
    public const string SemanticName = "Retrieval.PostgreSQL.Semantic";

    /// <summary>The name the fused ranking is filed and reported under.</summary>
    public const string HybridName = "Retrieval.PostgreSQL.Hybrid";

    /// <summary>The name the full-text ranking is filed under where a model's name would stand, since no model decides it.</summary>
    public const string LexicalReportedName = "full-text";

    /// <summary>How many results a search answers with when its caller names no limit, which is the window each ranking is read into.</summary>
    private const int ResultLimit = EmailSearchResultLimit.DefaultValue;

    /// <summary>How deep each half of a fusion is read, as a deployment's search reads it.</summary>
    private const int CandidateDepth = ResultLimit * MailboxSearchReader.FusionCandidateDepthMultiplier;

    /// <summary>Gets what the lexical baseline is measured on.</summary>
    public static IReadOnlyList<IEvaluator> LexicalEvaluators => [new RetrievalEvaluator(floorsRecall: false)];

    /// <summary>Gets what the semantic and hybrid rankings are measured on.</summary>
    public static IReadOnlyList<IEvaluator> ModelEvaluators => [new RetrievalEvaluator(floorsRecall: true)];

    /// <summary>Ranks every case by the full-text index alone and files the measurement.</summary>
    /// <param name="reporting">The run's store, opened over <see cref="LexicalEvaluators" /> without a judge.</param>
    /// <param name="database">The database holding the mailbox.</param>
    /// <param name="cancellationToken">Withdraws the run.</param>
    /// <returns>The metrics the measurement produced.</returns>
    public static async Task<EvaluationResult> MeasureLexicalAsync(
        ReportingConfiguration reporting,
        RetrievalDatabase database,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reporting);
        ArgumentNullException.ThrowIfNull(database);

        var cases = RetrievalCases.All;
        var rankings = new List<IReadOnlyList<StoredEmailId>>(cases.Count);

        foreach (var retrievalCase in cases)
        {
            var candidates = await database.RankLexicallyAsync(
                EmailSearchQueryText.Create(retrievalCase.Question),
                ResultLimit,
                cancellationToken);

            rankings.Add(IdentifiersOf(candidates));
        }

        return await FileAsync(reporting, LexicalName, LexicalReportedName, cases, rankings, paid: default, cancellationToken);
    }

    /// <summary>Serves one model's vectors, ranks every case by pgvector alone and fused with the full-text index, and files both measurements.</summary>
    /// <param name="reporting">The run's store, opened over <see cref="ModelEvaluators" /> without a judge.</param>
    /// <param name="database">The database holding the mailbox, whose serving profile becomes this model's.</param>
    /// <param name="generator">The model under test's generator, which stays the caller's.</param>
    /// <param name="model">The model, whose reported name is what both results are filed under.</param>
    /// <param name="modelSpend">What reaching that model has cost, which is the meter its generator is opened over.</param>
    /// <param name="cancellationToken">Withdraws the run.</param>
    /// <returns>The semantic measurement, then the hybrid one.</returns>
    public static async Task<IReadOnlyList<EvaluationResult>> MeasureModelAsync(
        ReportingConfiguration reporting,
        RetrievalDatabase database,
        IEmbeddingGenerator<string, Embedding<float>> generator,
        EmbeddingModelUnderTest model,
        SpendMeter modelSpend,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reporting);
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(modelSpend);

        var cases = RetrievalCases.All;
        var passages = await database.ReadPassagesAsync(cancellationToken);
        var vectors = await SemanticRetrievalScenario.EmbedThroughCacheAsync(
            reporting,
            SemanticName,
            generator,
            model,
            [.. passages.Select(static passage => passage.Text), .. cases.Select(static retrievalCase => retrievalCase.Question)],
            cancellationToken);
        var width = vectors[cases[0].Question].Dimension;
        var profile = await database.ServeAsync(
            ProfileOf(model, width),
            passages.ToDictionary(static passage => passage.ChunkId, passage => vectors[passage.Text]),
            cancellationToken);

        var semanticRankings = new List<IReadOnlyList<StoredEmailId>>(cases.Count);
        var hybridRankings = new List<IReadOnlyList<StoredEmailId>>(cases.Count);

        foreach (var retrievalCase in cases)
        {
            var semantic = await database.RankSemanticallyAsync(
                profile,
                vectors[retrievalCase.Question],
                CandidateDepth,
                cancellationToken);
            var lexical = await database.RankLexicallyAsync(
                EmailSearchQueryText.Create(retrievalCase.Question),
                CandidateDepth,
                cancellationToken);

            semanticRankings.Add(IdentifiersOf(semantic.Written));
            hybridRankings.Add(IdentifiersOf(HybridSearchRanking.Compose(lexical, semantic, ResultLimit).Candidates));
        }

        return
        [
            await FileAsync(reporting, SemanticName, model.ReportedName, cases, semanticRankings, modelSpend.Take(), cancellationToken),
            await FileAsync(reporting, HybridName, model.ReportedName, cases, hybridRankings, paid: default, cancellationToken),
        ];
    }

    /// <summary>Declares the profile a deployment serving this model at this width would have activated.</summary>
    private static EmbeddingProfileIdentity ProfileOf(EmbeddingModelUnderTest model, int dimension) =>
        EmbeddingProfileIdentity.Create(
            "evaluation",
            model.RoutedModelName,
            modelVersion: null,
            dimension,
            new EmbeddingEndpointOptions().DistanceMetric,
            EmbeddingModelUnderTest.Preparation);

    private static IReadOnlyList<StoredEmailId> IdentifiersOf(IReadOnlyList<RankedEmailCandidate> candidates) =>
        [.. candidates.Select(static candidate => candidate.StoredEmailId)];

    private static async Task<EvaluationResult> FileAsync(
        ReportingConfiguration reporting,
        string scenarioName,
        string reportedName,
        IReadOnlyList<RetrievalCase> cases,
        IReadOnlyList<IReadOnlyList<StoredEmailId>> rankings,
        PaidUsage paid,
        CancellationToken cancellationToken)
    {
        var iterationName = EvaluationStore.IterationNameFor(reportedName, repetition: 1);

        await using var scenarioRun = await reporting.CreateScenarioRunAsync(scenarioName, iterationName, cancellationToken: cancellationToken);

        var measurement = new RetrievalMeasurement(
            [.. cases.Zip(rankings, static (retrievalCase, ranking) => SemanticRanking.RanksOf(retrievalCase, ranking))]);

        var verdict = await scenarioRun.EvaluateAsync(
            [new ChatMessage(ChatRole.User, string.Join('\n', cases.Select(static retrievalCase => $"{retrievalCase.Name}: {retrievalCase.Question}")))],
            new ChatResponse(new ChatMessage(ChatRole.Assistant, measurement.Summary)) { ModelId = reportedName },
            [measurement],
            cancellationToken);

        EvaluationCost.Record(verdict, reportedName, paid, judgeSpend: default);

        return verdict;
    }
}
