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
/// The lexical side is measured twice over, because a deployment reads it two ways: a lexical-only instance matches any
/// word of the query and orders by cover density, and the lexical half of a fusion requires every word. Each is asked
/// both the question as written and the keyword query a model writes from it. Only the keyword queries are held to a
/// floor, the lexical-only ranking alone and the fusion whichever input it is given: those are what a deployment serves
/// to the models that search it, so a ranking that reaches nothing there fails the run. The rest is what the floors are
/// read against and the numbers a change to either ranking would rest on.
/// </para>
/// </remarks>
internal static class PostgresRetrievalScenario
{
    /// <summary>The name the full-text rankings alone are filed and reported under.</summary>
    public const string LexicalName = "Retrieval.PostgreSQL.Lexical";

    /// <summary>The name pgvector's ranking alone is filed and reported under.</summary>
    public const string SemanticName = "Retrieval.PostgreSQL.Semantic";

    /// <summary>The name the fused rankings are filed and reported under.</summary>
    public const string HybridName = "Retrieval.PostgreSQL.Hybrid";

    /// <summary>How many results a search answers with when its caller names no limit, which is the window each ranking is read into.</summary>
    private const int ResultLimit = EmailSearchResultLimit.DefaultValue;

    /// <summary>How deep each half of a fusion is read, as a deployment's search reads it.</summary>
    private const int CandidateDepth = ResultLimit * MailboxSearchReader.FusionCandidateDepthMultiplier;

    private enum LexicalInput
    {
        Question = 0,
        Keywords = 1,
    }

    /// <summary>Gets what a ranking a deployment serves, on the input a deployment is sent, is measured on.</summary>
    /// <remarks>
    /// The lexical-only ranking over the keyword queries is held to the same floor as an embedding model and a fusion:
    /// it reaches past it by some way on that input, so a regression fails while a query reworded by a word does not.
    /// </remarks>
    public static IReadOnlyList<IEvaluator> FlooredEvaluators => [new RetrievalEvaluator(RetrievalEvaluator.MinimumRecall)];

    /// <summary>Gets what a ranking asked what no deployment asks it is measured on.</summary>
    public static IReadOnlyList<IEvaluator> UnflooredEvaluators => [new RetrievalEvaluator(recallFloor: null)];

    /// <summary>Ranks every case by each way a deployment reads the lexical ranking, over the question and over the keyword query, and files the measurements.</summary>
    /// <param name="database">The database holding the mailbox.</param>
    /// <param name="cancellationToken">Withdraws the run.</param>
    /// <returns>One line per floor a ranking fell short of, naming the ranking.</returns>
    public static async Task<IReadOnlyList<string>> MeasureLexicalAsync(
        RetrievalDatabase database,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);

        var cases = RetrievalCases.All;
        var variants =
            from retrievalMode in (EmailSearchRetrievalMode[])[EmailSearchRetrievalMode.Hybrid, EmailSearchRetrievalMode.Lexical]
            from input in (LexicalInput[])[LexicalInput.Question, LexicalInput.Keywords]
            select (RetrievalMode: retrievalMode, Input: input);

        List<string> shortfalls = [];

        foreach (var (retrievalMode, input) in variants)
        {
            var queries = cases.Select(retrievalCase => QueryOf(retrievalCase, input)).ToArray();
            var rankings = new List<IReadOnlyList<StoredEmailId>>(cases.Count);

            foreach (var query in queries)
            {
                rankings.Add(IdentifiersOf(await RankLexicallyAsync(database, retrievalMode, query, ResultLimit, cancellationToken)));
            }

            var floored = retrievalMode is EmailSearchRetrievalMode.Lexical && input is LexicalInput.Keywords;

            shortfalls.AddRange(await FileAsync(
                EvaluationStore.OpenUnjudged(floored ? FlooredEvaluators : UnflooredEvaluators),
                LexicalName,
                $"{NameOf(retrievalMode)} {NameOf(input)}",
                cases,
                queries,
                rankings,
                paid: default,
                cancellationToken));
        }

