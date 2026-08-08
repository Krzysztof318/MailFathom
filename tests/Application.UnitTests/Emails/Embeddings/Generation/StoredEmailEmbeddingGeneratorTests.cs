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
        var generator = CreateGenerator(store, maximumPassagesPerCall: 8);

        // Act
        var run = await generator.EmbedAsync(Message, CreateProfile(), TestContext.Current.CancellationToken);

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
        var generator = CreateGenerator(store, textEmbeddingGenerator);

        // Act
        var run = await generator.EmbedAsync(Message, CreateProfile(), TestContext.Current.CancellationToken);

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
        var generator = CreateGenerator(store, textEmbeddingGenerator);
        await generator.EmbedAsync(Message, CreateProfile(), TestContext.Current.CancellationToken);

        // Act
        var repeat = await generator.EmbedAsync(Message, CreateProfile(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredEmailEmbeddingOutcome.Embedded, repeat.Outcome);
        Assert.Equal(0, repeat.EmbeddedChunkCount);
        Assert.Single(textEmbeddingGenerator.RequestedBatches);
        Assert.Equal(2, store.StoredVectors.Count);
    }

    /// <summary>
    /// The generation is the caller's decision, which is what lets a reindex fill a new one while the live path goes on
    /// embedding arriving mail into the one still answering searches. A generator that resolved it itself could serve
    /// only one of the two, and the vectors of the other would silently land under the wrong attribution.
    /// </summary>
    [Fact]
    public async Task EmbedAsync_GenerationBeingBuilt_AttributesEveryVectorToThatGenerationAlone()
    {
        // Arrange
        var store = new InMemoryEmailEmbeddingStore();
        store.AddPassages(Message, CreatePassages(2));
        var generator = CreateGenerator(store, maximumPassagesPerCall: 8);
        var building = new RegisteredEmbeddingProfile(
            EmbeddingProfileId.Create(Guid.CreateVersion7()),
            CreateIdentity());

        // Act
        var run = await generator.EmbedAsync(Message, building, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredEmailEmbeddingOutcome.Embedded, run.Outcome);
        Assert.Equal([building.Id, building.Id], store.StoredVectors.Keys.Select(key => key.ProfileId).ToArray());
        Assert.DoesNotContain(ProfileId, store.StoredVectors.Keys.Select(key => key.ProfileId));
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
        var generator = CreateGenerator(store, textEmbeddingGenerator);

        // Act
        var run = await generator.EmbedAsync(Message, CreateProfile(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredEmailEmbeddingOutcome.GeneratorDisagreesWithProfile, run.Outcome);
        Assert.Empty(textEmbeddingGenerator.RequestedBatches);
        Assert.Empty(store.StoredVectors);
    }

    [Theory]
    [InlineData(EmbeddingGenerationFailure.CredentialRejected)]
    [InlineData(EmbeddingGenerationFailure.RateLimited)]
    [InlineData(EmbeddingGenerationFailure.RequestTimedOut)]
    [InlineData(EmbeddingGenerationFailure.TransportFaulted)]
    [InlineData(EmbeddingGenerationFailure.RequestRefused)]
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
        var generator = CreateGenerator(store, textEmbeddingGenerator);

        // Act
        var run = await generator.EmbedAsync(Message, CreateProfile(), TestContext.Current.CancellationToken);

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
        var generator = CreateGenerator(store, textEmbeddingGenerator);

        // Act
        var run = await generator.EmbedAsync(Message, CreateProfile(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredEmailEmbeddingOutcome.ProviderFailed, run.Outcome);
        Assert.Equal(2, run.EmbeddedChunkCount);
        Assert.Equal(2, store.StoredVectors.Count);
    }

    /// <summary>
    /// A batch size far below what one message carries is a supported configuration, so a message can need more calls
    /// than a turn is allowed. Reporting that as <see cref="StoredEmailEmbeddingOutcome.Embedded" /> would say the
    /// message is whole when it is not — a truncated message stays retrievable and simply answers worse, so nothing
    /// later would notice.
    /// </summary>
    [Fact]
    public async Task EmbedAsync_MoreCallsThanOneTurnAllows_ReportsTheMessageAsUnfinishedRatherThanEmbedded()
    {
        // Arrange
        const int callBudget = 512;
        var store = new InMemoryEmailEmbeddingStore();
        store.AddPassages(Message, CreatePassages(callBudget + 5));
        var textEmbeddingGenerator = new ScriptedTextEmbeddingGenerator(CreateIdentity(), maximumPassagesPerCall: 1);
        var generator = CreateGenerator(store, textEmbeddingGenerator);

        // Act
        var run = await generator.EmbedAsync(Message, CreateProfile(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredEmailEmbeddingOutcome.CallBudgetExhausted, run.Outcome);
        Assert.Equal(callBudget, run.EmbeddedChunkCount);
        Assert.Equal(callBudget, textEmbeddingGenerator.RequestedBatches.Count);

        // What it did embed stays durable, which is what leaves the rest outstanding for the backfill rather than lost.
        Assert.Equal(callBudget, store.StoredVectors.Count);
    }

    /// <summary>
    /// The last call taking the final passages leaves the loop with nowhere to go rather than with work left, and
    /// reporting that message as truncated would be a false warning exactly as the opposite is a false success.
    /// </summary>
    [Fact]
    public async Task EmbedAsync_TheLastAllowedCallTakesTheFinalPassages_ReportsTheMessageAsEmbedded()
    {
        // Arrange
        const int callBudget = 512;
        var store = new InMemoryEmailEmbeddingStore();
        store.AddPassages(Message, CreatePassages(callBudget));
        var textEmbeddingGenerator = new ScriptedTextEmbeddingGenerator(CreateIdentity(), maximumPassagesPerCall: 1);
        var generator = CreateGenerator(store, textEmbeddingGenerator);

        // Act
        var run = await generator.EmbedAsync(Message, CreateProfile(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredEmailEmbeddingOutcome.Embedded, run.Outcome);
        Assert.Equal(callBudget, run.EmbeddedChunkCount);
        Assert.Equal(callBudget, textEmbeddingGenerator.RequestedBatches.Count);
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
        var generator = CreateGenerator(store, textEmbeddingGenerator);

        // Act
        var cancelled = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => generator.EmbedAsync(Message, CreateProfile(), cancellation.Token));

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

    private static RegisteredEmbeddingProfile CreateProfile() => new(ProfileId, CreateIdentity());

    private static StoredEmailEmbeddingGenerator CreateGenerator(
        IEmailEmbeddingStore store,
        int maximumPassagesPerCall) =>
        CreateGenerator(store, new ScriptedTextEmbeddingGenerator(CreateIdentity(), maximumPassagesPerCall));

    private static StoredEmailEmbeddingGenerator CreateGenerator(
        IEmailEmbeddingStore store,
        ITextEmbeddingGenerator textEmbeddingGenerator)
    {
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Substitute.For<IPersistenceSession>());

        return new StoredEmailEmbeddingGenerator(
            store,
            textEmbeddingGenerator,
            new OptimisticConcurrencyRetryPolicy(
                sessionFactory,
                new PersistenceConcurrencyOptions(),
                new FakeTimeProvider()));
    }
}
