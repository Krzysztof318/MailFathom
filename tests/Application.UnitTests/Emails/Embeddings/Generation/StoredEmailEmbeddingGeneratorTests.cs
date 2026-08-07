// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Embeddings.Generation;
using MailFathom.Application.Persistence;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Emails;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Embeddings.Generation;

public sealed class StoredEmailEmbeddingGeneratorTests
{
    private static readonly StoredEmailId Message = StoredEmailId.Create(Guid.CreateVersion7());

    private static readonly EmbeddingProfileId ProfileId = EmbeddingProfileId.Create(Guid.CreateVersion7());

    [Fact]
    public async Task EmbedAsync_ActiveProfileAndOutstandingPassages_EmbedsEveryPassage()
    {
        // Arrange
        var store = new InMemoryEmailEmbeddingStore();
        var passages = CreatePassages(3);
        store.AddPassages(Message, passages);
        var generator = CreateGenerator(store, CreateProfile(), maximumPassagesPerCall: 8);

        // Act
        var run = await generator.EmbedAsync(Message, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredEmailEmbeddingOutcome.Embedded, run.Outcome);
        Assert.Equal(3, run.EmbeddedChunkCount);
        Assert.Null(run.Failure);
        Assert.Equal(passages.Select(passage => passage.Id).ToArray(), store.EmbeddedPassages);
    }

    [Fact]
    public async Task EmbedAsync_MorePassagesThanOneCallAccepts_SendsBoundedBatchesAndCommitsEachOne()
    {
        // Arrange
        var store = new InMemoryEmailEmbeddingStore();
        store.AddPassages(Message, CreatePassages(5));
        var textEmbeddingGenerator = new ScriptedTextEmbeddingGenerator(CreateIdentity(), maximumPassagesPerCall: 2);
        var generator = CreateGenerator(store, CreateProfile(), textEmbeddingGenerator);

        // Act
        var run = await generator.EmbedAsync(Message, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(5, run.EmbeddedChunkCount);
        Assert.Equal([2, 2, 1], textEmbeddingGenerator.RequestedBatches.Select(batch => batch.Count).ToArray());
        Assert.Equal(3, store.WriteCount);
    }

    [Fact]
    public async Task EmbedAsync_MessageAlreadyEmbedded_ProducesNothingAndCallsNoProvider()
    {
        // Arrange
        var store = new InMemoryEmailEmbeddingStore();
        store.AddPassages(Message, CreatePassages(2));
        var textEmbeddingGenerator = new ScriptedTextEmbeddingGenerator(CreateIdentity(), maximumPassagesPerCall: 8);
        var generator = CreateGenerator(store, CreateProfile(), textEmbeddingGenerator);
        await generator.EmbedAsync(Message, TestContext.Current.CancellationToken);

        // Act
        var repeat = await generator.EmbedAsync(Message, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredEmailEmbeddingOutcome.Embedded, repeat.Outcome);
        Assert.Equal(0, repeat.EmbeddedChunkCount);
        Assert.Single(textEmbeddingGenerator.RequestedBatches);
        Assert.Equal(2, store.StoredVectors.Count);
    }

    [Fact]
    public async Task EmbedAsync_NoProfileActivated_EmbedsNothingAndReachesNoProvider()
    {
        // Arrange
        var store = new InMemoryEmailEmbeddingStore();
        store.AddPassages(Message, CreatePassages(2));
        var textEmbeddingGenerator = new ScriptedTextEmbeddingGenerator(CreateIdentity(), maximumPassagesPerCall: 8);
        var generator = CreateGenerator(store, activeProfile: null, textEmbeddingGenerator);

        // Act
        var run = await generator.EmbedAsync(Message, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredEmailEmbeddingOutcome.NoActiveProfile, run.Outcome);
        Assert.Equal(0, run.EmbeddedChunkCount);
        Assert.Empty(textEmbeddingGenerator.RequestedBatches);
        Assert.Equal(0, store.ReadCount);
    }

    [Fact]
    public async Task EmbedAsync_ConfiguredModelIsNotTheActivatedOne_RefusesToWriteIntoTheActiveProfile()
    {
        // Arrange
        var store = new InMemoryEmailEmbeddingStore();
        store.AddPassages(Message, CreatePassages(2));
        var textEmbeddingGenerator = new ScriptedTextEmbeddingGenerator(
            CreateIdentity(modelIdentifier: "a-newer-model"),
            maximumPassagesPerCall: 8);
        var generator = CreateGenerator(store, CreateProfile(), textEmbeddingGenerator);

        // Act
        var run = await generator.EmbedAsync(Message, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredEmailEmbeddingOutcome.GeneratorDisagreesWithProfile, run.Outcome);
        Assert.Empty(textEmbeddingGenerator.RequestedBatches);
        Assert.Empty(store.StoredVectors);
    }

    [Theory]
    [InlineData(EmbeddingGenerationFailure.CredentialRejected)]
    [InlineData(EmbeddingGenerationFailure.RateLimited)]
    [InlineData(EmbeddingGenerationFailure.TransportFaulted)]
    [InlineData(EmbeddingGenerationFailure.VectorShapeUnexpected)]
    public async Task EmbedAsync_ProviderUnavailable_ReportsTheClassificationWithoutRepeatingTheCall(
        EmbeddingGenerationFailure failure)
    {
        // Arrange
        var store = new InMemoryEmailEmbeddingStore();
        store.AddPassages(Message, CreatePassages(2));
        var textEmbeddingGenerator = new ScriptedTextEmbeddingGenerator(CreateIdentity(), maximumPassagesPerCall: 8)
        {
            Failure = failure,
        };
        var generator = CreateGenerator(store, CreateProfile(), textEmbeddingGenerator);

        // Act
        var run = await generator.EmbedAsync(Message, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredEmailEmbeddingOutcome.ProviderFailed, run.Outcome);
        Assert.Equal(failure, run.Failure);
        Assert.Single(textEmbeddingGenerator.RequestedBatches);
        Assert.Empty(store.StoredVectors);
    }

    [Fact]
    public async Task EmbedAsync_ProviderFailsPartWayThrough_KeepsWhatWasCommittedAndLeavesTheRestOutstanding()
    {
        // Arrange
        var store = new InMemoryEmailEmbeddingStore();
        store.AddPassages(Message, CreatePassages(4));
        var textEmbeddingGenerator = new ScriptedTextEmbeddingGenerator(CreateIdentity(), maximumPassagesPerCall: 2)
        {
            Failure = EmbeddingGenerationFailure.RateLimited,
            FailingCallNumber = 2,
        };
        var generator = CreateGenerator(store, CreateProfile(), textEmbeddingGenerator);

        // Act
        var run = await generator.EmbedAsync(Message, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredEmailEmbeddingOutcome.ProviderFailed, run.Outcome);
        Assert.Equal(2, run.EmbeddedChunkCount);
        Assert.Equal(2, store.StoredVectors.Count);
    }

    [Fact]
    public async Task EmbedAsync_CancelledWhileTheProviderIsAnswering_StopsWithoutStartingAnotherCall()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        var store = new InMemoryEmailEmbeddingStore();
        store.AddPassages(Message, CreatePassages(4));
        var textEmbeddingGenerator = new ScriptedTextEmbeddingGenerator(CreateIdentity(), maximumPassagesPerCall: 2)
        {
            CancelOnCall = cancellation,
        };
        var generator = CreateGenerator(store, CreateProfile(), textEmbeddingGenerator);

        // Act
        var cancelled = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => generator.EmbedAsync(Message, cancellation.Token));

