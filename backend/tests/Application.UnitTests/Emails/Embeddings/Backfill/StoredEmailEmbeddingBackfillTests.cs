// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Embeddings.Backfill;
using MailFathom.Application.Emails.Embeddings.Limits;
using MailFathom.Application.Emails.Embeddings.Vectorization;
using MailFathom.Application.Persistence;
using MailFathom.Application.Spam.Gating;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Embeddings.Backfill;

public sealed class StoredEmailEmbeddingBackfillTests
{
    private static readonly EmbeddingProfileId ProfileId = EmbeddingProfileId.Create(Guid.CreateVersion7());

    /// <summary>A moment the daily period places on a whole day, so an expected roll-over is arithmetic rather than a guess.</summary>
    private static readonly DateTimeOffset PeriodStart = new(2026, 8, 8, 0, 0, 0, TimeSpan.Zero);

    /// <summary>A message stored before chunking existed is cut into passages before anything is asked of a provider.</summary>
    [Fact]
    public async Task RunAsync_MessageWithNoPassages_CutsItBeforeItAsksForVectors()
    {
        // Arrange
        var world = CreateWorld();
        var message = NextEmail();
        world.BackfillStore.AddEmailAwaitingChunking(message, passageCount: 3);
        var backfill = world.CreateBackfill();

        // Act
        var result = await backfill.RunAsync(world.Target, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredEmailEmbeddingBackfillOutcome.SweepCompleted, result.Outcome);
        Assert.Equal(1, result.ChunkedEmailCount);
        Assert.Equal(1, result.EmbeddedEmailCount);
        Assert.Equal(3, result.EmbeddedChunkCount);
        Assert.Equal([message], world.BackfillStore.ChunkedEmails);

        // The passages the cut produced are the ones the provider was asked about, which is the ordering the backfill
        // exists to guarantee: a message with neither becomes a message with both, in that order.
        Assert.Equal(3, world.EmbeddingStore.EmbeddedPassages.Count);
    }

