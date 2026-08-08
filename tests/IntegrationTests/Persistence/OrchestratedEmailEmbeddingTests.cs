// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings;
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
/// Proves the two guarantees the vector schema exists for: that a profile's geometry is registered once, and that a
/// stored vector cannot disagree with the width the profile it names declares.
/// </summary>
/// <remarks>
/// None of this is reachable without a real server. The dimension rule is a check constraint over a pgvector function
/// paired with a composite foreign key, the identity rule is a unique index, and the erasure is the chunk's own
/// <c>ON DELETE CASCADE</c> — a substitute for the database would report every one of them as satisfied.
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedEmailEmbeddingTests(MailFathomOrchestrationFixture orchestration)
{
    private const string FolderAlias = "email-embeddings";

    /// <summary>
    /// The width dropped from the column is not a width dropped from the schema. A vector of the profile's dimension is
    /// stored; one of any other length is refused, whether it disagrees with the number beside it or with the profile
    /// that number claims to come from.
    /// </summary>
    [Fact]
    public async Task StoringAVector_ADimensionTheProfileDoesNotDeclare_IsRefusedByTheDatabase()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var chunkId = await StoreOnePassageAsync(services, uid: 9101, cancellationToken);
        var profileId = await RegisterProfileAsync(services, dimension: 3, modelIdentifier: "refuses", cancellationToken);

        // Act
        var mismatchedVector = await StoreVectorAsync(services, chunkId, profileId, 3, [1f, 2f], cancellationToken);
        var undeclaredDimension = await StoreVectorAsync(services, chunkId, profileId, 2, [1f, 2f], cancellationToken);
        var matching = await StoreVectorAsync(services, chunkId, profileId, 3, [1f, 2f, 3f], cancellationToken);

        // Assert
        Assert.NotNull(mismatchedVector);
        Assert.NotNull(undeclaredDimension);
        Assert.Null(matching);
        Assert.Equal(1, await CountVectorsAsync(services, profileId, cancellationToken));
    }

    /// <summary>
    /// Two profiles of different widths share one dimensionless column, which is what lets a new generation be built
    /// while the previous one still serves. Re-registering a geometry already present is refused instead, so returning
    /// to a previous model resolves to the profile that exists rather than paying for a duplicate of it.
    /// </summary>
    [Fact]
    public async Task RegisteringAProfile_TwoGeometriesAndOneRepeatedIdentity_CoexistExceptForTheRepeat()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var chunkId = await StoreOnePassageAsync(services, uid: 9102, cancellationToken);
        var narrow = await RegisterProfileAsync(services, dimension: 2, modelIdentifier: "narrow", cancellationToken);
        var wide = await RegisterProfileAsync(services, dimension: 4, modelIdentifier: "wide", cancellationToken);

        // Act
        var repeated = await RegisterDuplicateAsync(services, dimension: 2, modelIdentifier: "narrow", cancellationToken);

        // Assert
        Assert.NotNull(repeated);
        Assert.Null(await StoreVectorAsync(services, chunkId, narrow, 2, [1f, 2f], cancellationToken));
        Assert.Null(await StoreVectorAsync(services, chunkId, wide, 4, [1f, 2f, 3f, 4f], cancellationToken));
        Assert.Equal(1, await CountVectorsAsync(services, narrow, cancellationToken));
        Assert.Equal(1, await CountVectorsAsync(services, wide, cancellationToken));
    }

    /// <summary>
    /// Deleting a message deletes everything derived from it. The vector hangs on the chunk, which hangs on the email,
    /// so erasure reaches it without a rule anybody has to remember — and the profile it named survives, because a
    /// profile is what a stored vector's attribution points at rather than something a message owns.
    /// </summary>
    [Fact]
    public async Task DeletingAnEmail_ItsStoredVectors_AreErasedAndTheProfileSurvives()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var occurrenceId = SyntheticEmail.OccurrenceIn(
            await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken),
            uid: 9103);
        var chunkId = await StorePassageOfAsync(services, occurrenceId, cancellationToken);
        var profileId = await RegisterProfileAsync(services, dimension: 3, modelIdentifier: "erased", cancellationToken);

        Assert.Null(await StoreVectorAsync(services, chunkId, profileId, 3, [1f, 2f, 3f], cancellationToken));

        // The control the absence assertion needs: the vector is there to be found until the email is deleted, so an
        // emptied count afterwards reports the cascade rather than a predicate that never matched anything.
        Assert.Equal(1, await CountVectorsAsync(services, profileId, cancellationToken));

        // Act
        Assert.Equal(1, await DeleteEmailAsync(services, occurrenceId, cancellationToken));

        // Assert
        Assert.Equal(0, await CountVectorsAsync(services, profileId, cancellationToken));
        Assert.True(await ProfileExistsAsync(services, profileId, cancellationToken));
    }

    private static async Task<Guid> StoreOnePassageAsync(
        OrchestratedMailFathomServices services,
        uint uid,
        CancellationToken cancellationToken)
    {
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);

        return await StorePassageOfAsync(services, SyntheticEmail.OccurrenceIn(binding, uid), cancellationToken);
    }

    private static async Task<Guid> StorePassageOfAsync(
        OrchestratedMailFathomServices services,
        EmailOccurrenceId occurrenceId,
        CancellationToken cancellationToken)
    {
        var subject = $"embedding-{occurrenceId.Uid.Value}";

        var commitResult = await services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IEmailMetadataRepository>().UpsertMetadataAsync(
                session,
                SyntheticEmail.RemoteMetadataOf(occurrenceId, subject),
                SyntheticEmail.ExtractionOf(
                    occurrenceId,
                    subject,
                    SyntheticEmail.BodyTextContaining(subject, wordCount: 60),
                    "recipient@mailfathom.test"),
                StoredEmailContentAvailability.Available,
                token),
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);

        return await services.InScopeAsync(
            async (scope, token) =>
            {
                var alias = occurrenceId.FolderResolutionId.Alias.Value;

                return await scope.GetRequiredService<MailFathomDbContext>().EmailChunks
                    .AsNoTracking()
                    .Where(chunk => chunk.StoredEmail.MailFolder.MailboxAccountId == occurrenceId.AccountId.Value
                        && chunk.StoredEmail.MailFolder.Alias == alias
                        && chunk.StoredEmail.Uid == occurrenceId.Uid.Value)
                    .OrderBy(chunk => chunk.Ordinal)
                    .Select(chunk => chunk.Id)
                    .FirstAsync(token);
            },
            cancellationToken);
    }

    private static async Task<Guid> RegisterProfileAsync(
        OrchestratedMailFathomServices services,
        int dimension,
        string modelIdentifier,
        CancellationToken cancellationToken)
    {
        var profileId = Guid.CreateVersion7();

        var failure = await InsertProfileAsync(services, profileId, dimension, modelIdentifier, cancellationToken);

        Assert.Null(failure);

        return profileId;
    }

    private static Task<DbUpdateException?> RegisterDuplicateAsync(
        OrchestratedMailFathomServices services,
        int dimension,
        string modelIdentifier,
        CancellationToken cancellationToken) =>
        InsertProfileAsync(services, Guid.CreateVersion7(), dimension, modelIdentifier, cancellationToken);

    private static Task<DbUpdateException?> InsertProfileAsync(
        OrchestratedMailFathomServices services,
        Guid profileId,
        int dimension,
        string modelIdentifier,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) =>
            {
                var identity = EmbeddingProfileIdentity.Create(
                    "mailfathom-test-vendor",
                    modelIdentifier,
                    modelVersion: null,
                    dimension,
                    EmbeddingDistanceMetric.Cosine,
                    EmbeddingInputPreparation.Create(8000, passageInstruction: null, normalizesVector: true));
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
                    LifecycleState = EmbeddingProfileLifecycleState.Superseded,
                    RegisteredAt = TimeProvider.System.GetUtcNow(),
                });

                return await SaveAndReportRefusalAsync(context, token);
            },
            cancellationToken);

    /// <summary>Writes one vector and reports what the database refused, or <see langword="null" /> when it accepted.</summary>
    private static Task<DbUpdateException?> StoreVectorAsync(
        OrchestratedMailFathomServices services,
        Guid chunkId,
        Guid profileId,
        int recordedDimension,
        float[] values,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) =>
            {
                var context = scope.GetRequiredService<MailFathomDbContext>();

                context.EmailEmbeddings.Add(new EmailEmbeddingEntity
                {
                    EmailChunkId = chunkId,
                    EmbeddingProfileId = profileId,
                    Dimension = recordedDimension,
                    Embedding = new Vector(values),
                    GeneratedAt = TimeProvider.System.GetUtcNow(),
                });

                return await SaveAndReportRefusalAsync(context, token);
            },
            cancellationToken);

    private static async Task<DbUpdateException?> SaveAndReportRefusalAsync(
        MailFathomDbContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);

            return null;
        }
        catch (DbUpdateException refusal)
        {
            return refusal;
        }
    }

    private static Task<int> CountVectorsAsync(
        OrchestratedMailFathomServices services,
        Guid profileId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().EmailEmbeddings
                .AsNoTracking()
                .CountAsync(embedding => embedding.EmbeddingProfileId == profileId, token),
            cancellationToken);

    private static Task<bool> ProfileExistsAsync(
        OrchestratedMailFathomServices services,
        Guid profileId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().EmbeddingProfiles
                .AsNoTracking()
                .AnyAsync(profile => profile.Id == profileId, token),
            cancellationToken);

    private static Task<int> DeleteEmailAsync(
        OrchestratedMailFathomServices services,
        EmailOccurrenceId occurrenceId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) =>
            {
                var alias = occurrenceId.FolderResolutionId.Alias.Value;

                return await scope.GetRequiredService<MailFathomDbContext>().StoredEmails
                    .Where(email => email.MailFolder.MailboxAccountId == occurrenceId.AccountId.Value
                        && email.MailFolder.Alias == alias
                        && email.Uid == occurrenceId.Uid.Value)
                    .ExecuteDeleteAsync(token);
            },
            cancellationToken);
}
