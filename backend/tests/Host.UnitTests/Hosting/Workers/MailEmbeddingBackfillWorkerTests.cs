// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using MailFathom.Application.Access;
using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Embeddings.Backfill;
using MailFathom.Application.Emails.Embeddings.Generations;
using MailFathom.Application.Emails.Embeddings.Limits;
using MailFathom.Application.Emails.Embeddings.Vectorization;
using MailFathom.Application.Persistence;
using MailFathom.Application.Spam.Gating;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using MailFathom.Host.Configuration.Embeddings;
using MailFathom.Host.Hosting.Workers;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Observability;
using MailFathom.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting.Workers;

public sealed class MailEmbeddingBackfillWorkerTests
{
    private static readonly TimeSpan IdleSweepInterval = TimeSpan.FromMinutes(15);

    [Fact]
    public async Task ExecuteAsync_BackfillDisabled_NeverReadsAStoredEmail()
    {
        // Arrange
        using var world = CreateWorld(new EmbeddingBackfillOptions { Enabled = false });

        // Act
        await world.Worker.StartAsync(CancellationToken.None);
        await world.Worker.ExecuteTask!;

        // Assert
        await world.BackfillStore.DidNotReceiveWithAnyArgs().FindResumePositionAsync(CancellationToken.None);
        Assert.Contains(
            world.Logger.Messages,
            message => message.Contains("embedding backfill is disabled", StringComparison.Ordinal));
    }

    /// <summary>
    /// This loop is the only thing that ever takes a pass, so a worker that never runs one has to say so: otherwise an
    /// activation records a due instant nothing will reach, and every later status read reports a pass overdue by
    /// however long the process has been up.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_BackfillDisabled_LeavesAnActivationUnableToScheduleAPassNothingWouldTake()
    {
        // Arrange
        using var world = CreateWorld(new EmbeddingBackfillOptions { Enabled = false });

        // Act
        await world.Worker.StartAsync(CancellationToken.None);
        await world.Worker.ExecuteTask!;
        world.Schedule.BringForward();

        // Assert
        Assert.Null(world.Schedule.NextPassDueAt);
    }

    /// <summary>Every pass is published as a span, which is what keeps its provider calls and commands out of a trace as orphans.</summary>
    /// <remarks>
    /// What the span carries is covered where the publisher is; what this establishes is the part only the worker
    /// decides — that a pass opens one at all. The count is not asserted, because the worker is a loop and how many
    /// passes it got through before the assertion is timing rather than behavior.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_APassRuns_PublishesItAsASpanOfItsOwn()
    {
        // Arrange
        var passes = new ConcurrentQueue<string>();
        using var listener = SampledMailFathomSpans.Recording(passes.Enqueue);
        using var world = CreateWorld(new EmbeddingBackfillOptions());

        // Act
        await world.Worker.StartAsync(CancellationToken.None);
        await world.Logger.WaitForOccurrences(
            "reached the end of the stored mail",
            occurrences: 1,
            TestContext.Current.CancellationToken);
        await world.Worker.StopAsync(CancellationToken.None);

        // Assert
        Assert.Contains(EmailEmbeddingBackfillTelemetry.PassSpanName, passes);
    }

