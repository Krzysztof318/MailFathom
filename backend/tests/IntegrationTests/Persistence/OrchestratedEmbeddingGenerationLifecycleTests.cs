// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Embeddings.Generations;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pgvector;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>
/// Proves the generation lifecycle against a real schema: that a generation being built and one being read coexist,
/// that the switch between them is one transaction the partial unique index accepts, and that the superseded
/// generation's vectors are removed in bounded batches.
/// </summary>
/// <remarks>
/// None of it is reachable without a real server. The invariant under test is a partial unique index, so it is
/// PostgreSQL that decides whether two rows may claim to serve and PostgreSQL that decides whether the switch's two
/// statements are accepted in the order they are issued — a substitute would report every ordering as fine, including
/// the one that violates the index half the time. What a unit test already covers, which is what each transition means
/// and when it is taken, is deliberately not repeated here.
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedEmbeddingGenerationLifecycleTests(MailFathomOrchestrationFixture orchestration)
{
    private const string FolderAlias = "embedding-generations";

    private const int GenerationDimension = 2;

    /// <summary>The point every stored vector here is placed at, its value being irrelevant to what is under test.</summary>
    private static readonly float[] VectorComponents = [1f, 2f];

    /// <summary>
    /// One run over the whole lifecycle, because each step is what makes the next one meaningful: a generation that is
    /// serving is what a second one has to be built beside, the switch is what makes the first one superseded, and a
    /// superseded generation holding vectors is what the bounded removal is asked to empty.
    /// </summary>
    [Fact]
    public async Task TheGenerationLifecycle_AReindexThatCompletes_SwitchesOnceAndEmptiesWhatItReplaced()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var chunkIds = await StorePassagesAsync(services, uid: 9301, cancellationToken);
        var previous = await RegisterAsync(services, "generation-previous", cancellationToken);
        await SwitchToAsync(services, previous.Id, cancellationToken);

        // Drained after this switch rather than before it, because the residue this test has to be free of is not
        // superseded until the switch supersedes it. Any class that embedded under the deterministic profile leaves it
        // serving and holding vectors, and the claims below read a question the database answers about the whole
        // instance — which superseded generation still holds vectors — while what this test is about is the one
        // generation it supersedes itself. Without this, the reading names that class's generation rather than this
        // one's, for no reason except the order xUnit chose. Nothing this test created can be taken by it: `previous`
        // is serving here and holds no vector yet.
        await DrainSupersededVectorsAsync(services, cancellationToken);

        await StoreVectorsAsync(services, chunkIds, previous.Id, cancellationToken);

        // Act
        var next = await RegisterAsync(services, "generation-next", cancellationToken);
        var whileBuilding = await ReadGenerationsAsync(services, cancellationToken);
        await SwitchToAsync(services, next.Id, cancellationToken);
        var afterSwitch = await ReadGenerationsAsync(services, cancellationToken);

        // Assert
        Assert.Equal(previous.Id, whileBuilding.Serving?.Id);
        Assert.Equal(next.Id, whileBuilding.Building?.Id);
        Assert.Equal(next.Id, afterSwitch.Serving?.Id);
        Assert.Null(afterSwitch.Building);

        // The generation that was serving keeps its vectors until the removal reaches them, which is what makes the
        // switch itself an operation with nothing to wait for.
        Assert.Equal(chunkIds.Count, await CountVectorsAsync(services, previous.Id, cancellationToken));
        Assert.Equal(previous.Id, await FindSupersededHoldingVectorsAsync(services, cancellationToken));

        // A rollback that catches its own removal part-way through keeps what the generation still holds, and the
        // delete is what has to know that: the read that chose this generation happened in an earlier transaction, so
        // the statement re-checks the state rather than trusting the decision it was given.
        await RegisterAsync(services, "generation-previous", cancellationToken);
        Assert.Equal(0, await RemoveVectorsAsync(services, previous.Id, batchSize: 100, cancellationToken));
        Assert.Equal(chunkIds.Count, await CountVectorsAsync(services, previous.Id, cancellationToken));

        await AbandonAsync(services, previous.Id, cancellationToken);

        var firstBatch = await RemoveVectorsAsync(services, previous.Id, batchSize: 1, cancellationToken);
        var remainingBatch = await RemoveVectorsAsync(services, previous.Id, batchSize: 100, cancellationToken);

        Assert.Equal(1, firstBatch);
        Assert.Equal(chunkIds.Count - 1, remainingBatch);
        Assert.Equal(0, await CountVectorsAsync(services, previous.Id, cancellationToken));
        Assert.Null(await FindSupersededHoldingVectorsAsync(services, cancellationToken));
    }

    /// <summary>
    /// A second row claiming to serve is refused by the database rather than by the code that writes it, because the
    /// failure it prevents is silent: retrieval would read whichever row a query returned, leaving half the vectors in
    /// the table unreachable and nothing about the answers saying so.
    /// </summary>
    [Fact]
    public async Task RegisteringAGeneration_ASecondRowClaimingToServe_IsRefusedByTheDatabase()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var serving = await RegisterAsync(services, "generation-serving", cancellationToken);
        await SwitchToAsync(services, serving.Id, cancellationToken);

        // Act
        var refusal = await InsertServingRowAsync(services, "generation-intruder", cancellationToken);

        // Assert
        Assert.NotNull(refusal);
        Assert.Contains(
            PersistenceConstraintNames.EmbeddingProfileLifecycleUniqueIndexName,
            refusal.InnerException?.Message,
            StringComparison.Ordinal);

        // The control the refusal needs: the same row is accepted once it claims nothing the serving generation claims.
        Assert.Null(await InsertSupersededRowAsync(services, "generation-bystander", cancellationToken));

        // And the refusal reaches an application writer as a conflict rather than as a provider exception: two
        // activations of different geometries collide on this index, and the session is what classifies that.
        await RegisterAsync(services, "generation-first-to-build", cancellationToken);

        Assert.Equal(
            PersistenceCommitResult.ConcurrencyConflict,
            await TryRegisterAsync(services, "generation-second-to-build", cancellationToken));
    }

    private static EmbeddingProfileIdentity IdentityOf(string modelIdentifier) => EmbeddingProfileIdentity.Create(
        "mailfathom-test-vendor",
        modelIdentifier,
        modelVersion: null,
        GenerationDimension,
        EmbeddingDistanceMetric.Cosine,
        EmbeddingInputPreparation.Create(8_000, passageInstruction: null, normalizesVector: true));

    private static async Task<RegisteredEmbeddingProfile> RegisterAsync(
        OrchestratedMailFathomServices services,
        string modelIdentifier,
        CancellationToken cancellationToken)
    {
        RegisteredEmbeddingProfile? registered = null;

        var commitResult = await services.CommitAsync(
            async (scope, session, token) => registered = await scope.GetRequiredService<IEmbeddingGenerationStore>()
                .RegisterBuildingAsync(session, IdentityOf(modelIdentifier), token),
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);

        return Assert.IsType<RegisteredEmbeddingProfile>(registered);
    }

    private static async Task SwitchToAsync(
        OrchestratedMailFathomServices services,
        EmbeddingProfileId built,
        CancellationToken cancellationToken)
    {
        var switched = false;

        var commitResult = await services.CommitAsync(
            async (scope, session, token) => switched = await scope.GetRequiredService<IEmbeddingGenerationStore>()
                .SwitchToAsync(session, built, token),
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);

        // The store refuses to switch to a generation that is no longer being built, so a false here would mean the
        // arrangement never registered one rather than that the switch merely did nothing.
        Assert.True(switched);
    }

    /// <summary>Registers a generation through the real session, and answers with what the commit reported.</summary>
    /// <remarks>
    /// Unlike <c>RegisterAsync</c>, this one asserts nothing about the outcome: it exists to observe a refusal arriving
    /// as a classified conflict rather than as the provider exception underneath it.
    /// </remarks>
    private static Task<PersistenceCommitResult> TryRegisterAsync(
        OrchestratedMailFathomServices services,
        string modelIdentifier,
        CancellationToken cancellationToken) => services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IEmbeddingGenerationStore>()
                .RegisterBuildingAsync(session, IdentityOf(modelIdentifier), token),
            cancellationToken);

    private static async Task AbandonAsync(
        OrchestratedMailFathomServices services,
        EmbeddingProfileId building,
        CancellationToken cancellationToken)
    {
        var abandoned = false;

        var commitResult = await services.CommitAsync(
            async (scope, session, token) => abandoned = await scope.GetRequiredService<IEmbeddingGenerationStore>()
                .AbandonAsync(session, building, token),
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);
        Assert.True(abandoned);
    }

    private static Task<EmbeddingGenerations> ReadGenerationsAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IEmbeddingGenerationStore>().ReadGenerationsAsync(token),
            cancellationToken);

    /// <summary>Empties whatever superseded generation another class left holding vectors.</summary>
    /// <remarks>
    /// Bounded rather than looped until the query is quiet on its own: a removal that reports nothing removed while the
    /// same generation is still named would be an unbounded loop, so it fails the test instead of hanging it.
    /// </remarks>
    private static async Task DrainSupersededVectorsAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken)
    {
        const int maximumBatches = 100;

        for (var batch = 0; batch < maximumBatches; batch++)
        {
            if (await FindSupersededHoldingVectorsAsync(services, cancellationToken) is not { } leftover)
            {
                return;
            }

            Assert.True(
                await RemoveVectorsAsync(services, leftover, batchSize: 500, cancellationToken) > 0,
                "A superseded generation reported as holding vectors has to give some up when it is asked.");
        }

        Assert.Fail("A superseded generation went on holding vectors after every batch this test is allowed.");
    }

    private static Task<EmbeddingProfileId?> FindSupersededHoldingVectorsAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IEmbeddingGenerationStore>()
                .FindSupersededProfileHoldingVectorsAsync(token),
            cancellationToken);

    private static async Task<int> RemoveVectorsAsync(
        OrchestratedMailFathomServices services,
        EmbeddingProfileId profileId,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var removedVectorCount = 0;

        var commitResult = await services.CommitAsync(
            async (scope, session, token) => removedVectorCount = await scope
                .GetRequiredService<IEmbeddingGenerationStore>()
                .RemoveVectorsAsync(session, profileId, batchSize, token),
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);

        return removedVectorCount;
    }

    /// <summary>Stores one synthetic message and answers with the passages the chunk writer derived for it.</summary>
    private static async Task<IReadOnlyList<Guid>> StorePassagesAsync(
        OrchestratedMailFathomServices services,
        uint uid,
        CancellationToken cancellationToken)
    {
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var occurrenceId = SyntheticEmail.OccurrenceIn(binding, uid);
        var subject = $"generation-{uid}";

        var storedEmailId = default(StoredEmailId);
        var commitResult = await services.CommitAsync(
            async (scope, session, token) => storedEmailId = await scope
                .GetRequiredService<IEmailMetadataRepository>()
                .UpsertMetadataAsync(
                    session, SyntheticMailAccount.Owner,
                    SyntheticEmail.RemoteMetadataOf(occurrenceId, subject),
                    SyntheticEmail.ExtractionOf(
                        occurrenceId,
                        subject,
                        SyntheticEmail.BodyTextContaining(subject, wordCount: 400),
                        "recipient@mailfathom.test"),
                    StoredEmailContentAvailability.Available,
                    token),
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);

        // The passages a generation is built over, cut in their own transaction because storing no longer cuts.
        await OrchestratedPassages.CutAsync(services, storedEmailId, cancellationToken);

        var alias = occurrenceId.FolderResolutionId.Alias.Value;
        IReadOnlyList<Guid> chunkIds = await services.InScopeAsync(
            async (scope, token) => await scope.GetRequiredService<MailFathomDbContext>().EmailChunks
                .AsNoTracking()
                .Where(chunk => chunk.StoredEmail.MailFolder.MailboxAccountId == occurrenceId.AccountId.Value
                    && chunk.StoredEmail.MailFolder.Alias == alias
                    && chunk.StoredEmail.Uid == occurrenceId.Uid.Value)
                .OrderBy(chunk => chunk.Ordinal)
                .Select(chunk => chunk.Id)
                .ToArrayAsync(token),
            cancellationToken);

        Assert.NotEmpty(chunkIds);

        return chunkIds;
    }

    /// <summary>Gives one generation a vector for every passage, which is what a completed reindex leaves behind.</summary>
    private static Task<int> StoreVectorsAsync(
        OrchestratedMailFathomServices services,
        IReadOnlyList<Guid> chunkIds,
        EmbeddingProfileId profileId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) =>
            {
                var context = scope.GetRequiredService<MailFathomDbContext>();

                context.EmailEmbeddings.AddRange(chunkIds.Select(chunkId => new EmailEmbeddingEntity
                {
                    EmailChunkId = chunkId,
                    EmbeddingProfileId = profileId.Value,
                    Dimension = GenerationDimension,
                    Embedding = new Vector(VectorComponents),
                    GeneratedAt = TimeProvider.System.GetUtcNow(),
                }));

                return await context.SaveChangesAsync(token);
            },
            cancellationToken);

    private static Task<int> CountVectorsAsync(
        OrchestratedMailFathomServices services,
        EmbeddingProfileId profileId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().EmailEmbeddings
                .AsNoTracking()
                .CountAsync(vector => vector.EmbeddingProfileId == profileId.Value, token),
            cancellationToken);

    private static Task<DbUpdateException?> InsertServingRowAsync(
        OrchestratedMailFathomServices services,
        string modelIdentifier,
        CancellationToken cancellationToken) => InsertRowAsync(
            services,
            modelIdentifier,
            EmbeddingProfileLifecycleState.Active,
            cancellationToken);

    private static Task<DbUpdateException?> InsertSupersededRowAsync(
        OrchestratedMailFathomServices services,
        string modelIdentifier,
        CancellationToken cancellationToken) => InsertRowAsync(
            services,
            modelIdentifier,
            EmbeddingProfileLifecycleState.Superseded,
            cancellationToken);

    /// <summary>Inserts a profile row directly, and answers with what the database refused or <see langword="null" />.</summary>
    private static Task<DbUpdateException?> InsertRowAsync(
        OrchestratedMailFathomServices services,
        string modelIdentifier,
        EmbeddingProfileLifecycleState lifecycleState,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) =>
            {
                var identity = IdentityOf(modelIdentifier);
                var context = scope.GetRequiredService<MailFathomDbContext>();
                var registeredAt = TimeProvider.System.GetUtcNow();

                context.EmbeddingProfiles.Add(new EmbeddingProfileEntity
                {
                    Id = Guid.CreateVersion7(),
                    Provider = identity.Provider,
                    ModelIdentifier = identity.ModelIdentifier,
                    ModelVersion = identity.ModelVersion,
                    Dimension = identity.Dimension,
                    DistanceMetric = identity.DistanceMetric,
                    InputCharacterLimit = identity.InputPreparation.InputCharacterLimit,
                    PassageInstruction = identity.InputPreparation.PassageInstruction,
                    NormalizesVector = identity.InputPreparation.NormalizesVector,
                    IdentityFingerprint = EmbeddingProfileFingerprint.Compute(identity).Value,
                    LifecycleState = lifecycleState,
                    RegisteredAt = registeredAt,
                    ActivatedAt = lifecycleState == EmbeddingProfileLifecycleState.Active ? registeredAt : null,
                });

                try
                {
                    await context.SaveChangesAsync(token);

                    return null;
                }
                catch (DbUpdateException refusal)
                {
                    return refusal;
                }
            },
            cancellationToken);
}