        // Assert
        Assert.Equal(cancellation.Token, cancelled.CancellationToken);
        Assert.Single(textEmbeddingGenerator.RequestedBatches);
    }

    private static IReadOnlyList<EmailChunkAwaitingEmbedding> CreatePassages(int count) =>
        [.. Enumerable.Range(0, count).Select(ordinal => new EmailChunkAwaitingEmbedding(
            EmailChunkId.Create(Guid.CreateVersion7()),
            $"passage {ordinal}"))];

    private static EmbeddingProfileIdentity CreateIdentity(string modelIdentifier = "a-model") =>
        EmbeddingProfileIdentity.Create(
            "a-provider",
            modelIdentifier,
            modelVersion: null,
            dimension: 8,
            EmbeddingDistanceMetric.Cosine,
            EmbeddingInputPreparation.Create(2_000, passageInstruction: null, normalizesVector: true));

    private static ActiveEmbeddingProfile CreateProfile() => new(ProfileId, CreateIdentity());

    private static StoredEmailEmbeddingGenerator CreateGenerator(
        IEmailEmbeddingStore store,
        ActiveEmbeddingProfile? activeProfile,
        int maximumPassagesPerCall) =>
        CreateGenerator(
            store,
            activeProfile,
            new ScriptedTextEmbeddingGenerator(CreateIdentity(), maximumPassagesPerCall));

    private static StoredEmailEmbeddingGenerator CreateGenerator(
        IEmailEmbeddingStore store,
        ActiveEmbeddingProfile? activeProfile,
        ITextEmbeddingGenerator textEmbeddingGenerator)
    {
        var profileReader = Substitute.For<IActiveEmbeddingProfileReader>();
        profileReader.FindActiveProfileAsync(Arg.Any<CancellationToken>()).Returns(activeProfile);

        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Substitute.For<IPersistenceSession>());

        return new StoredEmailEmbeddingGenerator(
            profileReader,
            store,
            textEmbeddingGenerator,
            new OptimisticConcurrencyRetryPolicy(
                sessionFactory,
                new PersistenceConcurrencyOptions(),
                new FakeTimeProvider()));
    }
}