    /// <summary>
    /// The walk is a repeating sweep rather than one that finishes, so reaching the end starts another pass instead of
    /// ending the worker — which is what makes the promise that a refused call and a full live queue are reached later
    /// something this keeps.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_SweepReachesTheEnd_StartsAnotherSweepInsteadOfEnding()
    {
        // Arrange
        using var world = CreateWorld(new EmbeddingBackfillOptions());

        // Act
        await world.Worker.StartAsync(CancellationToken.None);
        await world.Logger.WaitForOccurrences(
            "reached the end of the stored mail",
            occurrences: 1,
            TestContext.Current.CancellationToken);
        await world.AdvanceUntilLogged("reached the end of the stored mail", occurrences: 2);

        // Assert
        Assert.False(world.Worker.ExecuteTask!.IsCompleted);
        await world.Worker.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// The wait this worker used to make an operator sit through. Every pass before an activation ends with no
    /// generation to walk towards and takes the long interval, so the row an activation commits is one the sleeping
    /// worker cannot observe — and the clock is deliberately never advanced here, because what is being proved is that
    /// the pass no longer waits for it.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_APassBroughtForwardWhileIdle_TakesItWithoutWaitingOutTheIdleInterval()
    {
        // Arrange
        using var world = CreateWorld(new EmbeddingBackfillOptions(), activeProfile: null);
        await world.Worker.StartAsync(CancellationToken.None);
        await world.Logger.WaitForOccurrences(
            "No embedding profile is active",
            occurrences: 1,
            TestContext.Current.CancellationToken);

        // Act
        world.Schedule.BringForward();
        await world.Logger.WaitForOccurrences(
            "brought the next embedding backfill pass forward",
            occurrences: 1,
            TestContext.Current.CancellationToken);

        // Assert
        await world.Logger.WaitForOccurrences(
            "No embedding profile is active",
            occurrences: 2,
            TestContext.Current.CancellationToken);
        await world.Worker.StopAsync(CancellationToken.None);
    }

    /// <summary>An instance that has activated no profile is a supported state, so it is reported without a warning and costs no walk.</summary>
    [Fact]
    public async Task ExecuteAsync_NoActiveProfile_ReportsItWithoutWalkingTheMail()
    {
        // Arrange
        using var world = CreateWorld(new EmbeddingBackfillOptions(), activeProfile: null);

        // Act
        await world.Worker.StartAsync(CancellationToken.None);
        await world.Logger.WaitForOccurrences(
            "No embedding profile is active",
            occurrences: 1,
            TestContext.Current.CancellationToken);
        await world.Worker.StopAsync(CancellationToken.None);

        // Assert
        await world.BackfillStore.DidNotReceiveWithAnyArgs().GetEmailsAwaitingEmbeddingAsync(
            Arg.Any<StoredEmailId?>(),
            Arg.Any<EmbeddingProfileId>(),
            Arg.Any<int>(),
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A message that spends every call one turn allows stops nothing, so the run reports it beside its ending rather
    /// than as one — and it still reaches an operator, because the walk steps past such a message and a mailbox
    /// finishing one across several sweeps would otherwise look like one finishing them outright.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_AMessageSpentEveryCallOneTurnAllows_WarnsAboutTheBatchBound()
    {
        // Arrange
        // One message per run, because exhausting a turn's budget costs the generator's whole 512-call ceiling and the
        // default bounds would pay that five times over before the run this test is waiting on ended.
        using var world = CreateWorld(new EmbeddingBackfillOptions { BatchSize = 1, MaxBatchesPerRun = 1 });
        var message = StoredEmailId.Create(Guid.CreateVersion7());

        // A store that keeps reporting the same passage outstanding is what spends a turn's whole call budget.
        world.BackfillStore
            .GetEmailsAwaitingEmbeddingAsync(
                Arg.Any<StoredEmailId?>(),
                Arg.Any<EmbeddingProfileId>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<StoredEmailAwaitingEmbedding>>(
                [new StoredEmailAwaitingEmbedding(message, RequiresChunking: false)]));
        world.EmbeddingStore
            .GetChunksAwaitingEmbeddingAsync(
                Arg.Any<StoredEmailId>(),
                Arg.Any<EmbeddingProfileId>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<EmailChunkAwaitingEmbedding>>(
                [new EmailChunkAwaitingEmbedding(EmailChunkId.Create(Guid.CreateVersion7()), "a passage")]));

        // Act
        await world.Worker.StartAsync(CancellationToken.None);
        await world.Logger.WaitForOccurrences(
            "spent every provider call a turn is allowed",
            occurrences: 1,
            TestContext.Current.CancellationToken);
        await world.Worker.StopAsync(CancellationToken.None);

        // Assert
        Assert.Contains(
            world.Logger.Messages,
            message => message.Contains("MaxPassagesPerRequest", StringComparison.Ordinal));

        // The run's only work was that one message, so it was neither cut nor brought up to date — and it still wrote
        // every vector its budget bought. A progress line that counted whole messages alone would report none of them.
        Assert.Contains(
            world.Logger.Messages,
            message => message.Contains("gave 0 messages 512 vectors", StringComparison.Ordinal));
    }

    /// <summary>A failed run says nothing about whether messages remain, so the worker stays alive to resume next interval.</summary>
    [Fact]
    public async Task ExecuteAsync_RunFails_LogsItWithoutEndingTheWorker()
    {
        // Arrange
        using var world = CreateWorld(new EmbeddingBackfillOptions());
        world.BackfillStore
            .FindResumePositionAsync(Arg.Any<CancellationToken>())
            .Returns<StoredEmailId?>(_ => throw new InvalidOperationException("the database is unavailable"));

        // Act
        await world.Worker.StartAsync(CancellationToken.None);
        await world.Logger.WaitForOccurrences(
            "backfill run failed",
            occurrences: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(world.Worker.ExecuteTask!.IsCompleted);
        await world.Worker.StopAsync(CancellationToken.None);
    }

    /// <summary>A conflict with a competing writer is reported as a deferral rather than as a failure of the sweep.</summary>
    [Fact]
    public async Task ExecuteAsync_ConcurrencyConflict_ReportsADeferral()
    {
        // Arrange
        using var world = CreateWorld(new EmbeddingBackfillOptions());
        world.BackfillStore
            .FindResumePositionAsync(Arg.Any<CancellationToken>())
            .Returns<StoredEmailId?>(
                _ => throw new PersistenceConcurrencyConflictException("A competing writer won the race."));

        // Act
        await world.Worker.StartAsync(CancellationToken.None);
        await world.Logger.WaitForOccurrences(
            "optimistic concurrency conflict",
            occurrences: 1,
            TestContext.Current.CancellationToken);
        await world.Worker.StopAsync(CancellationToken.None);

        // Assert
        Assert.Contains(
            world.Logger.Messages,
            message => message.Contains("optimistic concurrency conflict", StringComparison.Ordinal));
    }

    /// <summary>
    /// A reached spend ceiling is neither interval's business: the run named the instant it stops applying, and the
    /// worker waits for exactly that rather than re-reading a ceiling it already knows binds.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_TheSpendCeilingIsReached_WaitsForThePeriodToRollOverRatherThanForAnInterval()
    {
        // Arrange
        using var world = CreateWorld(
            new EmbeddingBackfillOptions { BatchSize = 1, MaxBatchesPerRun = 1 },
            EmbeddingSpendBudget.Create(maxInputCharactersPerPeriod: 100, 0, TimeSpan.FromDays(1)),
            consumedInputCharacterCount: 100);
        var message = StoredEmailId.Create(Guid.CreateVersion7());

        world.BackfillStore
            .GetEmailsAwaitingEmbeddingAsync(
                Arg.Any<StoredEmailId?>(),
                Arg.Any<EmbeddingProfileId>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<StoredEmailAwaitingEmbedding>>(
                [new StoredEmailAwaitingEmbedding(message, RequiresChunking: false)]));
        world.EmbeddingStore
            .GetChunksAwaitingEmbeddingAsync(
                Arg.Any<StoredEmailId>(),
                Arg.Any<EmbeddingProfileId>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<EmailChunkAwaitingEmbedding>>(
                [new EmailChunkAwaitingEmbedding(EmailChunkId.Create(Guid.CreateVersion7()), "a passage")]));

        // Act
        await world.Worker.StartAsync(CancellationToken.None);
        await world.Logger.WaitForOccurrences(
            "spend ceiling for this period is reached",
            occurrences: 1,
            TestContext.Current.CancellationToken);
        await world.Worker.StopAsync(CancellationToken.None);

        // Assert
        Assert.Contains(
            world.Logger.Messages,
            line => line.Contains("MaxInputCharactersPerPeriod", StringComparison.Ordinal));
        await world.EmbeddingStore.DidNotReceiveWithAnyArgs().SaveEmbeddingsAsync(
            Arg.Any<IPersistenceSession>(),
            Arg.Any<RegisteredEmbeddingProfile>(),
            Arg.Any<IReadOnlyList<GeneratedChunkEmbedding>>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// One owner at their share stops that owner's mail and nobody else's, so the sweep steps past the message and
    /// runs to its end rather than pausing for the period — and the count of what it stepped past is the only place an
    /// operator reads that a bound was reached at all, since no wait follows to announce it.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_AnOwnerHasSpentTheirShare_StepsPastTheirMailAndReportsHowMuch()
    {
        // Arrange
        using var world = CreateWorld(
            new EmbeddingBackfillOptions { BatchSize = 1, MaxBatchesPerRun = 1 },
            EmbeddingSpendBudget.Create(
                maxInputCharactersPerPeriod: 1_000,
                maxInputCharactersPerPeriodPerOwner: 10,
                TimeSpan.FromDays(1)),
            consumedInputCharacterCount: 10,
            deploymentConsumedInputCharacterCount: 10);
        var message = StoredEmailId.Create(Guid.CreateVersion7());

        world.BackfillStore
            .GetEmailsAwaitingEmbeddingAsync(
                Arg.Any<StoredEmailId?>(),
                Arg.Any<EmbeddingProfileId>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<StoredEmailAwaitingEmbedding>>(
                [new StoredEmailAwaitingEmbedding(message, RequiresChunking: false)]));
        world.EmbeddingStore
            .GetChunksAwaitingEmbeddingAsync(
                Arg.Any<StoredEmailId>(),
                Arg.Any<EmbeddingProfileId>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<EmailChunkAwaitingEmbedding>>(
                [new EmailChunkAwaitingEmbedding(EmailChunkId.Create(Guid.CreateVersion7()), "a passage")]));

        // Act
        await world.Worker.StartAsync(CancellationToken.None);
        await world.Logger.WaitForOccurrences(
            "has spent what one period admits for them",
            occurrences: 1,
            TestContext.Current.CancellationToken);
        await world.Worker.StopAsync(CancellationToken.None);

        // Assert
        Assert.Contains(
            world.Logger.Messages,
            line => line.Contains("MaxInputCharactersPerPeriodPerOwner", StringComparison.Ordinal));

        // The deployment still had room, so nothing announced a wait: reporting the instance's ceiling here would send
        // an operator after disk when what is full is one person's share.
        Assert.DoesNotContain(
            world.Logger.Messages,
            line => line.Contains("the backfill is paused for", StringComparison.Ordinal));
        await world.EmbeddingStore.DidNotReceiveWithAnyArgs().SaveEmbeddingsAsync(
            Arg.Any<IPersistenceSession>(),
            Arg.Any<RegisteredEmbeddingProfile>(),
            Arg.Any<IReadOnlyList<GeneratedChunkEmbedding>>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Nothing ends on an owner's ceiling, so the same fact is true of every pass until the period rolls over — and a
    /// busy instance takes the short interval. The warning is written once for the period and the counter beside it
    /// carries the rest, which is what keeps one owner over their share from burying the log for everybody.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_AnOwnerStaysOverTheirShareAcrossPasses_WarnsOnceForThePeriod()
    {
        // Arrange
        using var world = CreateWorld(
            new EmbeddingBackfillOptions { BatchSize = 1, MaxBatchesPerRun = 1 },
            EmbeddingSpendBudget.Create(
                maxInputCharactersPerPeriod: 1_000,
                maxInputCharactersPerPeriodPerOwner: 10,
                TimeSpan.FromDays(1)),
            consumedInputCharacterCount: 10,
            deploymentConsumedInputCharacterCount: 10);
        var message = StoredEmailId.Create(Guid.CreateVersion7());

        world.BackfillStore
            .GetEmailsAwaitingEmbeddingAsync(
                Arg.Any<StoredEmailId?>(),
                Arg.Any<EmbeddingProfileId>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<StoredEmailAwaitingEmbedding>>(
                [new StoredEmailAwaitingEmbedding(message, RequiresChunking: false)]));
        world.EmbeddingStore
            .GetChunksAwaitingEmbeddingAsync(
                Arg.Any<StoredEmailId>(),
                Arg.Any<EmbeddingProfileId>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<EmailChunkAwaitingEmbedding>>(
                [new EmailChunkAwaitingEmbedding(EmailChunkId.Create(Guid.CreateVersion7()), "a passage")]));

        // Act
        await world.Worker.StartAsync(CancellationToken.None);
        await world.Logger.WaitForOccurrences(
            "has spent what one period admits for them",
            occurrences: 1,
            TestContext.Current.CancellationToken);

        // Three passes rather than two, so the assertion is about a period rather than about the first repeat.
        await world.AdvanceUntilLogged("The next embedding backfill pass is due in", occurrences: 3);
        await world.Worker.StopAsync(CancellationToken.None);

        // Assert
        Assert.Equal(
            1,
            world.Logger.Messages.Count(
                line => line.Contains("has spent what one period admits for them", StringComparison.Ordinal)));
    }

    private static EmbeddingProfileIdentity CreateIdentity() =>
        EmbeddingProfileIdentity.Create(
            "a-provider",
            "a-model",
            modelVersion: null,
            dimension: 8,
            EmbeddingDistanceMetric.Cosine,
            EmbeddingInputPreparation.Create(2_000, passageInstruction: null, normalizesVector: true));

    /// <summary>Reports a period as already having spent a given amount, which is what puts a run against a ceiling.</summary>
    /// <remarks>
    /// Substituted rather than kept in a fake ledger, because these tests are about how the worker paces itself after a
    /// run reports the ceiling; that the ledger adds up is proved where the ledger's own arithmetic lives.
    /// </remarks>
    private static IEmbeddingSpendLedger CreateLedgerReporting(
        long consumedInputCharacterCount,
        long? deploymentConsumedInputCharacterCount)
    {
        var ledger = Substitute.For<IEmbeddingSpendLedger>();
        ledger.ReadConsumedInputCharactersAsync(
                Arg.Any<DateTimeOffset>(),
                Arg.Any<MailOwnerId>(),
                Arg.Any<CancellationToken>())
            .Returns(new EmbeddingSpendTotals(
                consumedInputCharacterCount,
                deploymentConsumedInputCharacterCount ?? consumedInputCharacterCount));

        return ledger;
    }

    private static WorkerWorld CreateWorld(
        EmbeddingBackfillOptions settings,
        EmbeddingSpendBudget? spendBudget = null,
        long consumedInputCharacterCount = 0,
        long? deploymentConsumedInputCharacterCount = null) =>
        CreateWorld(
            settings,
            new RegisteredEmbeddingProfile(EmbeddingProfileId.Create(Guid.CreateVersion7()), CreateIdentity()),
            spendBudget,
            consumedInputCharacterCount,
            deploymentConsumedInputCharacterCount);

    private static WorkerWorld CreateWorld(
        EmbeddingBackfillOptions settings,
        RegisteredEmbeddingProfile? activeProfile,
        EmbeddingSpendBudget? spendBudget = null,
        long consumedInputCharacterCount = 0,
        long? deploymentConsumedInputCharacterCount = null)
    {
        settings.IdleSweepInterval = IdleSweepInterval;

        var world = new WorkerWorld();

        world.BackfillStore
            .GetEmailsAwaitingEmbeddingAsync(
                Arg.Any<StoredEmailId?>(),
                Arg.Any<EmbeddingProfileId>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<StoredEmailAwaitingEmbedding>>([]));

        // Only a serving generation, because what a reindex adds beside it is the upkeep pass's own behavior and is
        // covered where that lives; these tests are about the loop the worker runs around it.
        var generationStore = Substitute.For<IEmbeddingGenerationStore>();
        generationStore.ReadGenerationsAsync(Arg.Any<CancellationToken>())
            .Returns(new EmbeddingGenerations(activeProfile, Building: null));

        var textEmbeddingGenerator = Substitute.For<ITextEmbeddingGenerator>();
        textEmbeddingGenerator.Identity.Returns(CreateIdentity());
        textEmbeddingGenerator.MaximumPassagesPerCall.Returns(8);

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(world.TimeProvider);
        services.AddSingleton(world.BackfillStore);
        services.AddSingleton(generationStore);
        services.AddSingleton(world.EmbeddingStore);
        services.AddSingleton(textEmbeddingGenerator);
        services.AddSingleton(Substitute.For<IPersistenceSessionFactory>());
        services.AddSingleton(new PersistenceConcurrencyOptions());
        services.AddSingleton(new StoredEmailEmbeddingBackfillOptions
        {
            BatchSize = settings.BatchSize,
            MaxBatchesPerRun = settings.MaxBatchesPerRun,
        });
        services.AddSingleton(spendBudget ?? EmbeddingSpendBudget.Unbounded);
        services.AddSingleton(
            CreateLedgerReporting(consumedInputCharacterCount, deploymentConsumedInputCharacterCount));
        services.AddSingleton(EmbeddingRequestPacer.Create(maxRequestsPerMinute: 0, world.TimeProvider));
        services.AddSingleton<IDerivedWorkGateTelemetry>(new RecordingDerivedWorkGateTelemetry());
        services.AddScoped<EmbeddingSpendGate>();
        services.AddScoped<OptimisticConcurrencyRetryPolicy>();
        services.AddSingleton<IMailOwnership>(new StubMailOwnership());
        services.AddSingleton(SensitiveContentEgressGuards.Inactive());
        services.AddScoped<StoredEmailEmbeddingGenerator>();
        services.AddScoped<StoredEmailEmbeddingBackfill>();
        services.AddScoped<EmbeddingGenerationUpkeep>();

        var serviceProvider = services.BuildServiceProvider();
        world.Attach(
            serviceProvider,
            new MailEmbeddingBackfillWorker(
                serviceProvider.GetRequiredService<IServiceScopeFactory>(),
                new EmailEmbeddingBackfillTelemetry(),
                world.Schedule,
                Options.Create(settings),
                world.Logger,
                world.TimeProvider));

        return world;
    }

    /// <summary>The worker under test and the collaborators one run works against.</summary>
    private sealed class WorkerWorld : IDisposable
    {
        private ServiceProvider? serviceProvider;
        private MailEmbeddingBackfillWorker? worker;

        public WorkerWorld() => this.Schedule = new EmbeddingBackfillSchedule(this.TimeProvider);

        public AwaitingLogger<MailEmbeddingBackfillWorker> Logger { get; } = new();

        public FakeTimeProvider TimeProvider { get; } = new();

        public EmbeddingBackfillSchedule Schedule { get; }

        public IStoredEmailEmbeddingBackfillStore BackfillStore { get; } =
            Substitute.For<IStoredEmailEmbeddingBackfillStore>();

        public IEmailEmbeddingStore EmbeddingStore { get; } = Substitute.For<IEmailEmbeddingStore>();

        public MailEmbeddingBackfillWorker Worker => this.worker!;

        public void Attach(ServiceProvider provider, MailEmbeddingBackfillWorker attachedWorker)
        {
            this.serviceProvider = provider;
            this.worker = attachedWorker;
        }

        /// <summary>Moves the clock on until the worker has logged the message the given number of times.</summary>
        /// <remarks>
        /// A loop rather than a single advance, because a run's delay is created after the line that ends it is written:
        /// an advance that arrives before the delay exists is simply lost, and the next one fires it. What the loop
        /// proves is that the worker starts another sweep at all, which is the claim this worker's shape rests on.
        /// <para>
        /// Each attempt waits on the line as well as on a short window rather than merely yielding, because yielding
        /// hands the loop straight back to itself on a busy machine: every advance would then be spent while the pass
        /// that creates the next delay is still running, and the worker would be left waiting on a clock nothing moves
        /// again. The window bounds one attempt and never the wait — the line completing ends it immediately.
        /// </para>
        /// </remarks>
        public async Task AdvanceUntilLogged(string fragment, int occurrences)
        {
            const int advanceAttempts = 200;
            var passObservationWindow = TimeSpan.FromMilliseconds(20);

            var logged = this.Logger.WaitForOccurrences(
                fragment,
                occurrences,
                TestContext.Current.CancellationToken);

            for (var attempt = 0; attempt < advanceAttempts && !logged.IsCompleted; attempt++)
            {
                this.TimeProvider.Advance(IdleSweepInterval);

                await Task.WhenAny(logged, Task.Delay(passObservationWindow, TestContext.Current.CancellationToken));
            }

            await logged;
        }

        public void Dispose()
        {
            this.worker?.Dispose();
            this.serviceProvider?.Dispose();
        }
    }
}
