// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Embeddings.Administration;
using MailFathom.Application.Emails.Embeddings.Backfill;
using MailFathom.Application.Emails.Embeddings.Limits;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Embeddings.Administration;

/// <summary>Covers the one read that says whether semantic search is working, and why it is not where it is not.</summary>
public sealed class EmbeddingStatusReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The answer this whole read exists for: a declaration edited into configuration takes effect at an activation and
    /// not before, so an operator who expected search results to change is told that nothing took it up.
    /// </summary>
    [Fact]
    public async Task ReadAsync_ADeclarationNobodyActivated_ReportsTheActivationAsOutstanding()
    {
        // Arrange
        var world = CreateWorld();
        world.GenerationStore.Add(CreateIdentity("the-old-model"), EmbeddingProfileLifecycleState.Active);

        // Act
        var status = await world.Reader.ReadAsync(CreateIdentity("a-newer-model"), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(status.ActivationOutstanding);
        Assert.NotNull(status.Serving);
    }

    /// <summary>A declaration whose generation is being built has been taken up, because the activation happened and the run is on.</summary>
    [Fact]
    public async Task ReadAsync_TheDeclarationBeingBuilt_ReportsNoOutstandingActivation()
    {
        // Arrange
        var world = CreateWorld();
        var declared = CreateIdentity("a-newer-model");
        world.GenerationStore.Add(CreateIdentity("the-old-model"), EmbeddingProfileLifecycleState.Active);
        world.GenerationStore.Add(declared, EmbeddingProfileLifecycleState.Building);

        // Act
        var status = await world.Reader.ReadAsync(declared, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(status.ActivationOutstanding);
        Assert.NotNull(status.Building);
    }

    /// <summary>An instance that declared no provider has nothing outstanding, because nothing was ever asked of it.</summary>
    [Fact]
    public async Task ReadAsync_NothingDeclared_ReportsNoDeclarationAndNothingOutstanding()
    {
        // Arrange
        var world = CreateWorld();

        // Act
        var status = await world.Reader.ReadAsync(declared: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(status.Declared);
        Assert.False(status.ActivationOutstanding);
        Assert.Null(status.Serving);
        Assert.Null(status.Building);
    }

    /// <summary>
    /// The two generations are counted apart, because "how much of the mailbox is searchable" and "how far the reindex
    /// has come" are different questions and answering both with one figure would report the new run's progress as the
    /// old generation's coverage.
    /// </summary>
    [Fact]
    public async Task ReadAsync_AReindexRunning_CountsEachGenerationAgainstItsOwnGeometry()
    {
        // Arrange
        var world = CreateWorld();
        var serving = CreateIdentity("the-old-model");
        var building = CreateIdentity("a-newer-model");
        world.GenerationStore.Add(serving, EmbeddingProfileLifecycleState.Active);
        world.GenerationStore.Add(building, EmbeddingProfileLifecycleState.Building);
        world.WorkloadReader.Set(serving, new EmbeddingWorkload(500, 0, 0, 0));
        world.WorkloadReader.Set(building, new EmbeddingWorkload(500, 320, 1_280, 128_000));

        // Act
        var status = await world.Reader.ReadAsync(building, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(500, status.Serving?.Workload.EmbeddedEmailCount);
        Assert.Equal(180, status.Building?.Workload.EmbeddedEmailCount);
        Assert.Equal(128_000, status.Building?.Workload.OutstandingCharacterCount);
    }

    /// <summary>What the provider last did and what the period has spent are part of the same answer, because either can be why nothing is being embedded.</summary>
    [Fact]
    public async Task ReadAsync_AProviderThatRefusedAndAPartlySpentPeriod_ReportsBothBesideTheGenerations()
    {
        // Arrange
        var world = CreateWorld(maxInputCharactersPerPeriod: 100_000);
        world.ProviderHealth.Read(AiProviderRole.Embedding).Returns(
            new AiProviderHealth(AiProviderRole.Embedding, AiProviderHealthState.Misconfigured, Now));
        world.Ledger.Seed(new DateTimeOffset(2026, 8, 8, 0, 0, 0, TimeSpan.Zero), 40_000);

        // Act
        var status = await world.Reader.ReadAsync(CreateIdentity("a-model"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(AiProviderHealthState.Misconfigured, status.ProviderHealth.State);
        Assert.Equal(40_000, status.Period.ConsumedInputCharacterCount);
        Assert.Equal(60_000, status.Period.RemainingInputCharacterCount);
    }

    /// <summary>
    /// The reading that tells a deployment which is waiting apart from one which is failing. Everything else in this
    /// answer reads the same during a pause between passes as it does on a broken instance: nothing serving, no vector
    /// written, and a provider nothing has been asked of.
    /// </summary>
    [Fact]
    public async Task ReadAsync_ABackfillPassScheduled_ReportsWhenItIsDue()
    {
        // Arrange
        var world = CreateWorld();
        world.BackfillSchedule.BringForward();

        // Act
        var status = await world.Reader.ReadAsync(CreateIdentity("a-model"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(Now, status.NextBackfillPassDueAt);
    }

    /// <summary>An instance whose backfill worker has scheduled nothing says so, which is what a disabled walk looks like.</summary>
    [Fact]
    public async Task ReadAsync_NoBackfillPassScheduled_ReportsNone()
    {
        // Arrange
        var world = CreateWorld();

        // Act
        var status = await world.Reader.ReadAsync(CreateIdentity("a-model"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(status.NextBackfillPassDueAt);
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
    public async Task ReadAsync_ACallerGrantedOnlyTheSpend_IsRefusedWithTheTransportAbsent()
    {
        // Arrange
        var world = CreateWorld(authorization: AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminSpend));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            world.Reader.ReadAsync(CreateIdentity("a-model"), TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminRead, refusal.RequiredPermission);
    }

    private static StatusWorld CreateWorld(
        long maxInputCharactersPerPeriod = 1_000_000,
        AccessAuthorization? authorization = null)
    {
        var generationStore = new InMemoryEmbeddingGenerationStore(new InMemoryEmailEmbeddingStore());
        var workloadReader = new InMemoryEmbeddingWorkloadReader();
        var ledger = new InMemoryEmbeddingSpendLedger();
        var providerHealth = Substitute.For<IAiProviderHealthReader>();
        providerHealth.Read(Arg.Any<AiProviderRole>()).Returns(callInfo =>
            new AiProviderHealth(callInfo.Arg<AiProviderRole>(), AiProviderHealthState.Unobserved, ObservedAt: null));

        var backfillSchedule = new EmbeddingBackfillSchedule(new FakeTimeProvider(Now));
        var reader = new EmbeddingStatusReader(
            generationStore,
            workloadReader,
            new EmbeddingSpendGate(
                ledger,
                EmbeddingSpendBudget.Create(maxInputCharactersPerPeriod, TimeSpan.FromDays(1)),
                new FakeTimeProvider(Now)),
            providerHealth,
            backfillSchedule,
            authorization ?? AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminRead));

        return new StatusWorld(generationStore, workloadReader, ledger, providerHealth, backfillSchedule, reader);
    }

    /// <summary>The state and the collaborators one status answer is composed from.</summary>
    private sealed record StatusWorld(
        InMemoryEmbeddingGenerationStore GenerationStore,
        InMemoryEmbeddingWorkloadReader WorkloadReader,
        InMemoryEmbeddingSpendLedger Ledger,
        IAiProviderHealthReader ProviderHealth,
        EmbeddingBackfillSchedule BackfillSchedule,
        EmbeddingStatusReader Reader);
}
