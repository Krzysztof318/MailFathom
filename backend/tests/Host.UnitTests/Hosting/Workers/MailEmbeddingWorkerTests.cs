// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics;
using MailFathom.Application.Access;
using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Embeddings.Limits;
using MailFathom.Application.Emails.Embeddings.Vectorization;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using MailFathom.Host.Hosting.Workers;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Observability;
using MailFathom.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting.Workers;

public sealed class MailEmbeddingWorkerTests
{
    /// <summary>Guards against a hung worker. No assertion depends on how long the run actually takes.</summary>
    private static readonly TimeSpan DeadlockGuard = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task ExecuteAsync_MessagesAreWaiting_EmbedsEveryOneOfThemInItsOwnScope()
    {
        // Arrange
        var messages = CreateMessages(3);
        var embeddingStore = CreateStoreWithNothingOutstanding();
        using var worker = CreateWorker(messages, CreateProfileReader(CreateProfile()), embeddingStore, out _);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);

        // Assert
        await embeddingStore.Received(messages.Count).GetChunksAwaitingEmbeddingAsync(
            Arg.Any<StoredEmailId>(),
            Arg.Any<EmbeddingProfileId>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>The turn's work runs inside the turn's own span, which is what makes the provider call attributable.</summary>
    /// <remarks>
    /// Asserted from inside the work rather than from the published span, because a span opened and closed around
    /// nothing looks identical from outside — and that is exactly what moving the call past the await would produce.
    /// The profile read is the first thing the turn does, so what was current there is what the provider call and the
    /// commands after it will be children of.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_AMessageIsWaiting_EmbedsItInsideTheTurnSpan()
    {
        // Arrange
        var spansTheTurnRanInside = new List<string?>();
        var profileReader = Substitute.For<IActiveEmbeddingProfileReader>();
        profileReader.FindActiveProfileAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                spansTheTurnRanInside.Add(Activity.Current?.OperationName);

                return Task.FromResult<RegisteredEmbeddingProfile?>(CreateProfile());
            });

