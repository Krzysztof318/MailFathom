// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Embeddings.Backfill;
using MailFathom.Application.Emails.Embeddings.Generations;
using MailFathom.Application.Emails.Embeddings.Indexing;
using MailFathom.Application.Persistence;
using MailFathom.Application.UnitTests.TestDoubles;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Embeddings.Generations;

public sealed class EmbeddingProfileActivationTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The whole guarantee in one assertion: the new geometry becomes a generation that is built rather than one that
    /// is read, and the generation answering searches is left exactly where it was.
    /// </summary>
    [Fact]
    public async Task ActivateAsync_ADifferentGeometryWhileOneIsServing_BuildsBesideItWithoutTakingItOutOfService()
    {
        // Arrange
        var world = CreateWorld();
        var serving = world.GenerationStore.Add(CreateIdentity("the-old-model"), EmbeddingProfileLifecycleState.Active);

        // Act
        var result = await world.Activation.ActivateAsync(
            CreateIdentity("a-newer-model"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmbeddingProfileActivationOutcome.ReindexStarted, result.Outcome);
        Assert.Equal(
            EmbeddingProfileLifecycleState.Building,
            world.GenerationStore.StateOf(result.ProfileId));
        Assert.Equal(EmbeddingProfileLifecycleState.Active, world.GenerationStore.StateOf(serving.Id));

        var generations = await world.GenerationStore.ReadGenerationsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(serving.Id, generations.Serving?.Id);
        Assert.Equal(result.ProfileId, generations.Building?.Id);
    }

    /// <summary>The index is built while the generation is empty, which is the cheapest moment it can be built.</summary>
    [Fact]
    public async Task ActivateAsync_ANewGeometry_BuildsTheApproximateIndexForTheGenerationItRegistered()
    {
        // Arrange
        var world = CreateWorld();

        // Act
        var result = await world.Activation.ActivateAsync(
            CreateIdentity("a-model"),
            TestContext.Current.CancellationToken);

        // Assert
        await world.VectorIndex.Received(1).EnsureBuiltAsync(
            Arg.Is<RegisteredEmbeddingProfile>(profile => profile!.Id == result.ProfileId),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// An instance with nothing serving builds rather than serving immediately, so the moment searches start being
    /// answered semantically is the same single transition every later model change produces.
    /// </summary>
    [Fact]
    public async Task ActivateAsync_NothingServingYet_StillBuildsRatherThanServingAPartialGeneration()
    {
        // Arrange
        var world = CreateWorld();

        // Act
        var result = await world.Activation.ActivateAsync(
            CreateIdentity("a-model"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmbeddingProfileActivationOutcome.ReindexStarted, result.Outcome);

        var generations = await world.GenerationStore.ReadGenerationsAsync(TestContext.Current.CancellationToken);
        Assert.Null(generations.Serving);
        Assert.Equal(result.ProfileId, generations.Building?.Id);
    }

    /// <summary>Activating what is already serving spends nothing, which is what makes the command safe to repeat.</summary>
    [Fact]
    public async Task ActivateAsync_TheGeometryAlreadyServing_ReportsItAndRegistersNothing()
    {
        // Arrange
        var world = CreateWorld();
        var identity = CreateIdentity("a-model");
        var serving = world.GenerationStore.Add(identity, EmbeddingProfileLifecycleState.Active);

        // Act
        var result = await world.Activation.ActivateAsync(identity, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmbeddingProfileActivationOutcome.AlreadyServing, result.Outcome);
        Assert.Equal(serving.Id, result.ProfileId);

        var generations = await world.GenerationStore.ReadGenerationsAsync(TestContext.Current.CancellationToken);
        Assert.Null(generations.Building);
    }

    /// <summary>
    /// Repeating the command against the generation already being built is what an operator does after an index build
    /// failed, so it re-ensures the index rather than reporting the reindex and doing nothing.
    /// </summary>
    [Fact]
    public async Task ActivateAsync_TheGeometryAlreadyBeingBuilt_LeavesTheReindexRunningAndReEnsuresItsIndex()
    {
        // Arrange
        var world = CreateWorld();
        var identity = CreateIdentity("a-model");
        var building = world.GenerationStore.Add(identity, EmbeddingProfileLifecycleState.Building);

        // Act
        var result = await world.Activation.ActivateAsync(identity, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmbeddingProfileActivationOutcome.AlreadyBuilding, result.Outcome);
        Assert.Equal(building.Id, result.ProfileId);
        await world.VectorIndex.Received(1).EnsureBuiltAsync(
            Arg.Is<RegisteredEmbeddingProfile>(profile => profile!.Id == building.Id),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The row this registers is the whole signal the backfill worker has, and it is asleep on a pause chosen by a pass
    /// that found nothing to embed. Without the request, the first passages of the reindex go out whenever that
    /// unrelated interval happens to expire rather than because an operator asked for them.
    /// </summary>
    [Fact]
    public async Task ActivateAsync_ANewGeometry_AsksForTheNextBackfillPassNow()
    {
        // Arrange
        var world = CreateWorld();

        // Act
        await world.Activation.ActivateAsync(CreateIdentity("a-model"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(Now, world.BackfillSchedule.NextPassDueAt);
    }

    /// <summary>
    /// Repeating the command against a reindex already running is the repair path for a failed index build, and it is
    /// also what an operator reaches for when a sweep that completed left messages a refused call had stepped past. The
    /// pass that reaches them is the one the long interval has just put a quarter of an hour away.
    /// </summary>
    [Fact]
    public async Task ActivateAsync_TheGeometryAlreadyBeingBuilt_AsksForTheNextBackfillPassNow()
    {
        // Arrange
        var world = CreateWorld();
        var identity = CreateIdentity("a-model");
        world.GenerationStore.Add(identity, EmbeddingProfileLifecycleState.Building);

        // Act
        await world.Activation.ActivateAsync(identity, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(Now, world.BackfillSchedule.NextPassDueAt);
    }

    /// <summary>An activation that changed no row created no work, so it does not spend a pass asking about none.</summary>
    [Fact]
    public async Task ActivateAsync_TheGeometryAlreadyServing_LeavesTheBackfillPaceAlone()
    {
        // Arrange
        var world = CreateWorld();
        var identity = CreateIdentity("a-model");
        world.GenerationStore.Add(identity, EmbeddingProfileLifecycleState.Active);

        // Act
        await world.Activation.ActivateAsync(identity, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(world.BackfillSchedule.NextPassDueAt);
    }

    /// <summary>
    /// Two generations being built at once would leave one walk between two partial generations and neither ever
    /// reaching the count that completes it, so the second activation is refused rather than started beside the first.
    /// </summary>
    [Fact]
    public async Task ActivateAsync_ADifferentReindexAlreadyRunning_RefusesAndNamesTheGenerationInTheWay()
    {
        // Arrange
        var world = CreateWorld();
        var building = world.GenerationStore.Add(
            CreateIdentity("a-model-being-built"),
            EmbeddingProfileLifecycleState.Building);

        // Act
        var result = await world.Activation.ActivateAsync(
            CreateIdentity("yet-another-model"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmbeddingProfileActivationOutcome.DifferentReindexRunning, result.Outcome);
        Assert.Equal(building.Id, result.ProfileId);
        await world.VectorIndex.DidNotReceiveWithAnyArgs().EnsureBuiltAsync(null!, TestContext.Current.CancellationToken);
        Assert.Null(world.BackfillSchedule.NextPassDueAt);

        var generations = await world.GenerationStore.ReadGenerationsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(building.Id, generations.Building?.Id);
    }

    /// <summary>
    /// Rolling back is activating a previous model again, and it resolves to the row that model already has: its
    /// identity may never move, so a second row would be a second generation of one geometry.
    /// </summary>
    [Fact]
    public async Task ActivateAsync_AGeometryThatWasSuperseded_ResolvesToItsExistingRowRatherThanRegisteringASecond()
    {
        // Arrange
        var world = CreateWorld();
        var identity = CreateIdentity("the-model-we-came-from");
        var superseded = world.GenerationStore.Add(identity, EmbeddingProfileLifecycleState.Superseded);
        world.GenerationStore.Add(CreateIdentity("the-model-that-replaced-it"), EmbeddingProfileLifecycleState.Active);

        // Act
        var result = await world.Activation.ActivateAsync(identity, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmbeddingProfileActivationOutcome.ReindexStarted, result.Outcome);
        Assert.Equal(superseded.Id, result.ProfileId);
        Assert.Equal(EmbeddingProfileLifecycleState.Building, world.GenerationStore.StateOf(superseded.Id));
    }

    /// <summary>
    /// The check for a running reindex and the registration are not one act, so two activations of different
    /// geometries can both find nothing being built. The database refuses the second, and no retry resolves that —
    /// what the operator needs is not that two commands collided but which generation is now being built.
    /// </summary>
    [Fact]
    public async Task ActivateAsync_AnotherActivationWonTheRegistrationRace_ReportsTheReindexThatWon()
    {
        // Arrange
        var winner = new RegisteredEmbeddingProfile(
            EmbeddingProfileId.Create(Guid.CreateVersion7()),
            CreateIdentity("the-model-that-won"));
        var activation = CreateActivation(
            RacedRegistrationStore(buildingAfterTheRace: winner),
            Substitute.For<IEmbeddingProfileVectorIndex>(),
            new EmbeddingBackfillSchedule(new FakeTimeProvider(Now)));

        // Act
        var result = await activation.ActivateAsync(
            CreateIdentity("the-model-that-lost"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmbeddingProfileActivationOutcome.DifferentReindexRunning, result.Outcome);
        Assert.Equal(winner.Id, result.ProfileId);
    }

    /// <summary>
    /// A conflict with nothing being built afterwards was some other competing writer, and reporting it as a reindex
    /// in the way would tell an operator to cancel something that does not exist.
    /// </summary>
    [Fact]
    public async Task ActivateAsync_AConflictThatWasNotAnotherActivation_RaisesItRatherThanInventingAnOutcome()
    {
        // Arrange
        var activation = CreateActivation(
            RacedRegistrationStore(buildingAfterTheRace: null),
            Substitute.For<IEmbeddingProfileVectorIndex>(),
            new EmbeddingBackfillSchedule(new FakeTimeProvider(Now)));

        // Act
        var conflict = await Assert.ThrowsAsync<PersistenceConcurrencyConflictException>(
            () => activation.ActivateAsync(CreateIdentity("a-model"), TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("optimistic concurrency", conflict.Message, StringComparison.Ordinal);
    }

    /// <summary>A store whose registration always loses the race, and which answers afterwards with what won it.</summary>
    private static IEmbeddingGenerationStore RacedRegistrationStore(RegisteredEmbeddingProfile? buildingAfterTheRace)
    {
        var generationStore = Substitute.For<IEmbeddingGenerationStore>();

        // Substituted rather than hand-written, because what this arranges is the database refusing a write, which the
        // in-memory store has no way to do: it is the port's failure mode rather than any state it holds.
        generationStore.ReadGenerationsAsync(Arg.Any<CancellationToken>())
            .Returns(
                _ => EmbeddingGenerations.None,
                _ => new EmbeddingGenerations(Serving: null, buildingAfterTheRace));
        generationStore.RegisterBuildingAsync(
                Arg.Any<IPersistenceSession>(),
                Arg.Any<EmbeddingProfileIdentity>(),
                Arg.Any<CancellationToken>())
            .Returns<RegisteredEmbeddingProfile>(_ => throw new PersistenceConcurrencyConflictException(
                "A local write did not commit within the configured optimistic concurrency attempts."));

        return generationStore;
    }

    private static EmbeddingProfileIdentity CreateIdentity(string modelIdentifier) =>
        EmbeddingProfileIdentity.Create(
            "a-provider",
            modelIdentifier,
            modelVersion: null,
            dimension: 8,
            EmbeddingDistanceMetric.Cosine,
            EmbeddingInputPreparation.Create(2_000, passageInstruction: null, normalizesVector: true));

    private static ActivationWorld CreateWorld()
    {
        var generationStore = new InMemoryEmbeddingGenerationStore(new InMemoryEmailEmbeddingStore());
        var vectorIndex = Substitute.For<IEmbeddingProfileVectorIndex>();
        var backfillSchedule = new EmbeddingBackfillSchedule(new FakeTimeProvider(Now));

        return new ActivationWorld(
            generationStore,
            vectorIndex,
            backfillSchedule,
            CreateActivation(generationStore, vectorIndex, backfillSchedule));
    }

    private static EmbeddingProfileActivation CreateActivation(
        IEmbeddingGenerationStore generationStore,
        IEmbeddingProfileVectorIndex vectorIndex,
        EmbeddingBackfillSchedule backfillSchedule)
    {
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Substitute.For<IPersistenceSession>());

        return new EmbeddingProfileActivation(
            generationStore,
            vectorIndex,
            new OptimisticConcurrencyRetryPolicy(
                sessionFactory,
                new PersistenceConcurrencyOptions(),
                new FakeTimeProvider()),
            backfillSchedule);
    }

    /// <summary>The generations and the collaborators one activation works against.</summary>
    private sealed record ActivationWorld(
        InMemoryEmbeddingGenerationStore GenerationStore,
        IEmbeddingProfileVectorIndex VectorIndex,
        EmbeddingBackfillSchedule BackfillSchedule,
        EmbeddingProfileActivation Activation);
}
