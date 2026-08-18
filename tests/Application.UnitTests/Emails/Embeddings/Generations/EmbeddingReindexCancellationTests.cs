// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Embeddings.Backfill;
using MailFathom.Application.Emails.Embeddings.Generations;
using MailFathom.Application.Emails.Embeddings.Indexing;
using MailFathom.Application.Persistence;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Embeddings.Generations;

public sealed class EmbeddingReindexCancellationTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Cancelling ends the reindex and changes nothing about what searches are answered from.</summary>
    [Fact]
    public async Task CancelAsync_AReindexRunning_AbandonsItAndLeavesTheServingGenerationServing()
    {
        // Arrange
        var world = CreateWorld();
        var serving = world.GenerationStore.Add(CreateIdentity("the-old-model"), EmbeddingProfileLifecycleState.Active);
        var building = world.GenerationStore.Add(
            CreateIdentity("a-newer-model"),
            EmbeddingProfileLifecycleState.Building);

        // Act
        var outcome = await world.Cancellation.CancelAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmbeddingReindexCancellationOutcome.Cancelled, outcome);
        Assert.Equal(EmbeddingProfileLifecycleState.Superseded, world.GenerationStore.StateOf(building.Id));
        Assert.Equal(EmbeddingProfileLifecycleState.Active, world.GenerationStore.StateOf(serving.Id));

        var generations = await world.GenerationStore.ReadGenerationsAsync(TestContext.Current.CancellationToken);
        Assert.Null(generations.Building);
        Assert.Equal(serving.Id, generations.Serving?.Id);
    }

    /// <summary>
    /// The abandoned generation's index goes with it, because every batched delete of its vectors would otherwise
    /// maintain an index nothing will ever read.
    /// </summary>
    [Fact]
    public async Task CancelAsync_AReindexRunning_RemovesTheApproximateIndexOfTheAbandonedGeneration()
    {
        // Arrange
        var world = CreateWorld();
        var building = world.GenerationStore.Add(CreateIdentity("a-model"), EmbeddingProfileLifecycleState.Building);

        // Act
        await world.Cancellation.CancelAsync(TestContext.Current.CancellationToken);

        // Assert
        await world.VectorIndex.Received(1).RemoveAsync(building.Id, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// What a cancellation leaves is a generation nothing reads whose partial vectors are personal data with no purpose
    /// left, and the pass that removes them is the one the interval before it chose while there was still a reindex to
    /// pace. Asking for it here is what keeps that removal from waiting out as much as a quarter of an hour.
    /// </summary>
    [Fact]
    public async Task CancelAsync_AReindexRunning_AsksForTheNextBackfillPassNowSoTheAbandonedVectorsGo()
    {
        // Arrange
        var world = CreateWorld();
        world.GenerationStore.Add(CreateIdentity("a-model"), EmbeddingProfileLifecycleState.Building);

        // Act
        await world.Cancellation.CancelAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(Now, world.BackfillSchedule.NextPassDueAt);
    }

    /// <summary>
    /// This command ends a reindex and is deliberately not a way to turn semantic search off, so an instance with only a
    /// serving generation is left untouched.
    /// </summary>
    [Fact]
    public async Task CancelAsync_NoReindexRunning_ReportsItAndTouchesNoGeneration()
    {
        // Arrange
        var world = CreateWorld();
        var serving = world.GenerationStore.Add(CreateIdentity("a-model"), EmbeddingProfileLifecycleState.Active);

        // Act
        var outcome = await world.Cancellation.CancelAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmbeddingReindexCancellationOutcome.NothingBuilding, outcome);
        Assert.Equal(EmbeddingProfileLifecycleState.Active, world.GenerationStore.StateOf(serving.Id));
        await world.VectorIndex.DidNotReceiveWithAnyArgs().RemoveAsync(default, TestContext.Current.CancellationToken);
        Assert.Null(world.BackfillSchedule.NextPassDueAt);
    }

    /// <summary>
    /// A reindex that completed between the read and the write took its generation into service, and abandoning that is
    /// not what this command means. Removing its index would be worse than the report: searches would go on being
    /// answered, exactly and slowly, with nothing saying why.
    /// </summary>
    [Fact]
    public async Task CancelAsync_TheReindexCompletedFirst_ChangesNothingAndLeavesTheIndexAlone()
    {
        // Arrange
        var vectorIndex = Substitute.For<IEmbeddingProfileVectorIndex>();
        var building = new RegisteredEmbeddingProfile(
            EmbeddingProfileId.Create(Guid.CreateVersion7()),
            CreateIdentity("a-model"));

        var generationStore = Substitute.For<IEmbeddingGenerationStore>();
        generationStore.ReadGenerationsAsync(Arg.Any<CancellationToken>())
            .Returns(new EmbeddingGenerations(Serving: null, building));
        generationStore.AbandonAsync(
                Arg.Any<IPersistenceSession>(),
                building.Id,
                Arg.Any<CancellationToken>())
            .Returns(false);

        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Substitute.For<IPersistenceSession>());

        var cancellation = new EmbeddingReindexCancellation(
            generationStore,
            vectorIndex,
            new OptimisticConcurrencyRetryPolicy(
                sessionFactory,
                new PersistenceConcurrencyOptions(),
                new FakeTimeProvider()),
            new EmbeddingBackfillSchedule(new FakeTimeProvider(Now)),
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminOperate));

        // Act
        var outcome = await cancellation.CancelAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmbeddingReindexCancellationOutcome.NothingBuilding, outcome);
        await vectorIndex.DidNotReceiveWithAnyArgs().RemoveAsync(default, TestContext.Current.CancellationToken);
    }

    private static EmbeddingProfileIdentity CreateIdentity(string modelIdentifier) =>
        EmbeddingProfileIdentity.Create(
            "a-provider",
            modelIdentifier,
            modelVersion: null,
            dimension: 8,
            EmbeddingDistanceMetric.Cosine,
            EmbeddingInputPreparation.Create(2_000, passageInstruction: null, normalizesVector: true));

    /// <summary>The grant is the authority here rather than at the transport, so an entrypoint that passed no filter meets the same refusal.</summary>
    [Fact]
    public async Task CancelAsync_ACallerGrantedOnlyTheAdministrativeRead_IsRefusedWithTheTransportAbsent()
    {
        // Arrange
        var world = CreateWorld(AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminRead));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            world.Cancellation.CancelAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminOperate, refusal.RequiredPermission);
    }

    private static CancellationWorld CreateWorld(AccessAuthorization? authorization = null)
    {
        var generationStore = new InMemoryEmbeddingGenerationStore(new InMemoryEmailEmbeddingStore());
        var vectorIndex = Substitute.For<IEmbeddingProfileVectorIndex>();

        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Substitute.For<IPersistenceSession>());

        var backfillSchedule = new EmbeddingBackfillSchedule(new FakeTimeProvider(Now));
        var cancellation = new EmbeddingReindexCancellation(
            generationStore,
            vectorIndex,
            new OptimisticConcurrencyRetryPolicy(
                sessionFactory,
                new PersistenceConcurrencyOptions(),
                new FakeTimeProvider()),
            backfillSchedule,
            authorization ?? AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminOperate));

        return new CancellationWorld(generationStore, vectorIndex, backfillSchedule, cancellation);
    }

    /// <summary>The generations and the collaborators one cancellation works against.</summary>
    private sealed record CancellationWorld(
        InMemoryEmbeddingGenerationStore GenerationStore,
        IEmbeddingProfileVectorIndex VectorIndex,
        EmbeddingBackfillSchedule BackfillSchedule,
        EmbeddingReindexCancellation Cancellation);
}