        using var listener = SampledMailFathomSpans.Sampling();
        using var worker = CreateWorker(CreateMessages(1), profileReader, CreateStoreWithNothingOutstanding(), out _);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([EmailEmbeddingTelemetry.MessageSpanName], spansTheTurnRanInside);
    }

    [Fact]
    public async Task ExecuteAsync_OneMessageFails_ReportsItAndKeepsEmbeddingTheOnesBehindIt()
    {
        // Arrange
        var messages = CreateMessages(2);
        var profileReader = Substitute.For<IActiveEmbeddingProfileReader>();
        profileReader.FindActiveProfileAsync(Arg.Any<CancellationToken>())
            .Returns(
                _ => throw new InvalidOperationException("the database is unavailable"),
                _ => Task.FromResult<RegisteredEmbeddingProfile?>(CreateProfile()));
        var embeddingStore = CreateStoreWithNothingOutstanding();
        using var worker = CreateWorker(messages, profileReader, embeddingStore, out var logger);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            logger.Messages,
            message => message.Contains("Embedding one message failed", StringComparison.Ordinal));
        await embeddingStore.Received(1).GetChunksAwaitingEmbeddingAsync(
            Arg.Any<StoredEmailId>(),
            Arg.Any<EmbeddingProfileId>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_NoProfileIsActive_ReportsItWithoutReadingAPassage()
    {
        // Arrange
        var embeddingStore = CreateStoreWithNothingOutstanding();
        using var worker = CreateWorker(CreateMessages(1), CreateProfileReader(null), embeddingStore, out var logger);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            logger.Messages,
            message => message.Contains("No embedding profile is active", StringComparison.Ordinal));
        await embeddingStore.DidNotReceiveWithAnyArgs().GetChunksAwaitingEmbeddingAsync(
            Arg.Any<StoredEmailId>(),
            Arg.Any<EmbeddingProfileId>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A reached ceiling pauses the worker rather than letting it drain the backlog one refusal at a time, and the
    /// period rolling over is what releases it — with nobody having acted, because nothing about the ceiling was wrong.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_TheSpendCeilingIsReached_PausesUntilThePeriodRollsOverAndThenCarriesOn()
    {
        // Arrange
        var period = TimeSpan.FromDays(1);
        var timeProvider = new FakeTimeProvider();
        var embeddingStore = CreateStoreWithOnePassageOutstanding();
        var logger = new AwaitingLogger<MailEmbeddingWorker>();
        using var worker = CreateWorker(
            CreateMessages(2),
            CreateProfileReader(CreateProfile()),
            embeddingStore,
            logger,
            EmbeddingSpendBudget.Create(maxInputCharactersPerPeriod: 10, 0, period),
            consumedInputCharacterCount: 10,
            timeProvider);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await logger.WaitForOccurrences(
            "embedding is paused",
            occurrences: 1,
            TestContext.Current.CancellationToken);

        // Assert
        // Exactly one message has been taken while the clock stands still: the worker is waiting rather than working
        // through what is behind it.
        await embeddingStore.Received(1).GetChunksAwaitingEmbeddingAsync(
            Arg.Any<StoredEmailId>(),
            Arg.Any<EmbeddingProfileId>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());

        await AdvanceUntilLogged(timeProvider, logger, period, "embedding is paused", occurrences: 2);
        await worker.StopAsync(CancellationToken.None);

        await embeddingStore.Received(2).GetChunksAwaitingEmbeddingAsync(
            Arg.Any<StoredEmailId>(),
            Arg.Any<EmbeddingProfileId>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// One owner at their share stops that owner's mail and nobody else's, so the loop keeps taking messages with the
    /// clock standing still — and says so once for the period rather than once for every message of theirs waiting.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_OneOwnerHasSpentTheirShare_KeepsTakingMessagesAndReportsItOncePerPeriod()
    {
        // Arrange
        var messages = CreateMessages(3);
        var embeddingStore = CreateStoreWithOnePassageOutstanding();
        var logger = new RecordingLogger<MailEmbeddingWorker>();
        using var worker = CreateWorker(
            messages,
            CreateProfileReader(CreateProfile()),
            embeddingStore,
            logger,
            EmbeddingSpendBudget.Create(
                maxInputCharactersPerPeriod: 1_000,
                maxInputCharactersPerPeriodPerOwner: 10,
                TimeSpan.FromDays(1)),
            consumedInputCharacterCount: 10,
            new FakeTimeProvider(),
            deploymentConsumedInputCharacterCount: 10);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);

        // Assert
        // The run reached the end of the backlog without the clock moving, which is what says no pause was taken.
        await embeddingStore.Received(messages.Count).GetChunksAwaitingEmbeddingAsync(
            Arg.Any<StoredEmailId>(),
            Arg.Any<EmbeddingProfileId>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
        Assert.Equal(
            1,
            logger.Messages.Count(
                message => message.Contains("has spent what one period admits for them", StringComparison.Ordinal)));
        Assert.DoesNotContain(
            logger.Messages,
            message => message.Contains("embedding is paused", StringComparison.Ordinal));
    }

    /// <summary>Moves the clock on until the worker has logged the line the given number of times.</summary>
    /// <remarks>
    /// A loop rather than a single advance, because the wait is created after the line that announces it is written:
    /// an advance that arrives before the delay exists is simply lost, and the next one fires it.
    /// </remarks>
    private static async Task AdvanceUntilLogged(
        FakeTimeProvider timeProvider,
        AwaitingLogger<MailEmbeddingWorker> logger,
        TimeSpan step,
        string fragment,
        int occurrences)
    {
        const int advanceAttempts = 200;
        var passObservationWindow = TimeSpan.FromMilliseconds(20);

        var logged = logger.WaitForOccurrences(fragment, occurrences, TestContext.Current.CancellationToken);

        for (var attempt = 0; attempt < advanceAttempts && !logged.IsCompleted; attempt++)
        {
            timeProvider.Advance(step);

            await Task.WhenAny(logged, Task.Delay(passObservationWindow, TestContext.Current.CancellationToken));
        }

        await logged;
    }

    private static IReadOnlyList<StoredEmailId> CreateMessages(int count) =>
        [.. Enumerable.Range(0, count).Select(_ => StoredEmailId.Create(Guid.CreateVersion7()))];

    private static EmbeddingProfileIdentity Identity { get; } = EmbeddingProfileIdentity.Create(
        "a-provider",
        "a-model",
        modelVersion: null,
        dimension: 8,
        EmbeddingDistanceMetric.Cosine,
        EmbeddingInputPreparation.Create(2_000, passageInstruction: null, normalizesVector: true));

    private static RegisteredEmbeddingProfile CreateProfile() =>
        new(EmbeddingProfileId.Create(Guid.CreateVersion7()), Identity);

    private static IActiveEmbeddingProfileReader CreateProfileReader(RegisteredEmbeddingProfile? profile)
    {
        var reader = Substitute.For<IActiveEmbeddingProfileReader>();
        reader.FindActiveProfileAsync(Arg.Any<CancellationToken>()).Returns(profile);

        return reader;
    }

    private static IEmailEmbeddingStore CreateStoreWithNothingOutstanding()
    {
        var store = Substitute.For<IEmailEmbeddingStore>();
        store.GetChunksAwaitingEmbeddingAsync(
                Arg.Any<StoredEmailId>(),
                Arg.Any<EmbeddingProfileId>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<EmailChunkAwaitingEmbedding>>([]));

        return store;
    }

    private static IEmailEmbeddingStore CreateStoreWithOnePassageOutstanding()
    {
        var store = Substitute.For<IEmailEmbeddingStore>();
        store.GetChunksAwaitingEmbeddingAsync(
                Arg.Any<StoredEmailId>(),
                Arg.Any<EmbeddingProfileId>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<EmailChunkAwaitingEmbedding>>(
                [new EmailChunkAwaitingEmbedding(EmailChunkId.Create(Guid.CreateVersion7()), "a passage")]));

        return store;
    }

    private static MailEmbeddingWorker CreateWorker(
        IReadOnlyList<StoredEmailId> messages,
        IActiveEmbeddingProfileReader profileReader,
        IEmailEmbeddingStore embeddingStore,
        out RecordingLogger<MailEmbeddingWorker> logger)
    {
        var recordingLogger = new RecordingLogger<MailEmbeddingWorker>();
        logger = recordingLogger;

        return CreateWorker(
            messages,
            profileReader,
            embeddingStore,
            recordingLogger,
            EmbeddingSpendBudget.Unbounded,
            consumedInputCharacterCount: 0,
            new FakeTimeProvider());
    }

    private static MailEmbeddingWorker CreateWorker(
        IReadOnlyList<StoredEmailId> messages,
        IActiveEmbeddingProfileReader profileReader,
        IEmailEmbeddingStore embeddingStore,
        ILogger<MailEmbeddingWorker> logger,
        EmbeddingSpendBudget spendBudget,
        long consumedInputCharacterCount,
        FakeTimeProvider timeProvider,
        long? deploymentConsumedInputCharacterCount = null)
    {
        var textEmbeddingGenerator = Substitute.For<ITextEmbeddingGenerator>();
        textEmbeddingGenerator.Identity.Returns(Identity);
        textEmbeddingGenerator.MaximumPassagesPerCall.Returns(8);

        var spendLedger = Substitute.For<IEmbeddingSpendLedger>();
        spendLedger.ReadConsumedInputCharactersAsync(
                Arg.Any<DateTimeOffset>(),
                Arg.Any<MailOwnerId>(),
                Arg.Any<CancellationToken>())
            .Returns(new EmbeddingSpendTotals(
                consumedInputCharacterCount,
                deploymentConsumedInputCharacterCount ?? consumedInputCharacterCount));

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(timeProvider);
        services.AddSingleton(profileReader);
        services.AddSingleton(embeddingStore);
        services.AddSingleton(textEmbeddingGenerator);
        services.AddSingleton(Substitute.For<IPersistenceSessionFactory>());
        services.AddSingleton(new PersistenceConcurrencyOptions());
        services.AddSingleton(spendBudget);
        services.AddSingleton(spendLedger);
        services.AddSingleton(EmbeddingRequestPacer.Create(maxRequestsPerMinute: 0, timeProvider));
        services.AddScoped<EmbeddingSpendGate>();
        services.AddScoped<OptimisticConcurrencyRetryPolicy>();
        services.AddSingleton<IMailOwnership>(new StubMailOwnership());
        services.AddScoped<StoredEmailEmbeddingGenerator>();

        var serviceProvider = services.BuildServiceProvider();

        return new MailEmbeddingWorker(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            new ScriptedEmailEmbeddingBacklog(messages),
            new EmailEmbeddingTelemetry(),
            logger,
            timeProvider);
    }
}
