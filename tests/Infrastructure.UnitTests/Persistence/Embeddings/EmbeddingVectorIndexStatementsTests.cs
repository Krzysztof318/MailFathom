// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings;
using MailFathom.Infrastructure.Persistence.Embeddings;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Embeddings;

/// <summary>
/// Covers what makes composed data-definition SQL safe to compose: that every value in the statement comes from a
/// registered profile's own columns or from a closed mapping, and that a name PostgreSQL would truncate is never
/// produced.
/// </summary>
/// <remarks>
/// Whether the database accepts these statements is the integration suite's question. What is asked here is the one a
/// database could not answer: that nothing a caller wrote reaches the text.
/// </remarks>
public sealed class EmbeddingVectorIndexStatementsTests
{
    private static readonly Guid ProfileIdentifier = new("0198f3d2-4b6a-7c1e-9f04-2a5b8c7d6e10");

    /// <summary>
    /// The statement casts the dimensionless column to the profile's width, measures under the profile's metric, and
    /// restricts itself to the profile's own rows — which together are what let one column carry two generations.
    /// </summary>
    [Fact]
    public void CreateIndexFor_ACosineProfile_ComposesThePartialExpressionIndexForItsWidth()
    {
        // Arrange
        var profile = ProfileOf(dimension: 1536, EmbeddingDistanceMetric.Cosine);

        // Act
        var statement = EmbeddingVectorIndexStatements.CreateIndexFor(profile);

        // Assert
        Assert.Equal(
            """
            CREATE INDEX IF NOT EXISTS "ix_email_embeddings_hnsw_0198f3d24b6a7c1e9f042a5b8c7d6e10"
            ON email_embeddings USING hnsw (("Embedding"::vector(1536)) vector_cosine_ops)
            WHERE "EmbeddingProfileId" = '0198f3d2-4b6a-7c1e-9f04-2a5b8c7d6e10'::uuid
            """,
            statement);
    }

    /// <summary>
    /// Each metric names the operator class pgvector measures it with. Indexing under the wrong one returns a number
    /// rather than an error, so the mapping is the whole of what keeps a distance meaning what it claims to mean.
    /// </summary>
    [Theory]
    [InlineData(EmbeddingDistanceMetric.Cosine, "vector_cosine_ops")]
    [InlineData(EmbeddingDistanceMetric.InnerProduct, "vector_ip_ops")]
    [InlineData(EmbeddingDistanceMetric.EuclideanDistance, "vector_l2_ops")]
    public void CreateIndexFor_EachDistanceMetric_NamesThePgvectorOperatorClassThatMeasuresIt(
        EmbeddingDistanceMetric distanceMetric,
        string expectedOperatorClass)
    {
        // Arrange
        var profile = ProfileOf(dimension: 768, distanceMetric);

        // Act
        var statement = EmbeddingVectorIndexStatements.CreateIndexFor(profile);

        // Assert
        Assert.Contains($"(\"Embedding\"::vector(768)) {expectedOperatorClass}", statement, StringComparison.Ordinal);
    }

    /// <summary>
    /// A metric outside the mapped set is refused rather than defaulted, because a default would index a space under a
    /// distance it was not built for and every search afterwards would return plausible wrong neighbours.
    /// </summary>
    [Fact]
    public void CreateIndexFor_AMetricWithNoOperatorClass_IsRefused()
    {
        // Arrange
        var profile = ProfileOf(dimension: 4, (EmbeddingDistanceMetric)int.MaxValue);

        // Act
        var refusal = Record.Exception(() => EmbeddingVectorIndexStatements.CreateIndexFor(profile));

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(refusal);
    }

    /// <summary>
    /// The provider and the model name a profile carries are vendor-supplied text this system stores verbatim, and no
    /// parameter can protect a utility statement. None of it reaches the statement at all, which is what makes that
    /// irrelevant rather than merely unlikely.
    /// </summary>
    [Fact]
    public void CreateIndexFor_AProfileWhoseNamesCarrySql_LeavesNoneOfThatTextInTheStatement()
    {
        // Arrange
        var profile = ProfileOf(
            dimension: 3,
            EmbeddingDistanceMetric.Cosine,
            provider: "'; DROP TABLE email_embeddings; --",
            modelIdentifier: "\" OR 1=1 --",
            passageInstruction: "'); DELETE FROM embedding_profiles; --");

        // Act
        var statement = EmbeddingVectorIndexStatements.CreateIndexFor(profile);

        // Assert
        Assert.DoesNotContain("DROP TABLE", statement, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE", statement, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OR 1=1", statement, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// PostgreSQL keeps sixty-three bytes of an identifier and silently truncates the rest. Two profiles whose names
    /// collided after truncation would look to <c>CREATE INDEX IF NOT EXISTS</c> like one index that already exists, so
    /// the second generation would be served by the first one's index.
    /// </summary>
    [Fact]
    public void IndexNameFor_AnyProfile_StaysInsideThePostgresIdentifierLimit()
    {
        // Arrange
        const int postgresIdentifierByteLimit = 63;

        // Act
        var name = EmbeddingVectorIndexStatements.IndexNameFor(EmbeddingProfileId.Create(ProfileIdentifier));

        // Assert
        Assert.Equal("ix_email_embeddings_hnsw_0198f3d24b6a7c1e9f042a5b8c7d6e10", name);
        Assert.True(name.Length <= postgresIdentifierByteLimit, $"The index name is {name.Length} characters.");
    }

    /// <summary>
    /// Removal is named from the identifier alone, so a caller holding nothing but a superseded generation's identifier
    /// can drop its index — and dropping one that was never built is not a failure.
    /// </summary>
    [Fact]
    public void DropIndexFor_AProfile_NamesTheIndexBuiltForItAndToleratesItsAbsence()
    {
        // Act
        var statement = EmbeddingVectorIndexStatements.DropIndexFor(EmbeddingProfileId.Create(ProfileIdentifier));

        // Assert
        Assert.Equal(
            "DROP INDEX IF EXISTS \"ix_email_embeddings_hnsw_0198f3d24b6a7c1e9f042a5b8c7d6e10\"",
            statement);
    }

    private static RegisteredEmbeddingProfile ProfileOf(
        int dimension,
        EmbeddingDistanceMetric distanceMetric,
        string provider = "mailfathom-test-vendor",
        string modelIdentifier = "test-embedding",
        string? passageInstruction = null) => new(
            EmbeddingProfileId.Create(ProfileIdentifier),
            EmbeddingProfileIdentity.Create(
                provider,
                modelIdentifier,
                modelVersion: null,
                dimension,
                distanceMetric,
                EmbeddingInputPreparation.Create(8000, passageInstruction, normalizesVector: true)));
}
