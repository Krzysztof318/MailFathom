// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Embeddings.Indexing;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Embeddings;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>
/// Proves that the statements composed for a profile's approximate index are ones pgvector actually accepts, that two
/// generations of different widths each get an index of their own over the one dimensionless column, and that both
/// operations are repeatable.
/// </summary>
/// <remarks>
/// None of it is reachable without a real server. Whether an HNSW expression index over a cast column is legal, whether
/// two of them may coexist, and what <c>IF NOT EXISTS</c> does on the second call are all PostgreSQL's answers; a
/// substitute would report every one of them as satisfied. What a unit test already covers — that no text a caller
/// wrote reaches the statement — is deliberately not repeated here.
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedEmbeddingVectorIndexTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>
    /// One run over the whole lifecycle, because each step is what makes the next one meaningful: an index that exists
    /// is what a repeated build must not duplicate, and an index observed present is what makes its later absence a
    /// removal rather than a predicate that never matched. The second profile is there throughout to show that neither
    /// operation reaches beyond the generation it names.
    /// </summary>
    [Fact]
    public async Task MaintainingTheIndex_TwoProfilesOfDifferentWidths_EachKeepsItsOwnAndOnlyItsOwn()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var narrow = await RegisterProfileAsync(services, dimension: 2, "index-narrow", cancellationToken);
        var wide = await RegisterProfileAsync(services, dimension: 4, "index-wide", cancellationToken);

        // Act
        await EnsureBuiltAsync(services, narrow, cancellationToken);
        await EnsureBuiltAsync(services, narrow, cancellationToken);
        await EnsureBuiltAsync(services, wide, cancellationToken);

        // Assert
        var narrowDefinition = await IndexDefinitionAsync(services, narrow.Id, cancellationToken);
        var wideDefinition = await IndexDefinitionAsync(services, wide.Id, cancellationToken);

        Assert.NotNull(narrowDefinition);
        Assert.NotNull(wideDefinition);
        Assert.Contains("USING hnsw", narrowDefinition, StringComparison.Ordinal);
        Assert.Contains("vector(2)", narrowDefinition, StringComparison.Ordinal);
        Assert.Contains("vector_cosine_ops", narrowDefinition, StringComparison.Ordinal);
        Assert.Contains(narrow.Id.Value.ToString(), narrowDefinition, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("vector(4)", wideDefinition, StringComparison.Ordinal);

        // The name is derived from the profile's immutable identity, so the second build found the first index rather
        // than adding one beside it. Counted by the predicate rather than by the name, because a name that was not
        // derived is exactly the defect this would otherwise miss.
        Assert.Equal(1, await CountIndexesForAsync(services, narrow.Id, cancellationToken));

        await RemoveAsync(services, narrow.Id, cancellationToken);
        await RemoveAsync(services, narrow.Id, cancellationToken);

        Assert.Null(await IndexDefinitionAsync(services, narrow.Id, cancellationToken));
        Assert.NotNull(await IndexDefinitionAsync(services, wide.Id, cancellationToken));

        await RemoveAsync(services, wide.Id, cancellationToken);

        Assert.Equal(0, await CountIndexesForAsync(services, wide.Id, cancellationToken));
    }

    private static async Task EnsureBuiltAsync(
        OrchestratedMailFathomServices services,
        ActiveEmbeddingProfile profile,
        CancellationToken cancellationToken) => await services.InScopeAsync(
            async (scope, token) =>
            {
                await scope.GetRequiredService<IEmbeddingProfileVectorIndex>().EnsureBuiltAsync(profile, token);

                return profile.Id;
            },
            cancellationToken);

    private static async Task RemoveAsync(
        OrchestratedMailFathomServices services,
        EmbeddingProfileId profileId,
        CancellationToken cancellationToken) => await services.InScopeAsync(
            async (scope, token) =>
            {
                await scope.GetRequiredService<IEmbeddingProfileVectorIndex>().RemoveAsync(profileId, token);

                return profileId;
            },
            cancellationToken);

    private static async Task<ActiveEmbeddingProfile> RegisterProfileAsync(
        OrchestratedMailFathomServices services,
        int dimension,
        string modelIdentifier,
        CancellationToken cancellationToken)
    {
        var identity = EmbeddingProfileIdentity.Create(
            "mailfathom-test-vendor",
            modelIdentifier,
            modelVersion: null,
            dimension,
            EmbeddingDistanceMetric.Cosine,
            EmbeddingInputPreparation.Create(8_000, passageInstruction: null, normalizesVector: true));
        var profileId = Guid.CreateVersion7();

        await services.InScopeAsync(
            async (scope, token) =>
            {
                var context = scope.GetRequiredService<MailFathomDbContext>();

                context.EmbeddingProfiles.Add(new EmbeddingProfileEntity
                {
                    Id = profileId,
                    Provider = identity.Provider,
                    ModelIdentifier = identity.ModelIdentifier,
                    ModelVersion = identity.ModelVersion,
                    Dimension = identity.Dimension,
                    DistanceMetric = identity.DistanceMetric,
                    InputCharacterLimit = identity.InputPreparation.InputCharacterLimit,
                    PassageInstruction = identity.InputPreparation.PassageInstruction,
                    NormalizesVector = identity.InputPreparation.NormalizesVector,
                    IdentityFingerprint = EmbeddingProfileFingerprint.Compute(identity).Value,
                    LifecycleState = EmbeddingProfileLifecycleState.Active,
                    RegisteredAt = TimeProvider.System.GetUtcNow(),
                    ActivatedAt = TimeProvider.System.GetUtcNow(),
                });

                return await context.SaveChangesAsync(token);
            },
            cancellationToken);

        return new ActiveEmbeddingProfile(EmbeddingProfileId.Create(profileId), identity);
    }

    /// <summary>Reads what PostgreSQL says the index is, which is the only account of it a test can trust.</summary>
    /// <remarks>
    /// The name comes from the production composer rather than from a copy of it here. What this test is about is the
    /// database's answer; that the name is what it is, and short enough to survive, is the unit tests' question.
    /// </remarks>
    private static Task<string?> IndexDefinitionAsync(
        OrchestratedMailFathomServices services,
        EmbeddingProfileId profileId,
        CancellationToken cancellationToken)
    {
        var indexName = EmbeddingVectorIndexStatements.IndexNameFor(profileId);

        return services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().Database
                .SqlQuery<string>($"""SELECT indexdef AS "Value" FROM pg_indexes WHERE indexname = {indexName}""")
                .FirstOrDefaultAsync(token),
            cancellationToken);
    }

    /// <summary>Counts every approximate index restricted to one profile, whatever it happens to be named.</summary>
    private static Task<int> CountIndexesForAsync(
        OrchestratedMailFathomServices services,
        EmbeddingProfileId profileId,
        CancellationToken cancellationToken)
    {
        var predicate = $"%{profileId.Value}%";

        return services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().Database
                .SqlQuery<int>(
                    $"""
                     SELECT COUNT(*)::int AS "Value"
                     FROM pg_indexes
                     WHERE tablename = 'email_embeddings'
                       AND indexdef LIKE '%USING hnsw%'
                       AND indexdef LIKE {predicate}
                     """)
                .SingleAsync(token),
            cancellationToken);
    }
}
