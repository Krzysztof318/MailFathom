// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Embeddings.Backfill;
using MailFathom.Application.Emails.Embeddings.Generations;
using MailFathom.Application.Emails.Embeddings.Indexing;
using MailFathom.Application.Emails.Embeddings.Limits;
using MailFathom.Application.Emails.Embeddings.Vectorization;
using MailFathom.Application.Persistence;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Embeddings.Generations;

public sealed class EmbeddingGenerationUpkeepTests
{
    /// <summary>
    /// The generation being built is what the sweep works towards, and the one serving searches keeps every vector it
    /// had while that happens. Two generations coexisting in one table is the whole point of a reindex without an
    /// outage.
    /// </summary>
    [Fact]
    public async Task RunAsync_AGenerationBeingBuilt_FillsItWhileTheServingGenerationKeepsItsOwnVectors()
    {
        // A pass bounded below what the mailbox holds, so the reindex is observed part-way through rather than
        // completing and switching in the same pass — which is the state a reindex of a real mailbox is in for hours.
        var world = CreateWorld(batchSize: 1, maxBatchesPerRun: 1);
        var serving = await world.ServeAGenerationWithVectorsAsync(messageCount: 3);
        var building = world.GenerationStore.Add(
            CreateIdentity("a-model"),
            EmbeddingProfileLifecycleState.Building);

        // Act
        var result = await world.Upkeep.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmbeddingGenerationTransition.None, result.Transition);
        Assert.Equal(1, world.EmbeddingStore.CountVectors(building.Id));
        Assert.Equal(3, world.EmbeddingStore.CountVectors(serving.Id));
        Assert.Equal(EmbeddingProfileLifecycleState.Active, world.GenerationStore.StateOf(serving.Id));
    }

    /// <summary>The switch is one transition, and it is what makes the new generation the one retrieval reads.</summary>
    [Fact]
    public async Task RunAsync_TheGenerationBeingBuiltIsComplete_SwitchesToItAndSupersedesTheOneItReplaces()
    {
        // Arrange
        var world = CreateWorld();
        var serving = await world.ServeAGenerationWithVectorsAsync(messageCount: 2);
        var building = world.GenerationStore.Add(
            CreateIdentity("a-model"),
            EmbeddingProfileLifecycleState.Building);

        // Act
        var result = await world.Upkeep.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmbeddingGenerationTransition.Switched, result.Transition);
        Assert.Equal(EmbeddingProfileLifecycleState.Active, world.GenerationStore.StateOf(building.Id));
        Assert.Equal(EmbeddingProfileLifecycleState.Superseded, world.GenerationStore.StateOf(serving.Id));

        var generations = await world.GenerationStore.ReadGenerationsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(building.Id, generations.Serving?.Id);
        Assert.Null(generations.Building);
    }

    /// <summary>
    /// A completed sweep is not a complete generation. The walk ends its sweep when nothing is outstanding in front of
    /// its position, and a message a provider refused stays outstanding behind it — switching there would promote a
    /// generation that is missing mail, which no later pass would notice because it is retrievable and simply answers
    /// worse.
    /// </summary>
    [Fact]
    public async Task RunAsync_ASweepCompletedWithAMessageStillOutstanding_DoesNotSwitch()
    {
        // Arrange
        var world = CreateWorld();
        var serving = await world.ServeAGenerationWithVectorsAsync(messageCount: 3);
        var building = world.GenerationStore.Add(
            CreateIdentity("a-model"),
            EmbeddingProfileLifecycleState.Building);
        world.TextEmbeddingGenerator.Failure = EmbeddingGenerationFailure.RateLimited;
        world.TextEmbeddingGenerator.FailingCallNumber = world.TextEmbeddingGenerator.RequestedBatches.Count + 2;

        // Act
        var refusedPass = await world.Upkeep.RunAsync(TestContext.Current.CancellationToken);
        var completingPass = await world.Upkeep.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredEmailEmbeddingBackfillOutcome.ProviderFailed, refusedPass.Sweep.Outcome);
        Assert.Equal(StoredEmailEmbeddingBackfillOutcome.SweepCompleted, completingPass.Sweep.Outcome);
        Assert.Equal(EmbeddingGenerationTransition.None, completingPass.Transition);
        Assert.Equal(EmbeddingProfileLifecycleState.Building, world.GenerationStore.StateOf(building.Id));
        Assert.Equal(EmbeddingProfileLifecycleState.Active, world.GenerationStore.StateOf(serving.Id));
    }

    /// <summary>
    /// A cancellation that lands after the pass counted the generation complete must not be overtaken by the switch it
    /// was cancelling. Superseding the generation that is serving and then failing to promote the one that was
    /// abandoned would leave the instance with nothing to answer a search from, and nothing later would put it back.
    /// </summary>
    [Fact]
    public async Task RunAsync_TheGenerationBeingBuiltIsAbandonedAsTheSwitchCommits_LeavesTheServingGenerationServing()
    {
        // Arrange
        var world = CreateWorld();
        var serving = await world.ServeAGenerationWithVectorsAsync(messageCount: 2);
        var building = world.GenerationStore.Add(
            CreateIdentity("a-model"),
            EmbeddingProfileLifecycleState.Building);
        world.GenerationStore.AbandonWhenSwitched = building.Id;

        // Act
        var result = await world.Upkeep.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmbeddingGenerationTransition.None, result.Transition);
        Assert.Equal(EmbeddingProfileLifecycleState.Active, world.GenerationStore.StateOf(serving.Id));
        Assert.Equal(EmbeddingProfileLifecycleState.Superseded, world.GenerationStore.StateOf(building.Id));

        var generations = await world.GenerationStore.ReadGenerationsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(serving.Id, generations.Serving?.Id);
    }

    /// <summary>
    /// The superseded generation's vectors go in bounded batches after the switch, and the generation now serving keeps
    /// every one of its own. They are removed rather than kept for a rollback window because they are personal data
    /// whose purpose ended at the switch.
    /// </summary>
    [Fact]
    public async Task RunAsync_AfterASwitch_RemovesTheSupersededVectorsAndLeavesTheServingOnesAlone()
    {
        // Arrange
        var world = CreateWorld();
        var superseded = await world.ServeAGenerationWithVectorsAsync(messageCount: 2);
        var building = world.GenerationStore.Add(
            CreateIdentity("a-model"),
            EmbeddingProfileLifecycleState.Building);

        // Act
        var result = await world.Upkeep.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, result.RemovedSupersededVectorCount);
        Assert.Equal(0, world.EmbeddingStore.CountVectors(superseded.Id));
        Assert.Equal(2, world.EmbeddingStore.CountVectors(building.Id));

        // Bounded rather than one statement, which is what keeps a generation of a large mailbox from being deleted in
        // a single transaction holding one lock set and one write-ahead burst for as long as it takes.
        Assert.All(world.GenerationStore.RequestedRemovalBatchSizes, batchSize => Assert.True(batchSize > 0));
    }

    /// <summary>
    /// The index of a generation nothing reads goes when its last vectors do, which is also what clears one left behind
    /// by a process that stopped part-way through a removal.
    /// </summary>
    [Fact]
    public async Task RunAsync_TheLastVectorsOfASupersededGeneration_RemovesItsApproximateIndexToo()
    {
        // Arrange
        var world = CreateWorld();
        var superseded = await world.ServeAGenerationWithVectorsAsync(messageCount: 1);
        world.GenerationStore.Add(CreateIdentity("a-model"), EmbeddingProfileLifecycleState.Building);

        // Act
        await world.Upkeep.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        await world.VectorIndex.Received().RemoveAsync(superseded.Id, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A reindex cancelled on an instance that had never served a generation leaves one superseded row and no sibling,
    /// so there is nothing to sweep towards and still something to clear out. The vectors it accumulated are personal
    /// data derived from mail, and a pass that walked away from them because it had no target would keep them forever.
    /// </summary>
    [Fact]
    public async Task RunAsync_ASupersededGenerationAndNothingElse_StillRemovesItsVectors()
    {
        // Arrange
        var world = CreateWorld();
        var abandoned = await world.ServeAGenerationWithVectorsAsync(messageCount: 2);
        world.GenerationStore.Supersede(abandoned.Id);

        // Act
        var result = await world.Upkeep.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredEmailEmbeddingBackfillOutcome.NoActiveProfile, result.Sweep.Outcome);
        Assert.Equal(2, result.RemovedSupersededVectorCount);
        Assert.Equal(0, world.EmbeddingStore.CountVectors(abandoned.Id));
    }

    /// <summary>
    /// Rolling back catches its own removal part-way through: a generation activated again stops being superseded, and
    /// the vectors it still holds are what makes that rollback cheaper than a full re-embed. A delete decided before
    /// the reactivation must not go through on the strength of that earlier decision.
    /// </summary>
    [Fact]
    public async Task RunAsync_AGenerationActivatedAgainWhileItsRemovalWasPending_KeepsTheVectorsItStillHolds()
    {
        // Arrange
        var world = CreateWorld();
        var reactivated = await world.ServeAGenerationWithVectorsAsync(messageCount: 2);
        world.GenerationStore.Supersede(reactivated.Id);
        await world.GenerationStore.RegisterBuildingAsync(
            Substitute.For<IPersistenceSession>(),
            CreateIdentity("a-model"),
            TestContext.Current.CancellationToken);

        // Act
        var result = await world.Upkeep.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, result.RemovedSupersededVectorCount);
        Assert.Equal(2, world.EmbeddingStore.CountVectors(reactivated.Id));

        // The vectors that survived are what makes this rollback cheap: nothing was outstanding, so the generation was
        // complete the moment it was registered again and the pass switched straight back to it.
        Assert.Equal(EmbeddingProfileLifecycleState.Active, world.GenerationStore.StateOf(reactivated.Id));
    }

    /// <summary>An instance that has registered no generation has nothing to work towards, so nothing is spent.</summary>
    [Fact]
    public async Task RunAsync_NoGenerationRegistered_ReadsNoMailAndReachesNoProvider()
    {
        // Arrange
        var world = CreateWorld();
        world.AddMail(messageCount: 3);

        // Act
        var result = await world.Upkeep.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredEmailEmbeddingBackfillOutcome.NoActiveProfile, result.Sweep.Outcome);
        Assert.Equal(EmbeddingGenerationTransition.None, result.Transition);
        Assert.False(result.MoreWorkIsWorthTryingSoon);
        Assert.Empty(world.TextEmbeddingGenerator.RequestedBatches);
        Assert.Empty(world.BackfillStore.SavedPositions);
    }

    /// <summary>
    /// With nothing being built the sweep works towards the generation serving searches, which is the ordinary backfill
    /// of mail the live path never reached.
    /// </summary>
    [Fact]
    public async Task RunAsync_NothingBeingBuilt_SweepsTowardsTheServingGeneration()
    {
        // Arrange
        var world = CreateWorld();
        var serving = world.GenerationStore.Add(CreateIdentity("a-model"), EmbeddingProfileLifecycleState.Active);
        world.AddMail(messageCount: 2);

        // Act
        var result = await world.Upkeep.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, result.Sweep.EmbeddedEmailCount);
        Assert.Equal(EmbeddingGenerationTransition.None, result.Transition);
        Assert.Equal(2, world.EmbeddingStore.CountVectors(serving.Id));
    }

    private static EmbeddingProfileIdentity CreateIdentity(string modelIdentifier) =>
        EmbeddingProfileIdentity.Create(
            "a-provider",
            modelIdentifier,
            modelVersion: null,
            dimension: 8,
            EmbeddingDistanceMetric.Cosine,
            EmbeddingInputPreparation.Create(2_000, passageInstruction: null, normalizesVector: true));

    private static UpkeepWorld CreateWorld(int batchSize = 50, int maxBatchesPerRun = 10)
    {
        var embeddingStore = new InMemoryEmailEmbeddingStore();
        var backfillStore = new InMemoryStoredEmailEmbeddingBackfillStore(embeddingStore);
        var generationStore = new InMemoryEmbeddingGenerationStore(embeddingStore);

        // One geometry for every generation in these tests, because the generator refuses to write into a profile whose
        // fingerprint is not its own and what is under test here is the lifecycle rather than the geometry check.
        var textEmbeddingGenerator = new ScriptedTextEmbeddingGenerator(
            CreateIdentity("a-model"),
            maximumPassagesPerCall: 8);
        var vectorIndex = Substitute.For<IEmbeddingProfileVectorIndex>();

        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Substitute.For<IPersistenceSession>());

        var concurrencyRetryPolicy = new OptimisticConcurrencyRetryPolicy(
            sessionFactory,
            new PersistenceConcurrencyOptions(),
            new FakeTimeProvider());

        var upkeep = new EmbeddingGenerationUpkeep(
            generationStore,
            backfillStore,
            new StoredEmailEmbeddingBackfill(
                backfillStore,
                new StoredEmailEmbeddingGenerator(
                    embeddingStore,
                    textEmbeddingGenerator,
                    concurrencyRetryPolicy,
                    new EmbeddingSpendGate(
                        new InMemoryEmbeddingSpendLedger(),
                        EmbeddingSpendBudget.Unbounded,
                        new FakeTimeProvider()),
                    EmbeddingRequestPacer.Create(maxRequestsPerMinute: 0, new FakeTimeProvider())),
                concurrencyRetryPolicy,
                new RecordingDerivedWorkGateTelemetry(),
                new StoredEmailEmbeddingBackfillOptions
                {
                    BatchSize = batchSize,
                    MaxBatchesPerRun = maxBatchesPerRun,
                }),
            vectorIndex,
            concurrencyRetryPolicy);

        return new UpkeepWorld(
            embeddingStore,
            backfillStore,
            generationStore,
            textEmbeddingGenerator,
            vectorIndex,
            upkeep);
    }

    /// <summary>The mail, the generations, and the collaborators one upkeep pass works against.</summary>
    private sealed record UpkeepWorld(
        InMemoryEmailEmbeddingStore EmbeddingStore,
        InMemoryStoredEmailEmbeddingBackfillStore BackfillStore,
        InMemoryEmbeddingGenerationStore GenerationStore,
        ScriptedTextEmbeddingGenerator TextEmbeddingGenerator,
        IEmbeddingProfileVectorIndex VectorIndex,
        EmbeddingGenerationUpkeep Upkeep)
    {
        /// <summary>Adds mail that has its passages and no vector under any generation.</summary>
        public void AddMail(int messageCount)
        {
            // A loop rather than a projection, because registering a message with the store is a side effect.
            foreach (var _ in Enumerable.Range(0, messageCount))
            {
                this.BackfillStore.AddEmailAwaitingEmbedding(
                    StoredEmailId.Create(Guid.CreateVersion7()),
                    passageCount: 1);
            }
        }

        /// <summary>Brings the world to where an instance that has been embedding for a while already is.</summary>
        /// <remarks>
        /// The vectors come from real passes rather than being placed by hand, so what a later pass finds is what the
        /// production path would actually have left there. It runs until the sweep has nothing left, because a bounded
        /// pass reaches only part of the mail and an arrangement that stopped there would leave the generation the test
        /// calls serving with a hole in it.
        /// </remarks>
        public async Task<RegisteredEmbeddingProfile> ServeAGenerationWithVectorsAsync(int messageCount)
        {
            var serving = this.GenerationStore.Add(
                CreateIdentity("a-model"),
                EmbeddingProfileLifecycleState.Active);

            this.AddMail(messageCount);

            // Bounded rather than "until it says so", so an arrangement that stops making progress fails the test it is
            // arranging instead of hanging it.
            for (var pass = 0; pass < messageCount + 1; pass++)
            {
                var result = await this.Upkeep.RunAsync(TestContext.Current.CancellationToken);
                if (result.Sweep.Outcome == StoredEmailEmbeddingBackfillOutcome.SweepCompleted)
                {
                    return serving;
                }
            }

            Assert.Fail("The arrangement did not finish embedding the mail it stored.");

            return serving;
        }
    }
}
