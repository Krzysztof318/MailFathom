// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Embeddings.Generation;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Emails;
using MailFathom.Host.Hosting.Workers;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Observability;
using Microsoft.Extensions.DependencyInjection;
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

    [Fact]
    public async Task ExecuteAsync_OneMessageFails_ReportsItAndKeepsEmbeddingTheOnesBehindIt()
    {
        // Arrange
        var messages = CreateMessages(2);
        var profileReader = Substitute.For<IActiveEmbeddingProfileReader>();
        profileReader.FindActiveProfileAsync(Arg.Any<CancellationToken>())
            .Returns(
                _ => throw new InvalidOperationException("the database is unavailable"),
                _ => Task.FromResult<ActiveEmbeddingProfile?>(CreateProfile()));
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

    private static IReadOnlyList<StoredEmailId> CreateMessages(int count) =>
        [.. Enumerable.Range(0, count).Select(_ => StoredEmailId.Create(Guid.CreateVersion7()))];

    private static EmbeddingProfileIdentity Identity { get; } = EmbeddingProfileIdentity.Create(
        "a-provider",
        "a-model",
        modelVersion: null,
        dimension: 8,
        EmbeddingDistanceMetric.Cosine,
        EmbeddingInputPreparation.Create(2_000, passageInstruction: null, normalizesVector: true));

    private static ActiveEmbeddingProfile CreateProfile() =>
        new(EmbeddingProfileId.Create(Guid.CreateVersion7()), Identity);

    private static IActiveEmbeddingProfileReader CreateProfileReader(ActiveEmbeddingProfile? profile)
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

    private static MailEmbeddingWorker CreateWorker(
        IReadOnlyList<StoredEmailId> messages,
        IActiveEmbeddingProfileReader profileReader,
        IEmailEmbeddingStore embeddingStore,
        out RecordingLogger<MailEmbeddingWorker> logger)
    {
        logger = new RecordingLogger<MailEmbeddingWorker>();
        var timeProvider = new FakeTimeProvider();

        var textEmbeddingGenerator = Substitute.For<ITextEmbeddingGenerator>();
        textEmbeddingGenerator.Identity.Returns(Identity);
        textEmbeddingGenerator.MaximumPassagesPerCall.Returns(8);

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(timeProvider);
        services.AddSingleton(profileReader);
        services.AddSingleton(embeddingStore);
        services.AddSingleton(textEmbeddingGenerator);
        services.AddSingleton(Substitute.For<IPersistenceSessionFactory>());
        services.AddSingleton(new PersistenceConcurrencyOptions());
        services.AddScoped<OptimisticConcurrencyRetryPolicy>();
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
