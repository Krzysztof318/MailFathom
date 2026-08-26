// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Embeddings.Vectorization;
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
/// Proves that automatic generation reaches every passage of a message exactly once, and that "exactly once" is
/// decided per profile.
/// </summary>
/// <remarks>
/// Neither claim is reachable without a real server. What is outstanding is a correlated <c>NOT EXISTS</c> over the
/// vector table, and what makes a repeat write nothing is that same query answering differently after the first commit;
/// a substitute for the database would report both as satisfied whatever the translation actually produced. The
/// vectors come from the deterministic generator, so the whole path costs no provider call.
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedEmailEmbeddingGenerationTests(MailFathomOrchestrationFixture orchestration)
{
    private const string FolderAlias = "embedding-generation";

    /// <summary>
    /// The whole live path over a real schema: the active profile is read, the passages without a vector under it are
    /// found, and the vectors are committed. Running it again finds nothing outstanding and writes nothing, which is
    /// what makes offering one message twice — a re-synchronization, a restart, a repeated queue entry — free.
    /// </summary>
    [Fact]
    public async Task EmbeddingAMessage_UnderTheActiveProfile_ReachesEveryPassageOnceAndRepeatsNothing()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var storedEmailId = await StoreOneMessageAsync(services, uid: 9201, cancellationToken);
        var profileId = await OrchestratedEmbeddingProfile.EnsureActiveDeterministicAsync(services, cancellationToken);

        // The control the "nothing more was written" assertion needs: a message with no outstanding passages and one
        // whose passages were never embedded are indistinguishable unless the first run is seen to produce vectors.
        var passageCount = await CountPassagesAsync(services, storedEmailId, cancellationToken);
        Assert.True(passageCount > 0);

        // Act
        var first = await EmbedAsync(services, storedEmailId, cancellationToken);
        var repeat = await EmbedAsync(services, storedEmailId, cancellationToken);

        // Assert
        Assert.Equal(StoredEmailEmbeddingOutcome.Embedded, first.Outcome);
        Assert.Equal(passageCount, first.EmbeddedChunkCount);
        Assert.Equal(StoredEmailEmbeddingOutcome.Embedded, repeat.Outcome);
        Assert.Equal(0, repeat.EmbeddedChunkCount);
        Assert.Equal(passageCount, await CountVectorsAsync(services, storedEmailId, profileId, cancellationToken));
    }

    /// <summary>
    /// A vector belongs to one space, so a passage embedded under one profile is untouched work under another. That is
    /// what lets a later generation be built beside the one still serving, and it is a property of the query rather
    /// than of any column on the passage.
    /// </summary>
    [Fact]
    public async Task ReadingOutstandingPassages_APassageEmbeddedUnderAnotherProfile_IsStillOutstandingHere()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var storedEmailId = await StoreOneMessageAsync(services, uid: 9202, cancellationToken);
        var previous = await RegisterProfileAsync(services, "superseded-generation", cancellationToken);
        var current = await RegisterProfileAsync(services, "current-generation", cancellationToken);
        var passageCount = await CountPassagesAsync(services, storedEmailId, cancellationToken);

        await StoreEveryVectorAsync(services, storedEmailId, previous, cancellationToken);

        // Act
        var outstandingForPrevious = await ReadOutstandingAsync(services, storedEmailId, previous, cancellationToken);
        var outstandingForCurrent = await ReadOutstandingAsync(services, storedEmailId, current, cancellationToken);

        // Assert
        Assert.Empty(outstandingForPrevious);
        Assert.Equal(passageCount, outstandingForCurrent.Count);
    }

    private static Task<StoredEmailEmbeddingRun> EmbedAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) =>
            {
                var serving = await scope.GetRequiredService<IActiveEmbeddingProfileReader>()
                    .FindActiveProfileAsync(token);

                return await scope.GetRequiredService<StoredEmailEmbeddingGenerator>().EmbedAsync(
                    storedEmailId,
                    Assert.IsType<RegisteredEmbeddingProfile>(serving),
                    token);
            },
            cancellationToken);

    private static Task<IReadOnlyList<EmailChunkAwaitingEmbedding>> ReadOutstandingAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        EmbeddingProfileId profileId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IEmailEmbeddingStore>().GetChunksAwaitingEmbeddingAsync(
                storedEmailId,
                profileId,
                maxCount: 1_000,
                token),
            cancellationToken);

    /// <summary>Stores one synthetic message and cuts it, which is the state an account run leaves one in.</summary>
    private static async Task<StoredEmailId> StoreOneMessageAsync(
        OrchestratedMailFathomServices services,
        uint uid,
        CancellationToken cancellationToken)
    {
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var occurrenceId = SyntheticEmail.OccurrenceIn(binding, uid);
        var subject = $"embedding-generation-{uid}";
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
                        SyntheticEmail.BodyTextContaining(subject, wordCount: 600),
                        "recipient@mailfathom.test"),
                    StoredEmailContentAvailability.Available,
                    token),
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);

        // The passages this class embeds, cut in their own transaction because storing no longer cuts.
        await OrchestratedPassages.CutAsync(services, storedEmailId, cancellationToken);

        return storedEmailId;
    }

    private static Task<int> CountPassagesAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().EmailChunks
                .AsNoTracking()
                .CountAsync(chunk => chunk.StoredEmailId == storedEmailId.Value, token),
            cancellationToken);

    /// <summary>Counts the vectors one message holds under one profile.</summary>
    /// <remarks>
    /// Narrowed to the message rather than counting the profile's whole table, because the suite shares one database
    /// and every class embedding into the deterministic geometry writes into the same profile. A count of everything
    /// would report whatever the classes before this one happened to leave behind.
    /// </remarks>
    private static Task<int> CountVectorsAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        EmbeddingProfileId profileId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().EmailEmbeddings
                .AsNoTracking()
                .CountAsync(
                    embedding => embedding.EmbeddingProfileId == profileId.Value
                        && embedding.EmailChunk!.StoredEmailId == storedEmailId.Value,
                    token),
            cancellationToken);

    /// <summary>Registers a geometry no generator in this process produces, which is what a superseded one looks like.</summary>
    private static Task<EmbeddingProfileId> RegisterProfileAsync(
        OrchestratedMailFathomServices services,
        string modelIdentifier,
        CancellationToken cancellationToken) => InsertProfileAsync(
            services,
            EmbeddingProfileIdentity.Create(
                "mailfathom-test-vendor",
                modelIdentifier,
                modelVersion: null,
                OrchestratedMailFathomServices.DeterministicEmbeddingDimension,
                EmbeddingDistanceMetric.Cosine,
                EmbeddingInputPreparation.Create(
                    OrchestratedMailFathomServices.DeterministicEmbeddingInputCharacterLimit,
                    passageInstruction: null,
                    normalizesVector: true)),
            EmbeddingProfileLifecycleState.Superseded,
            cancellationToken);

    private static Task<EmbeddingProfileId> InsertProfileAsync(
        OrchestratedMailFathomServices services,
        EmbeddingProfileIdentity identity,
        EmbeddingProfileLifecycleState lifecycleState,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) =>
            {
                var profileId = Guid.CreateVersion7();
                var context = scope.GetRequiredService<MailFathomDbContext>();
                var registeredAt = TimeProvider.System.GetUtcNow();

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
                    LifecycleState = lifecycleState,
                    RegisteredAt = registeredAt,
                    ActivatedAt = lifecycleState == EmbeddingProfileLifecycleState.Active ? registeredAt : null,
                });

                await context.SaveChangesAsync(token);

                return EmbeddingProfileId.Create(profileId);
            },
            cancellationToken);

    /// <summary>Writes one vector for every passage of a message, without asking a generator for it.</summary>
    /// <returns>How many rows the write produced, which the caller reads only through the assertions that follow it.</returns>
    private static Task<int> StoreEveryVectorAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        EmbeddingProfileId profileId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) =>
            {
                var context = scope.GetRequiredService<MailFathomDbContext>();
                var generatedAt = TimeProvider.System.GetUtcNow();

                var chunkIds = await context.EmailChunks
                    .AsNoTracking()
                    .Where(chunk => chunk.StoredEmailId == storedEmailId.Value)
                    .Select(chunk => chunk.Id)
                    .ToArrayAsync(token);

                context.EmailEmbeddings.AddRange(chunkIds.Select(chunkId => new EmailEmbeddingEntity
                {
                    EmailChunkId = chunkId,
                    EmbeddingProfileId = profileId.Value,
                    Dimension = OrchestratedMailFathomServices.DeterministicEmbeddingDimension,
                    Embedding = new Vector(
                        new float[OrchestratedMailFathomServices.DeterministicEmbeddingDimension]),
                    GeneratedAt = generatedAt,
                }));

                return await context.SaveChangesAsync(token);
            },
            cancellationToken);
}
