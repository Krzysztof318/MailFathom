// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.AiProviders;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Embeddings.Administration;
using MailFathom.Application.Emails.Embeddings.Generations;
using MailFathom.Application.Emails.Embeddings.Indexing;
using MailFathom.Application.Emails.Embeddings.Limits;
using MailFathom.Application.Persistence;
using MailFathom.Host.Api;
using MailFathom.Host.Configuration.Embeddings;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers what the embedding routes decide, and what they refuse to start.</summary>
/// <remarks>
/// The refusals are the point. One of these routes begins a provider bill, so what is asserted is that it refuses
/// before writing anything when the budget says so, that it says which two numbers refused it, and that a deployment
/// with nothing declared is told that rather than being answered with a container failure.
/// </remarks>
public sealed class EmbeddingProfileEndpointsTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The deployment's half of an agreement with a command it cannot reference. <c>mfctl</c> composes these three
    /// paths from constants of its own and its suite pins the same literals, because a rename on either side compiles
    /// cleanly and leaves every embedding command reaching a 404 that reads exactly like an endpoint nobody enabled.
    /// </summary>
    [Fact]
    public void Routes_AreThePathsTheCommandComposes()
    {
        Assert.Equal("/embeddings", EmbeddingProfileEndpoints.StatusRoute);
        Assert.Equal("/embeddings/activation", EmbeddingProfileEndpoints.ActivationRoute);
        Assert.Equal("/embeddings/reindex/cancellation", EmbeddingProfileEndpoints.ReindexCancellationRoute);
    }

    /// <summary>The instance whose operator most needs this answer is the one that declared nothing, so it is answered rather than refused.</summary>
    [Fact]
    public async Task ReadStatusAsync_NothingDeclared_AnswersWithTheAbsencesRatherThanARefusal()
    {
        // Arrange
        var world = CreateWorld();

        // Act
        var result = await EmbeddingProfileEndpoints.ReadStatusAsync(
            new DeclaredEmbeddingGeometry(Identity: null),
            world.StatusReader,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result.Value?.Declared);
        Assert.Null(result.Value?.Serving);
        Assert.Null(result.Value?.Building);
        Assert.False(result.Value?.ActivationOutstanding);
        Assert.NotNull(result.Value?.Spend);
    }

    /// <summary>An operator who edited configuration and expected search results to change learns here that nothing took it up.</summary>
    [Fact]
    public async Task ReadStatusAsync_ADeclarationNobodyActivated_ReportsTheActivationAsOutstanding()
    {
        // Arrange
        var world = CreateWorld();
        var declared = CreateIdentity("a-model");

        // Act
        var result = await EmbeddingProfileEndpoints.ReadStatusAsync(
            new DeclaredEmbeddingGeometry(declared),
            world.StatusReader,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Value?.ActivationOutstanding);
        Assert.Equal("a-model", result.Value?.Declared?.Model);
        Assert.Equal(
            EmbeddingProfileFingerprint.Compute(declared).Value,
            result.Value?.Declared?.Fingerprint);
    }

    /// <summary>There is nothing to activate where nothing is declared, and the answer names the setting that would declare one.</summary>
    [Fact]
    public async Task ActivateAsync_NothingDeclared_IsRefusedNamingTheConfigurationSection()
    {
        // Arrange
        var world = CreateWorld();

        // Act
        var result = await EmbeddingProfileEndpoints.ActivateAsync(
            new DeclaredEmbeddingGeometry(Identity: null),
            world.Activation,
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.Contains("Embeddings:Endpoints", refusal.ProblemDetails.Detail, StringComparison.Ordinal);
    }

    /// <summary>Reading what an activation would cost is refused the same way, so a client never has to interpret an empty assessment.</summary>
    [Fact]
    public async Task ReadActivationAsync_NothingDeclared_IsRefusedRatherThanAnsweredWithAnEmptyAssessment()
    {
        // Arrange
        var world = CreateWorld();

        // Act
        var result = await EmbeddingProfileEndpoints.ReadActivationAsync(
            new DeclaredEmbeddingGeometry(Identity: null),
            world.Activation,
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
    }

    /// <summary>
    /// The refusal has to carry both numbers: the estimate alone leaves the operator guessing at the ceiling, and the
    /// ceiling alone leaves them guessing at how far over it they are.
    /// </summary>
    [Fact]
    public async Task ActivateAsync_AnEstimateAboveTheCeiling_IsRefusedNamingTheEstimateAndTheCeiling()
    {
        // Arrange
        var world = CreateWorld(maxInputCharactersPerPeriod: 10_000);
        var declared = CreateIdentity("a-model");
        world.WorkloadIs(new EmbeddingWorkload(500, 500, 2_000, 200_000));

        // Act
        var result = await EmbeddingProfileEndpoints.ActivateAsync(
            new DeclaredEmbeddingGeometry(declared),
            world.Activation,
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, refusal.StatusCode);
        Assert.Contains("200000", refusal.ProblemDetails.Detail, StringComparison.Ordinal);
        Assert.Contains("10000", refusal.ProblemDetails.Detail, StringComparison.Ordinal);
        await world.GenerationStore.DidNotReceiveWithAnyArgs().RegisterBuildingAsync(
            default!,
            default!,
            TestContext.Current.CancellationToken);
    }

    /// <summary>One reindex runs at a time, and the refusal says what an operator does about that.</summary>
    [Fact]
    public async Task ActivateAsync_ADifferentReindexRunning_IsRefusedSayingToCancelIt()
    {
        // Arrange
        var world = CreateWorld();
        world.GenerationsAre(new EmbeddingGenerations(
            Serving: null,
            new RegisteredEmbeddingProfile(
                EmbeddingProfileId.Create(Guid.CreateVersion7()),
                CreateIdentity("a-different-model"))));

        // Act
        var result = await EmbeddingProfileEndpoints.ActivateAsync(
            new DeclaredEmbeddingGeometry(CreateIdentity("a-model")),
            world.Activation,
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, refusal.StatusCode);
        Assert.Contains("Cancel it", refusal.ProblemDetails.Detail, StringComparison.Ordinal);
    }

    /// <summary>An activation that ran reports what it started and the estimate it was weighed as, so the figure confirmed is recognizable in the answer.</summary>
    [Fact]
    public async Task ActivateAsync_ADeclarationNothingHasTakenUp_StartsTheReindexAndReportsWhatItWasWeighedAs()
    {
        // Arrange
        var world = CreateWorld();
        var registered = new RegisteredEmbeddingProfile(
            EmbeddingProfileId.Create(Guid.CreateVersion7()),
            CreateIdentity("a-model"));
        world.RegistersAs(registered);
        world.WorkloadIs(new EmbeddingWorkload(120, 120, 400, 40_000));

        // Act
        var result = await EmbeddingProfileEndpoints.ActivateAsync(
            new DeclaredEmbeddingGeometry(CreateIdentity("a-model")),
            world.Activation,
            TestContext.Current.CancellationToken);

        // Assert
        var started = Assert.IsType<Ok<EmbeddingActivationResponse>>(result.Result);
        Assert.Equal(nameof(EmbeddingProfileActivationOutcome.ReindexStarted), started.Value?.Outcome);
        Assert.Equal(registered.Id.Value, started.Value?.ProfileId);
        Assert.Equal(400, started.Value?.Estimate.OutstandingPassageCount);
        Assert.Equal(10_000, started.Value?.Estimate.ApproximateTokenCount);
    }

    /// <summary>Finding nothing to cancel is an outcome rather than a refusal: a run that finished first is not a fault.</summary>
    [Fact]
    public async Task CancelReindexAsync_NoReindexRunning_AnswersWithTheOutcomeRatherThanARefusal()
    {
        // Arrange
        var world = CreateWorld();

        // Act
        var result = await EmbeddingProfileEndpoints.CancelReindexAsync(
            world.Cancellation,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            nameof(EmbeddingReindexCancellationOutcome.NothingBuilding),
            result.Value?.Outcome);
    }

    private static EmbeddingProfileIdentity CreateIdentity(string modelIdentifier) =>
        EmbeddingProfileIdentity.Create(
            "a-provider",
            modelIdentifier,
            modelVersion: null,
            dimension: 8,
            EmbeddingDistanceMetric.Cosine,
            EmbeddingInputPreparation.Create(2_000, passageInstruction: null, normalizesVector: true));

    private static EndpointWorld CreateWorld(long maxInputCharactersPerPeriod = 1_000_000)
    {
        var generationStore = Substitute.For<IEmbeddingGenerationStore>();
        generationStore.ReadGenerationsAsync(Arg.Any<CancellationToken>()).Returns(EmbeddingGenerations.None);

        var workloadReader = Substitute.For<IEmbeddingWorkloadReader>();
        workloadReader.ReadWorkloadAsync(Arg.Any<EmbeddingProfileFingerprint>(), Arg.Any<CancellationToken>())
            .Returns(EmbeddingWorkload.Nothing);

        var providerHealth = Substitute.For<IAiProviderHealthReader>();
        providerHealth.Read(Arg.Any<AiProviderRole>()).Returns(callInfo =>
            new AiProviderHealth(callInfo.Arg<AiProviderRole>(), AiProviderHealthState.Unobserved, ObservedAt: null));

        var ledger = Substitute.For<IEmbeddingSpendLedger>();
        ledger.ReadConsumedInputCharactersAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(0L);

        var timeProvider = new FakeTimeProvider(Now);
        var spendGate = new EmbeddingSpendGate(
            ledger,
            EmbeddingSpendBudget.Create(maxInputCharactersPerPeriod, TimeSpan.FromDays(1)),
            timeProvider);

        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Substitute.For<IPersistenceSession>());

        var retryPolicy = new OptimisticConcurrencyRetryPolicy(
            sessionFactory,
            new PersistenceConcurrencyOptions(),
            timeProvider);
        var vectorIndex = Substitute.For<IEmbeddingProfileVectorIndex>();

        return new EndpointWorld(
            generationStore,
            workloadReader,
            new CountedEmbeddingActivation(
                generationStore,
                workloadReader,
                spendGate,
                new EmbeddingProfileActivation(generationStore, vectorIndex, retryPolicy)),
            new EmbeddingStatusReader(generationStore, workloadReader, spendGate, providerHealth),
            new EmbeddingReindexCancellation(generationStore, vectorIndex, retryPolicy));
    }

    /// <summary>The ports one request runs against, and the three services the routes resolve.</summary>
    private sealed record EndpointWorld(
        IEmbeddingGenerationStore GenerationStore,
        IEmbeddingWorkloadReader WorkloadReader,
        CountedEmbeddingActivation Activation,
        EmbeddingStatusReader StatusReader,
        EmbeddingReindexCancellation Cancellation)
    {
        /// <summary>States what the deployment holds, which decides what an activation would do.</summary>
        internal void GenerationsAre(EmbeddingGenerations generations) =>
            this.GenerationStore.ReadGenerationsAsync(Arg.Any<CancellationToken>()).Returns(generations);

        /// <summary>States what every geometry still owes, which is what an activation is weighed as.</summary>
        internal void WorkloadIs(EmbeddingWorkload workload) => this.WorkloadReader
            .ReadWorkloadAsync(Arg.Any<EmbeddingProfileFingerprint>(), Arg.Any<CancellationToken>())
            .Returns(workload);

        /// <summary>States the row a registration produces, so the answer can be asserted against a known generation.</summary>
        internal void RegistersAs(RegisteredEmbeddingProfile registered) => this.GenerationStore
            .RegisterBuildingAsync(
                Arg.Any<IPersistenceSession>(),
                Arg.Any<EmbeddingProfileIdentity>(),
                Arg.Any<CancellationToken>())
            .Returns(registered);
    }
}