        return shortfalls;
    }

    /// <summary>Serves one model's vectors, ranks every case by pgvector alone and fused with the lexical ranking, over both inputs, and files the measurements.</summary>
    /// <param name="database">The database holding the mailbox, whose serving profile becomes this model's.</param>
    /// <param name="generator">The model under test's generator, which stays the caller's.</param>
    /// <param name="model">The model, whose reported name is what every result is filed under.</param>
    /// <param name="modelSpend">What reaching that model has cost, which is the meter its generator is opened over.</param>
    /// <param name="cancellationToken">Withdraws the run.</param>
    /// <returns>One line per floor a ranking fell short of, naming the ranking.</returns>
    /// <remarks>
    /// A fusion reads one text into both halves, so each input is measured whole: the question, which is what the
    /// semantic figure has always rested on, and the keyword query, which is what a model that searches actually sends.
    /// The fusion is held to the floor on both, and pgvector alone on the question only, since a few words carry less
    /// meaning than the sentence they were taken from and the fusion is what a deployment serves with them.
    /// </remarks>
    public static async Task<IReadOnlyList<string>> MeasureModelAsync(
        RetrievalDatabase database,
        IEmbeddingGenerator<string, Embedding<float>> generator,
        EmbeddingModelUnderTest model,
        SpendMeter modelSpend,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(modelSpend);

        var floored = EvaluationStore.OpenUnjudged(FlooredEvaluators);
        var unfloored = EvaluationStore.OpenUnjudged(UnflooredEvaluators);
        var cases = RetrievalCases.All;
        var passages = await database.ReadPassagesAsync(cancellationToken);
        var vectors = await SemanticRetrievalScenario.EmbedThroughCacheAsync(
            floored,
            SemanticName,
            generator,
            model,
            [
                .. passages.Select(static passage => passage.Text),
                .. cases.Select(static retrievalCase => retrievalCase.Question),
                .. cases.Select(static retrievalCase => retrievalCase.Keywords),
            ],
            cancellationToken);
        var width = vectors[cases[0].Question].Dimension;
        var profile = await database.ServeAsync(
            ProfileOf(model, width),
            passages.ToDictionary(static passage => passage.ChunkId, passage => vectors[passage.Text]),
            cancellationToken);
        var paid = modelSpend.Take();

        List<string> shortfalls = [];

        foreach (var input in (LexicalInput[])[LexicalInput.Question, LexicalInput.Keywords])
        {
            var queries = cases.Select(retrievalCase => QueryOf(retrievalCase, input)).ToArray();
            var semanticRankings = new List<IReadOnlyList<StoredEmailId>>(cases.Count);
            var hybridRankings = new List<IReadOnlyList<StoredEmailId>>(cases.Count);

            foreach (var query in queries)
            {
                var semantic = await database.RankSemanticallyAsync(profile, vectors[query], CandidateDepth, cancellationToken);
                var lexical = await RankLexicallyAsync(database, EmailSearchRetrievalMode.Hybrid, query, CandidateDepth, cancellationToken);

                semanticRankings.Add(IdentifiersOf(semantic.Written));
                hybridRankings.Add(IdentifiersOf(HybridSearchRanking.Compose(lexical, semantic, ResultLimit).Candidates));
            }

            var isQuestion = input is LexicalInput.Question;
            var suffix = isQuestion ? string.Empty : " " + NameOf(input);

            shortfalls.AddRange(await FileAsync(isQuestion ? floored : unfloored, SemanticName, model.ReportedName + suffix, cases, queries, semanticRankings, isQuestion ? paid : default, cancellationToken));
            shortfalls.AddRange(await FileAsync(floored, HybridName, model.ReportedName + suffix, cases, queries, hybridRankings, paid: default, cancellationToken));
        }

        return shortfalls;
    }

    private static string QueryOf(RetrievalCase retrievalCase, LexicalInput input) =>
        input switch
        {
            LexicalInput.Question => retrievalCase.Question,
            LexicalInput.Keywords => retrievalCase.Keywords,
            _ => throw new ArgumentOutOfRangeException(nameof(input), input, "No such input."),
        };

    /// <summary>Names a lexical ranking after how it matches, which is what tells the two apart in a report.</summary>
    private static string NameOf(EmailSearchRetrievalMode retrievalMode) =>
        retrievalMode switch
        {
            EmailSearchRetrievalMode.Hybrid => "all-words",
            EmailSearchRetrievalMode.Lexical => "any-word",
            _ => throw new ArgumentOutOfRangeException(nameof(retrievalMode), retrievalMode, "No such retrieval mode."),
        };

    private static string NameOf(LexicalInput input) =>
        input switch
        {
            LexicalInput.Question => "question",
            LexicalInput.Keywords => "keywords",
            _ => throw new ArgumentOutOfRangeException(nameof(input), input, "No such input."),
        };

    /// <summary>Ranks by the full-text index as a search ranking the given way reads it.</summary>
    private static Task<IReadOnlyList<RankedEmailCandidate>> RankLexicallyAsync(
        RetrievalDatabase database,
        EmailSearchRetrievalMode retrievalMode,
        string query,
        int limit,
        CancellationToken cancellationToken) =>
        database.RankLexicallyAsync(EmailSearchQueryText.Create(query).MatchedUnder(retrievalMode), limit, cancellationToken);

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

    private static async Task<IReadOnlyList<string>> FileAsync(
        ReportingConfiguration reporting,
        string scenarioName,
        string reportedName,
        IReadOnlyList<RetrievalCase> cases,
        IReadOnlyList<string> queries,
        IReadOnlyList<IReadOnlyList<StoredEmailId>> rankings,
        PaidUsage paid,
        CancellationToken cancellationToken)
    {
        var iterationName = EvaluationStore.IterationNameFor(reportedName, repetition: 1);

        await using var scenarioRun = await reporting.CreateScenarioRunAsync(scenarioName, iterationName, cancellationToken: cancellationToken);

        var measurement = new RetrievalMeasurement(
            [.. cases.Zip(rankings, static (retrievalCase, ranking) => SemanticRanking.RanksOf(retrievalCase, ranking))]);

        var verdict = await scenarioRun.EvaluateAsync(
            [new ChatMessage(ChatRole.User, string.Join('\n', cases.Zip(queries, static (retrievalCase, query) => $"{retrievalCase.Name}: {query}")))],
            new ChatResponse(new ChatMessage(ChatRole.Assistant, measurement.Summary)) { ModelId = reportedName },
            [measurement],
            cancellationToken);

        EvaluationCost.Record(verdict, reportedName, paid, judgeSpend: default);

        return [.. EvaluationMetrics.ShortfallsOf(verdict).Select(shortfall => $"{scenarioName} under {reportedName}: {shortfall}")];
    }
}
