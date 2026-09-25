// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Search;
using MailFathom.Evaluations.Retrieval;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Npgsql;
using Xunit;

namespace MailFathom.Evaluations.UnitTests.Retrieval;

public sealed class PostgresRetrievalScenarioTests
{
    [Fact]
    public void All_EveryQuestion_IsAQueryTheLexicalSearchAccepts()
    {
        // Act
        var queries = RetrievalCases.All.Select(static retrievalCase => EmailSearchQueryText.Create(retrievalCase.Question)).ToArray();

        // Assert
        Assert.Equal(RetrievalCases.All.Count, queries.Length);
    }

    [Fact]
    public void All_EveryKeywordQuery_IsAQueryTheLexicalSearchAccepts()
    {
        // Act
        var queries = RetrievalCases.All.Select(static retrievalCase => EmailSearchQueryText.Create(retrievalCase.Keywords)).ToArray();

        // Assert
        Assert.Equal(RetrievalCases.All.Count, queries.Length);
    }

    [Fact]
    public async Task UnflooredEvaluators_ARankingReachingNoEvidence_ReportsItWithoutFailing()
    {
        // Act
        var verdict = await EvaluateAsync(PostgresRetrievalScenario.UnflooredEvaluators, NothingReached());

        // Assert
        Assert.Equal(0, verdict.Get<NumericMetric>(RetrievalEvaluator.RecallMetricName(RetrievalEvaluator.FlooredDepth)).Value);
        Assert.DoesNotContain(verdict.Metrics.Values, static metric => metric.Interpretation is { Failed: true });
    }

    [Fact]
    public async Task FlooredEvaluators_ARankingReachingNoEvidence_FailsTheRecallFloor()
    {
        // Act
        var verdict = await EvaluateAsync(PostgresRetrievalScenario.FlooredEvaluators, NothingReached());

        // Assert
        var recall = verdict.Get<NumericMetric>(RetrievalEvaluator.RecallMetricName(RetrievalEvaluator.FlooredDepth));

        Assert.True(recall.Interpretation?.Failed);
    }

    [Fact]
    public void ConnectionTo_AServerConnectionString_NamesTheDatabaseAndKeepsEverythingElse()
    {
        // Arrange
        const string server = "Host=127.0.0.1;Port=5433;Username=postgres;Database=postgres";

        // Act
        var connection = new NpgsqlConnectionStringBuilder(RetrievalDatabase.ConnectionTo(server, "mailfathom_retrieval_run"));

        // Assert
        Assert.Equal("mailfathom_retrieval_run", connection.Database);
        Assert.Equal("127.0.0.1", connection.Host);
        Assert.Equal(5433, connection.Port);
        Assert.Equal("postgres", connection.Username);
    }

    private static RetrievalMeasurement NothingReached() =>
        new([.. RetrievalCases.All.Select(static retrievalCase => SemanticRanking.RanksOf(retrievalCase, []))]);

    private static async Task<EvaluationResult> EvaluateAsync(IReadOnlyList<IEvaluator> evaluators, RetrievalMeasurement measurement) =>
        await Assert.Single(evaluators).EvaluateAsync(
            [],
            new ChatResponse(),
            additionalContext: [measurement],
            cancellationToken: TestContext.Current.CancellationToken);
}