    /// <summary>The sweep is where a message classification never decided about is let through, so it says which answer let it.</summary>
    /// <remarks>
    /// It is the only place the release is decidable one message at a time: the walk below it is narrowed by a
    /// set-based predicate, which admits a batch without saying what admitted each row. Without this count a wedged
    /// classifier and a mailbox with nothing to embed publish the same silence.
    /// </remarks>
    [Theory]
    [InlineData(DerivedWorkAdmission.ReleasedAfterWaiting)]
    [InlineData(DerivedWorkAdmission.ReleasedAsUnclassifiable)]
    [InlineData(DerivedWorkAdmission.Admitted)]
    public async Task RunAsync_MessageTheGateReleased_RecordsWhichAnswerReleasedIt(DerivedWorkAdmission admission)
    {
        // Arrange
        var world = CreateWorld();
        world.BackfillStore.AddEmailAwaitingChunking(NextEmail(), passageCount: 2, admission);
        var backfill = world.CreateBackfill();

        // Act
        await backfill.RunAsync(world.Target, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([admission], world.GateTelemetry.Admissions);
    }

    /// <summary>A message whose passages already exist is ordinary outstanding work and says nothing about the gate.</summary>
    [Fact]
    public async Task RunAsync_MessageThatAlreadyHasPassages_RecordsNoAdmission()
    {
        // Arrange
        var world = CreateWorld();
        world.BackfillStore.AddEmailAwaitingEmbedding(NextEmail(), passageCount: 2);
        var backfill = world.CreateBackfill();

        // Act
        await backfill.RunAsync(world.Target, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(world.GateTelemetry.Admissions);
    }

    /// <summary>A message that already has its passages is embedded without being cut a second time.</summary>
    [Fact]
    public async Task RunAsync_MessageThatAlreadyHasPassages_IsEmbeddedWithoutBeingCutAgain()
    {
        // Arrange
        var world = CreateWorld();
        world.BackfillStore.AddEmailAwaitingEmbedding(NextEmail(), passageCount: 2);
        var backfill = world.CreateBackfill();

        // Act
        var result = await backfill.RunAsync(world.Target, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, result.ChunkedEmailCount);
        Assert.Equal(1, result.EmbeddedEmailCount);
        Assert.Equal(2, result.EmbeddedChunkCount);
        Assert.Empty(world.BackfillStore.ChunkedEmails);
    }

    /// <summary>A run is bounded by its batch budget and says that work remains rather than running the mailbox down.</summary>
    [Fact]
    public async Task RunAsync_MoreMessagesThanTheBatchBudgetCovers_StopsAndReportsRemainingWork()
    {
        // Arrange
        var world = CreateWorld();
        AddMessagesAwaitingEmbedding(world, count: 20, passagesEach: 1);
        var backfill = world.CreateBackfill(batchSize: 3, maxBatchesPerRun: 2);

        // Act
        var result = await backfill.RunAsync(world.Target, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredEmailEmbeddingBackfillOutcome.BatchBudgetSpent, result.Outcome);
        Assert.True(result.MoreWorkIsWorthTryingSoon);
        Assert.Equal(6, result.EmbeddedEmailCount);
        Assert.Equal(2, world.BackfillStore.RequestedResumePositions.Count);
    }

    /// <summary>The position each message commits is what the next run continues past, so nothing is re-read or skipped.</summary>
    [Fact]
    public async Task RunAsync_InterruptedRun_ResumesFromThePersistedPosition()
    {
        // Arrange
        var world = CreateWorld();
        var messages = AddMessagesAwaitingEmbedding(world, count: 9, passagesEach: 1);
        var backfill = world.CreateBackfill(batchSize: 3, maxBatchesPerRun: 1);

        // Act
        var firstRun = await backfill.RunAsync(world.Target, TestContext.Current.CancellationToken);
        var positionAfterFirstRun = world.BackfillStore.SavedPositions[^1];
        var secondRun = await backfill.RunAsync(world.Target, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(firstRun.MoreWorkIsWorthTryingSoon);
        Assert.Equal(messages[2], positionAfterFirstRun);
        Assert.Equal(3, secondRun.EmbeddedEmailCount);
        Assert.Equal(messages[2], world.BackfillStore.RequestedResumePositions[1]);
        Assert.Equal(messages[5], world.BackfillStore.SavedPositions[^1]);
    }

    /// <summary>What is outstanding is the absence of a vector, so a second sweep over current mail costs no provider call.</summary>
    [Fact]
    public async Task RunAsync_MailAlreadyCurrent_ReEmbedsNothing()
    {
        // Arrange
        var world = CreateWorld();
        AddMessagesAwaitingEmbedding(world, count: 4, passagesEach: 2);
        var backfill = world.CreateBackfill();

        // Act
        var firstRun = await backfill.RunAsync(world.Target, TestContext.Current.CancellationToken);
        var callsAfterFirstRun = world.TextEmbeddingGenerator.RequestedBatches.Count;
        var secondRun = await backfill.RunAsync(world.Target, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(8, firstRun.EmbeddedChunkCount);
        Assert.Equal(0, secondRun.EmbeddedEmailCount);
        Assert.Equal(0, secondRun.EmbeddedChunkCount);
        Assert.Equal(callsAfterFirstRun, world.TextEmbeddingGenerator.RequestedBatches.Count);
    }

    /// <summary>Reaching the end ends the sweep, so the next one starts again and reaches what a failed turn left behind.</summary>
    [Fact]
    public async Task RunAsync_WalkReachesTheEnd_EndsTheSweepSoTheNextOneStartsFromTheBeginning()
    {
        // Arrange
        var world = CreateWorld();
        AddMessagesAwaitingEmbedding(world, count: 2, passagesEach: 1);
        var backfill = world.CreateBackfill();

        // Act
        var result = await backfill.RunAsync(world.Target, TestContext.Current.CancellationToken);
        var resumePosition = await world.BackfillStore.FindResumePositionAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredEmailEmbeddingBackfillOutcome.SweepCompleted, result.Outcome);
        Assert.False(result.MoreWorkIsWorthTryingSoon);
        Assert.Null(world.BackfillStore.SavedPositions[^1]);
        Assert.Null(resumePosition);
    }

    /// <summary>
    /// A declaration that disagrees with what was activated says nothing about the message in hand, so the position
    /// stays where it was and the next run offers that same message rather than skipping it.
    /// </summary>
    [Fact]
    public async Task RunAsync_GeneratorDisagreesWithProfile_EndsTheRunWithoutAdvancingThePosition()
    {
        // Arrange
        var world = CreateWorld(generatorModelIdentifier: "a-model-nobody-activated");
        AddMessagesAwaitingEmbedding(world, count: 3, passagesEach: 1);
        var backfill = world.CreateBackfill();

        // Act
        var result = await backfill.RunAsync(world.Target, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredEmailEmbeddingBackfillOutcome.GeneratorDisagreesWithProfile, result.Outcome);
        Assert.False(result.MoreWorkIsWorthTryingSoon);
        Assert.Equal(0, result.EmbeddedEmailCount);
        Assert.Empty(world.BackfillStore.SavedPositions);
        Assert.Empty(world.TextEmbeddingGenerator.RequestedBatches);
    }

    /// <summary>
    /// A reached spend ceiling ends the run without advancing the position, unlike a refused call: it says nothing
    /// about the message in hand, and the passages it did not reach are the ones a rolled-over period should pay for
    /// first. The run names the instant the ceiling lifts, which is what a worker waits for instead of an interval.
    /// </summary>
    [Fact]
    public async Task RunAsync_TheSpendCeilingIsReached_EndsTheRunWithoutAdvancingThePosition()
    {
        // Arrange
        var world = CreateWorld(
            spendBudget: EmbeddingSpendBudget.Create(maxInputCharactersPerPeriod: 1, 0, TimeSpan.FromDays(1)));
        AddMessagesAwaitingEmbedding(world, count: 3, passagesEach: 1);
        var backfill = world.CreateBackfill();

        // Act
        var result = await backfill.RunAsync(world.Target, TestContext.Current.CancellationToken);

        // Assert
        // The first message is paid for whole and the ceiling binds on the second, so the run stops having embedded one.
        Assert.Equal(StoredEmailEmbeddingBackfillOutcome.SpendCeilingReached, result.Outcome);
        Assert.False(result.MoreWorkIsWorthTryingSoon);
        Assert.Equal(PeriodStart.AddDays(1), result.SpendPeriodEndsAt);
        Assert.Equal(1, result.EmbeddedEmailCount);
        Assert.Single(world.BackfillStore.SavedPositions);
    }

    /// <summary>
    /// A refused call ends the run rather than the next message's turn, and the position steps past the message so
    /// nothing blocks the walk; what that turn did not reach is what the next sweep selects on.
    /// </summary>
    /// <remarks>
    /// The classification decides only whether asking again shortly is worth anything: a remote condition is waited out
    /// on the short interval, while a rejected credential, a refused request, and an unexpected vector shape would buy
    /// the same answer at the same price. Neither changes what happens to the message itself.
    /// </remarks>
    [Theory]
    [InlineData(EmbeddingGenerationFailure.RateLimited, true)]
    [InlineData(EmbeddingGenerationFailure.RequestTimedOut, true)]
    [InlineData(EmbeddingGenerationFailure.TransportFaulted, true)]
    [InlineData(EmbeddingGenerationFailure.CredentialRejected, false)]
    [InlineData(EmbeddingGenerationFailure.RequestRefused, false)]
    [InlineData(EmbeddingGenerationFailure.VectorShapeUnexpected, false)]
    public async Task RunAsync_ProviderRefusesACall_EndsTheRunAndStepsPastTheMessage(
        EmbeddingGenerationFailure failure,
        bool worthTryingSoon)
    {
        // Arrange
        var world = CreateWorld();
        var messages = AddMessagesAwaitingEmbedding(world, count: 3, passagesEach: 1);
        world.TextEmbeddingGenerator.Failure = failure;
        world.TextEmbeddingGenerator.FailingCallNumber = 2;
        var backfill = world.CreateBackfill();

        // Act
        var result = await backfill.RunAsync(world.Target, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredEmailEmbeddingBackfillOutcome.ProviderFailed, result.Outcome);
        Assert.Equal(failure, result.Failure);
        Assert.Equal(worthTryingSoon, result.MoreWorkIsWorthTryingSoon);
        Assert.Equal(1, result.EmbeddedEmailCount);
        Assert.Equal(messages[1], world.BackfillStore.SavedPositions[^1]);
        Assert.Equal(2, world.TextEmbeddingGenerator.RequestedBatches.Count);
    }

    /// <summary>
    /// A message needing more calls than one turn allows keeps the walk going, because it says something about that
    /// message's length and nothing about the provider — but it is counted, because the walk steps past it and a
    /// mailbox where several sweeps go by before one message finishes would otherwise look like one that is finishing
    /// them.
    /// </summary>
    [Fact]
    public async Task RunAsync_MessageNeedingMoreCallsThanOneTurnAllows_CountsItAndCarriesOn()
    {
        // Arrange
        const int callBudget = 512;
        var world = CreateWorld(maximumPassagesPerCall: 1);
        var longMessage = NextEmail();
        world.BackfillStore.AddEmailAwaitingEmbedding(longMessage, passageCount: callBudget + 3);
        var messageBehindIt = NextEmail();
        world.BackfillStore.AddEmailAwaitingEmbedding(messageBehindIt, passageCount: 1);
        var backfill = world.CreateBackfill();

        // Act
        var result = await backfill.RunAsync(world.Target, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, result.CallBudgetExhaustedEmailCount);

        // The message behind it was still reached, and the truncated one is not counted as brought up to date.
        Assert.Equal(1, result.EmbeddedEmailCount);
        Assert.Equal(callBudget + 1, result.EmbeddedChunkCount);
        Assert.Contains(messageBehindIt, world.BackfillStore.SavedPositions);
    }

    /// <summary>Cancellation ends the run at the next message and leaves the position it already committed durable.</summary>
    [Fact]
    public async Task RunAsync_CancelledAfterOneMessage_StopsThereAndLeavesThatPositionDurable()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        var world = CreateWorld();
        var messages = AddMessagesAwaitingEmbedding(world, count: 4, passagesEach: 1);
        var backfill = world.CreateBackfill();
        world.BackfillStore.CancelWhenPositionSaved = cancellation;

        // Act
        var cancelled = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => backfill.RunAsync(world.Target, cancellation.Token));

        // Assert
        Assert.Equal(cancellation.Token, cancelled.CancellationToken);

        // The first message was embedded and its position committed before the cancellation was observed, and the
        // second message was never asked of the provider — so the next run starts at it rather than paying twice.
        Assert.Single(world.TextEmbeddingGenerator.RequestedBatches);
        Assert.Equal(messages[0], Assert.Single(world.BackfillStore.SavedPositions));
    }

    /// <summary>A sweep reports the size of what it is about to work through; a run resuming one measures nothing again.</summary>
    [Fact]
    public async Task RunAsync_StartingASweep_ReportsHowManyMessagesAwaitEmbedding()
    {
        // Arrange
        var world = CreateWorld();
        AddMessagesAwaitingEmbedding(world, count: 5, passagesEach: 1);
        var backfill = world.CreateBackfill(batchSize: 2, maxBatchesPerRun: 1);

        // Act
        var firstRun = await backfill.RunAsync(world.Target, TestContext.Current.CancellationToken);
        var secondRun = await backfill.RunAsync(world.Target, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(5, firstRun.OutstandingEmailCountAtSweepStart);
        Assert.Null(secondRun.OutstandingEmailCountAtSweepStart);
    }

    private static StoredEmailId NextEmail() => StoredEmailId.Create(Guid.CreateVersion7());

    /// <summary>
    /// One owner reaching their share steps the walk past their mail and leaves every other owner's being embedded,
    /// which is the whole reason the per-owner ceiling does not end the run the way the deployment's does.
    /// </summary>
    /// <remarks>
    /// The walk visits messages in identifier order and owners interleave in it, so ending the sweep at the first
    /// owner-bound refusal would leave everybody else unembedded until the period rolled over — the harm bounding
    /// spend per owner exists to prevent.
    /// </remarks>
    [Fact]
    public async Task RunAsync_OneOwnerHasSpentTheirShare_KeepsEmbeddingEveryOtherOwnersMail()
    {
        // Arrange
        const long OwnerCeiling = 1_000;

        var ledger = new InMemoryEmbeddingSpendLedger();
        ledger.Seed(PeriodStart, SyntheticMailOwner.Another, OwnerCeiling);
        var ownership = new StubMailOwnership();
        var world = CreateWorld(
            spendBudget: EmbeddingSpendBudget.Create(
                maxInputCharactersPerPeriod: 1_000_000,
                maxInputCharactersPerPeriodPerOwner: OwnerCeiling,
                TimeSpan.FromDays(1)),
            ownership: ownership,
            spendLedger: ledger);

        // The two owners' mail interleaves in the order the walk visits it, which is what makes stepping past one of
        // them a different thing from ending the run.
        var messages = AddMessagesAwaitingEmbedding(world, count: 4, passagesEach: 1);
        ownership.Owns(messages[0], SyntheticMailOwner.Another);
        ownership.Owns(messages[2], SyntheticMailOwner.Another);
        var backfill = world.CreateBackfill();

        // Act
        var result = await backfill.RunAsync(world.Target, TestContext.Current.CancellationToken);

        // Assert
        // The spent owner's two messages are stepped past and the other owner's two are embedded between them, so the
        // sweep reaches the end of the mail rather than ending at the first refusal.
        Assert.Equal(StoredEmailEmbeddingBackfillOutcome.SweepCompleted, result.Outcome);
        Assert.Equal(EmbeddingSpendBound.None, result.ReachedSpendBound);
        Assert.Equal(2, result.EmbeddedEmailCount);
        Assert.Equal(2, result.EmbeddedChunkCount);
        Assert.Equal(2, result.OwnerSpendCeilingEmailCount);

        // The stepped-over messages keep their outstanding passages, which is what the next sweep selects on.
        Assert.Equal(
            2,
            await world.BackfillStore.CountEmailsAwaitingEmbeddingAsync(
                world.Target.Id,
                TestContext.Current.CancellationToken));
    }

    private static IReadOnlyList<StoredEmailId> AddMessagesAwaitingEmbedding(
        BackfillWorld world,
        int count,
        int passagesEach)
    {
        IReadOnlyList<StoredEmailId> messages = [.. Enumerable.Range(0, count).Select(_ => NextEmail())];

        // A loop rather than a projection, because registering a message with the store is a side effect.
        foreach (var message in messages)
        {
            world.BackfillStore.AddEmailAwaitingEmbedding(message, passagesEach);
        }

        return messages;
    }

    private static EmbeddingProfileIdentity CreateIdentity(string modelIdentifier) =>
        EmbeddingProfileIdentity.Create(
            "a-provider",
            modelIdentifier,
            modelVersion: null,
            dimension: 8,
            EmbeddingDistanceMetric.Cosine,
            EmbeddingInputPreparation.Create(2_000, passageInstruction: null, normalizesVector: true));

    private static BackfillWorld CreateWorld(
        string generatorModelIdentifier = "a-model",
        int maximumPassagesPerCall = 8,
        EmbeddingSpendBudget? spendBudget = null,
        IMailOwnership? ownership = null,
        InMemoryEmbeddingSpendLedger? spendLedger = null)
    {
        var embeddingStore = new InMemoryEmailEmbeddingStore();
        var backfillStore = new InMemoryStoredEmailEmbeddingBackfillStore(embeddingStore);
        var textEmbeddingGenerator = new ScriptedTextEmbeddingGenerator(
            CreateIdentity(generatorModelIdentifier),
            maximumPassagesPerCall);

        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Substitute.For<IPersistenceSession>());

        var concurrencyRetryPolicy = new OptimisticConcurrencyRetryPolicy(
            sessionFactory,
            new PersistenceConcurrencyOptions(),
            new FakeTimeProvider());

        return new BackfillWorld(
            new RegisteredEmbeddingProfile(ProfileId, CreateIdentity("a-model")),
            embeddingStore,
            backfillStore,
            textEmbeddingGenerator,
            new StoredEmailEmbeddingGenerator(
                embeddingStore,
                textEmbeddingGenerator,
                concurrencyRetryPolicy,
                new EmbeddingSpendGate(
                    spendLedger ?? new InMemoryEmbeddingSpendLedger(),
                    spendBudget ?? EmbeddingSpendBudget.Unbounded,
                    new FakeTimeProvider(PeriodStart)),
                EmbeddingRequestPacer.Create(maxRequestsPerMinute: 0, new FakeTimeProvider()),
                ownership ?? new StubMailOwnership()),
            concurrencyRetryPolicy,
            new RecordingDerivedWorkGateTelemetry());
    }

    /// <summary>The mail, the vectors, and the collaborators one backfill run works against.</summary>
    private sealed record BackfillWorld(
        RegisteredEmbeddingProfile Target,
        InMemoryEmailEmbeddingStore EmbeddingStore,
        InMemoryStoredEmailEmbeddingBackfillStore BackfillStore,
        ScriptedTextEmbeddingGenerator TextEmbeddingGenerator,
        StoredEmailEmbeddingGenerator EmbeddingGenerator,
        OptimisticConcurrencyRetryPolicy ConcurrencyRetryPolicy,
        RecordingDerivedWorkGateTelemetry GateTelemetry)
    {
        public StoredEmailEmbeddingBackfill CreateBackfill(int batchSize = 50, int maxBatchesPerRun = 10) => new(
            this.BackfillStore,
            this.EmbeddingGenerator,
            this.ConcurrencyRetryPolicy,
            this.GateTelemetry,
            new StoredEmailEmbeddingBackfillOptions
            {
                BatchSize = batchSize,
                MaxBatchesPerRun = maxBatchesPerRun,
            });
    }
}
