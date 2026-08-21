// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Embeddings.Administration;
using MailFathom.Application.Emails.Embeddings.Backfill;
using MailFathom.Application.Emails.Embeddings.Generations;
using MailFathom.Application.Emails.Embeddings.Indexing;
using MailFathom.Application.Emails.Embeddings.Limits;
using MailFathom.Application.Persistence;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Embeddings.Administration;

/// <summary>Covers the counting and the refusal an operator meets before a provider is ever called.</summary>
public sealed class CountedEmbeddingActivationTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    /// <summary>An instance embedding nothing yet is told it would start a reindex, and what that reindex would send.</summary>
    [Fact]
    public async Task AssessAsync_NothingActivated_ForecastsAReindexAndReportsWhatItWouldSend()
    {
        // Arrange
        var world = CreateWorld();
        var declared = CreateIdentity("a-model");
        world.WorkloadReader.Set(declared, new EmbeddingWorkload(120, 120, 400, 40_000));

        // Act
        var assessment = await world.Activation.AssessAsync(declared, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmbeddingActivationForecast.WouldStartReindex, assessment.Forecast);
        Assert.Equal(400, assessment.Estimate.OutstandingPassageCount);
        Assert.Equal(40_000, assessment.Estimate.OutstandingCharacterCount);
        Assert.Equal(10_000, assessment.Estimate.ApproximateTokenCount);
        Assert.False(assessment.ExceedsSpendCeiling);
    }

    /// <summary>
    /// The estimate is counted against the geometry rather than against whatever is serving, which is what keeps a
    /// rollback to a model whose vectors are still there from being priced as a first activation.
    /// </summary>
    [Fact]
    public async Task AssessAsync_ADeclarationWithVectorsStillStored_CountsAgainstThatGeometryRatherThanTheWholeMailbox()
    {
        // Arrange
        var world = CreateWorld();
        var declared = CreateIdentity("the-previous-model");
        world.GenerationStore.Add(CreateIdentity("the-current-model"), EmbeddingProfileLifecycleState.Active);
        world.WorkloadReader.Unarranged = new EmbeddingWorkload(500, 500, 2_000, 200_000);
        world.WorkloadReader.Set(declared, new EmbeddingWorkload(500, 40, 160, 16_000));

        // Act
        var assessment = await world.Activation.AssessAsync(declared, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(160, assessment.Estimate.OutstandingPassageCount);
        Assert.Equal(16_000, assessment.Estimate.OutstandingCharacterCount);
    }

    /// <summary>The three forecasts that spend nothing are told apart, because each leaves the operator a different next move.</summary>
    [Theory]
    [InlineData(EmbeddingProfileLifecycleState.Active, EmbeddingActivationForecast.AlreadyServing)]
    [InlineData(EmbeddingProfileLifecycleState.Building, EmbeddingActivationForecast.WouldResumeReindex)]
    public async Task AssessAsync_TheDeclarationAlreadyRegistered_ForecastsWhatThatGenerationIsDoing(
        EmbeddingProfileLifecycleState lifecycleState,
        EmbeddingActivationForecast expected)
    {
        // Arrange
        var world = CreateWorld();
        var declared = CreateIdentity("a-model");
        world.GenerationStore.Add(declared, lifecycleState);

        // Act
        var assessment = await world.Activation.AssessAsync(declared, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(expected, assessment.Forecast);
    }

    /// <summary>One reindex runs at a time, so a declaration arriving while a different one is being built is forecast as refused.</summary>
    [Fact]
    public async Task AssessAsync_ADifferentReindexRunning_ForecastsThatRatherThanAStart()
    {
        // Arrange
        var world = CreateWorld();
        world.GenerationStore.Add(CreateIdentity("a-different-model"), EmbeddingProfileLifecycleState.Building);

        // Act
        var assessment = await world.Activation.AssessAsync(
            CreateIdentity("a-model"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmbeddingActivationForecast.DifferentReindexRunning, assessment.Forecast);
    }

    /// <summary>
    /// A budget that only slowed a run down would be a schedule, so an estimate above the ceiling is refused before a
    /// profile row is written rather than started and throttled.
    /// </summary>
    [Fact]
    public async Task ActivateAsync_AnEstimateAboveTheCeiling_RefusesAndRegistersNothing()
    {
        // Arrange
        var world = CreateWorld(maxInputCharactersPerPeriod: 10_000);
        var declared = CreateIdentity("a-model");
        world.WorkloadReader.Set(declared, new EmbeddingWorkload(500, 500, 2_000, 200_000));

        // Act
        var result = await world.Activation.ActivateAsync(declared, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.RefusedBySpendCeiling);
        Assert.Null(result.Activation);
        Assert.Equal(200_000, result.Assessment.Estimate.OutstandingCharacterCount);
        Assert.Equal(10_000, result.Assessment.Period.CeilingInputCharacterCount);

        var generations = await world.GenerationStore.ReadGenerationsAsync(TestContext.Current.CancellationToken);
        Assert.Null(generations.Building);
    }

    /// <summary>
    /// The ceiling is weighed against what one period admits rather than against what this period has left, so the same
    /// activation does not succeed in the morning and fail in the afternoon.
    /// </summary>
    [Fact]
    public async Task ActivateAsync_APartlySpentPeriodStillLeavingTheCeilingAboveTheEstimate_Activates()
    {
        // Arrange
        var world = CreateWorld(maxInputCharactersPerPeriod: 100_000);
        var declared = CreateIdentity("a-model");
        world.WorkloadReader.Set(declared, new EmbeddingWorkload(500, 500, 2_000, 90_000));
        world.Ledger.Seed(new DateTimeOffset(2026, 8, 8, 0, 0, 0, TimeSpan.Zero), 80_000);

        // Act
        var result = await world.Activation.ActivateAsync(declared, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.RefusedBySpendCeiling);
        Assert.Equal(EmbeddingProfileActivationOutcome.ReindexStarted, result.Activation?.Outcome);
    }

    /// <summary>A deployment that declared no ceiling refuses nothing, however large the mailbox it is about to embed.</summary>
    [Fact]
    public async Task ActivateAsync_NoCeilingDeclared_ActivatesWhateverTheEstimateIs()
    {
        // Arrange
        var world = CreateWorld(maxInputCharactersPerPeriod: 0);
        var declared = CreateIdentity("a-model");
        world.WorkloadReader.Set(declared, new EmbeddingWorkload(50_000, 50_000, 400_000, 900_000_000));

        // Act
        var result = await world.Activation.ActivateAsync(declared, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.RefusedBySpendCeiling);
        Assert.Equal(EmbeddingProfileActivationOutcome.ReindexStarted, result.Activation?.Outcome);
    }

    /// <summary>
    /// Re-activating what already serves spends nothing this command decided on, so a ceiling smaller than the mail
    /// still outstanding must not turn reading the deployment's own state into a refusal.
    /// </summary>
    [Fact]
    public async Task ActivateAsync_TheDeclarationAlreadyServingAndTheCeilingSmall_ReportsItRatherThanRefusing()
    {
        // Arrange
        var world = CreateWorld(maxInputCharactersPerPeriod: 10);
        var declared = CreateIdentity("a-model");
        world.GenerationStore.Add(declared, EmbeddingProfileLifecycleState.Active);
        world.WorkloadReader.Set(declared, new EmbeddingWorkload(500, 20, 80, 8_000));

        // Act
        var result = await world.Activation.ActivateAsync(declared, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.RefusedBySpendCeiling);
        Assert.Equal(EmbeddingProfileActivationOutcome.AlreadyServing, result.Activation?.Outcome);
    }

    /// <summary>What was weighed travels with what happened, so an operator recognizes the figure they confirmed.</summary>
    [Fact]
    public async Task ActivateAsync_AnActivationThatRan_ReportsTheEstimateItWasWeighedAs()
    {
        // Arrange
        var world = CreateWorld();
        var declared = CreateIdentity("a-model");
        world.WorkloadReader.Set(declared, new EmbeddingWorkload(120, 120, 400, 40_000));

        // Act
        var result = await world.Activation.ActivateAsync(declared, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(400, result.Assessment.Estimate.OutstandingPassageCount);
        Assert.Equal(EmbeddingActivationForecast.WouldStartReindex, result.Assessment.Forecast);
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
    public async Task AssessAsync_ACallerGrantedOnlyTheSpend_IsRefusedWithTheTransportAbsent()
    {
        // Arrange
        var world = CreateWorld(authorization: AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminSpend));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            world.Activation.AssessAsync(CreateIdentity("a-model"), TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminRead, refusal.RequiredPermission);
    }

    /// <summary>The read in front of the activation is the activating caller's own, so holding the spend alone is enough to activate.</summary>
    [Fact]
    public async Task ActivateAsync_ACallerGrantedOnlyTheSpend_ActivatesWithoutAlsoHoldingTheRead()
    {
        // Arrange
        var world = CreateWorld(authorization: AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminSpend));
        var declared = CreateIdentity("a-model");

        // Act
        var result = await world.Activation.ActivateAsync(declared, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.RefusedBySpendCeiling);
        Assert.Equal(EmbeddingProfileActivationOutcome.ReindexStarted, result.Activation?.Outcome);
    }

    /// <summary>Starting a provider bill asks for the one permission allocated to spending, and the administrative read does not carry it.</summary>
    [Fact]
    public async Task ActivateAsync_ACallerGrantedOnlyTheRead_IsRefusedWithTheTransportAbsent()
    {
        // Arrange
        var world = CreateWorld(authorization: AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminRead));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            world.Activation.ActivateAsync(CreateIdentity("a-model"), TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminSpend, refusal.RequiredPermission);
    }

    private static ActivationWorld CreateWorld(
        long maxInputCharactersPerPeriod = 1_000_000,
        AccessAuthorization? authorization = null)
    {
        var generationStore = new InMemoryEmbeddingGenerationStore(new InMemoryEmailEmbeddingStore());
        var workloadReader = new InMemoryEmbeddingWorkloadReader();
        var ledger = new InMemoryEmbeddingSpendLedger();
        var timeProvider = new FakeTimeProvider(Now);

        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Substitute.For<IPersistenceSession>());

        var activation = new CountedEmbeddingActivation(
            generationStore,
            workloadReader,
            new EmbeddingSpendGate(
                ledger,
                EmbeddingSpendBudget.Create(maxInputCharactersPerPeriod, TimeSpan.FromDays(1)),
                timeProvider),
            new EmbeddingProfileActivation(
                generationStore,
                Substitute.For<IEmbeddingProfileVectorIndex>(),
                new OptimisticConcurrencyRetryPolicy(
                    sessionFactory,
                    new PersistenceConcurrencyOptions(),
                    timeProvider),
                new EmbeddingBackfillSchedule(timeProvider)),
            authorization ?? AccessAuthorizations.ForCallerGranted(
                MailFathomPermission.AdminRead,
                MailFathomPermission.AdminSpend));

        return new ActivationWorld(generationStore, workloadReader, ledger, activation);
    }

    /// <summary>The state and the collaborators one counted activation works against.</summary>
    private sealed record ActivationWorld(
        InMemoryEmbeddingGenerationStore GenerationStore,
        InMemoryEmbeddingWorkloadReader WorkloadReader,
        InMemoryEmbeddingSpendLedger Ledger,
        CountedEmbeddingActivation Activation);
}
