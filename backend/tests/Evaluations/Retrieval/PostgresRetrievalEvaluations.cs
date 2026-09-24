// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Evaluations.Costing;
using MailFathom.Evaluations.Providers;
using Xunit;

namespace MailFathom.Evaluations.Retrieval;

/// <summary>Measures the lexical, semantic, and hybrid rankings a deployment serves, over a PostgreSQL database seeded with the retrieval mailbox.</summary>
/// <remarks>
/// <para>
/// The one scenario in this suite that reaches a database. It needs a server and seeds a database on it, which an
/// ordinary run has no reason to pay for, so it runs only where a run names a server as well as asking for the
/// evaluations; a run naming none skips it rather than failing.
/// </para>
/// <para>
/// The mailbox is stored once and the lexical rankings measured once, because no model decides either; each declared
/// embedding model is then served in turn over the same database and measured alone and fused.
/// </para>
/// </remarks>
public sealed class PostgresRetrievalEvaluations
{
    /// <summary>The reason a run that names no server reports against the skipped scenario.</summary>
    public const string SkipReason =
        $"Measuring retrieval over PostgreSQL calls real embedding models and seeds a database, so it runs only when "
        + $"{AiEvaluationRun.EnablingVariable} is set to true and {RetrievalDatabase.ServerVariable} names a server.";

    /// <summary>Gets whether an evaluation run was asked for over a named server.</summary>
    /// <remarks>Public and static because that is the shape xUnit reads a skip condition from.</remarks>
    public static bool DatabaseEvaluationRequested =>
        AiEvaluationRun.Requested && AiEvaluationRun.Optional(RetrievalDatabase.ServerVariable) is not null;

    [Fact(Skip = SkipReason, SkipUnless = nameof(DatabaseEvaluationRequested))]
    public async Task RunAsync_LabelledQuestionsOverPostgreSql_EveryRankingIsFiledAndEveryModelReachesTheFloor()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var apiKey = EmbeddingModelsUnderTest.ApiKey();

        await using var database = await RetrievalDatabase.CreateAsync(
            AiEvaluationRun.Required(RetrievalDatabase.ServerVariable),
            RetrievalCases.Mailbox,
            TimeProvider.System,
            cancellationToken);

        // Act
        List<string> shortfalls = [.. await PostgresRetrievalScenario.MeasureLexicalAsync(database, cancellationToken)];

        // One model after another, because each is served over the same database and a deployment serves one profile.
        foreach (var model in EmbeddingModelsUnderTest.Declared())
        {
            shortfalls.AddRange(await MeasureAsync(database, model, apiKey, cancellationToken));
        }

        // Assert
        AiEvaluationRun.AssertNoShortfalls(shortfalls);
    }

    private static async Task<IReadOnlyList<string>> MeasureAsync(
        RetrievalDatabase database,
        EmbeddingModelUnderTest model,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var modelSpend = new SpendMeter();

        using var generator = ProviderEmbeddingGenerator.Open(model, apiKey, modelSpend);

        return await PostgresRetrievalScenario.MeasureModelAsync(database, generator, model, modelSpend, cancellationToken);
    }
}
