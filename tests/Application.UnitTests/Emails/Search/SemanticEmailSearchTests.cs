// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.AiProviders;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.UnitTests.TestDoubles;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Search;

/// <summary>Covers when a search is ranked semantically at all, and which of the three capability states it reports.</summary>
public sealed class SemanticEmailSearchTests
{
    private static readonly DateTimeOffset FirstJuly = new(2026, 7, 1, 8, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Now = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    private static readonly EmbeddingProfileId ProfileId =
        EmbeddingProfileId.Create(new Guid("0f9d6b0b-2f1e-4c2a-9a3d-7c8e5f4a1b20"));

    /// <summary>Serving lexical search alone is a supported deployment rather than a degraded one.</summary>
    [Fact]
    public async Task FindNearestCandidatesAsync_NoActiveProfile_ReportsSemanticRetrievalInactive()
    {
        // Arrange
        var vectorIndex = new InMemoryEmailVectorSearchIndex();
        var search = SearchOver(
            vectorIndex,
            profile: null,
            generator: new ScriptedTextEmbeddingGenerator(Identity(), maximumPassagesPerCall: 8));

        // Act
        var outcome = await FindAsync(search);

        // Assert
        Assert.Equal(SemanticSearchCapability.Inactive, outcome.Capability);
        Assert.Null(outcome.Candidates);
        Assert.Empty(vectorIndex.Calls);
    }

    /// <summary>An instance that declared no provider and activated nothing is the ordinary lexical deployment.</summary>
    [Fact]
    public async Task FindNearestCandidatesAsync_NoProviderAndNoProfile_ReportsSemanticRetrievalInactive()
    {
        // Arrange
        var search = SearchOver(new InMemoryEmailVectorSearchIndex(), profile: null, generator: null);

        // Act
        var outcome = await FindAsync(search);

        // Assert
        Assert.Equal(SemanticSearchCapability.Inactive, outcome.Capability);
        Assert.Null(outcome.Candidates);
    }

    /// <summary>Vectors exist and nothing can place a query beside them, which is a state an operator has to settle.</summary>
    [Fact]
    public async Task FindNearestCandidatesAsync_ActiveProfileWithNoProviderDeclared_ReportsDegraded()
    {
        // Arrange
        var vectorIndex = new InMemoryEmailVectorSearchIndex().With(SyntheticEmailSummaries.Create(FirstJuly), 0.1f);
        var search = SearchOver(vectorIndex, ActiveProfile(), generator: null);

        // Act
        var outcome = await FindAsync(search);

        // Assert
        Assert.Equal(SemanticSearchCapability.Degraded, outcome.Capability);
        Assert.Null(outcome.Candidates);
        Assert.Empty(vectorIndex.Calls);
    }

    /// <summary>A generator of another space would place the query where the stored vectors do not live.</summary>
    [Fact]
    public async Task FindNearestCandidatesAsync_GeneratorDisagreeingWithTheProfile_ReportsDegraded()
    {
        // Arrange
        var vectorIndex = new InMemoryEmailVectorSearchIndex();
        var search = SearchOver(
            vectorIndex,
            ActiveProfile(),
            new ScriptedTextEmbeddingGenerator(Identity("another-model"), maximumPassagesPerCall: 8));

        // Act
        var outcome = await FindAsync(search);

        // Assert
        Assert.Equal(SemanticSearchCapability.Degraded, outcome.Capability);
        Assert.Null(outcome.Candidates);
        Assert.Empty(vectorIndex.Calls);
    }

    /// <summary>A provider outage costs one call its second ranking rather than turning a mailbox search into an error.</summary>
    [Fact]
    public async Task FindNearestCandidatesAsync_ProviderFailure_ReportsDegradedRatherThanRaising()
    {
        // Arrange
        var vectorIndex = new InMemoryEmailVectorSearchIndex();
        var generator = new ScriptedTextEmbeddingGenerator(Identity(), maximumPassagesPerCall: 8)
        {
            Failure = EmbeddingGenerationFailure.RateLimited,
        };
        var search = SearchOver(vectorIndex, ActiveProfile(), generator);

        // Act
        var outcome = await FindAsync(search);

        // Assert
        Assert.Equal(SemanticSearchCapability.Degraded, outcome.Capability);
        Assert.Null(outcome.Candidates);
        Assert.Empty(vectorIndex.Calls);
    }

    /// <summary>
    /// A provider already known to be refusing is not asked again by a search. This is what keeps a failing provider from
    /// being polled once more per query, and what makes the degraded state cost nothing to report.
    /// </summary>
    [Theory]
    [InlineData(AiProviderHealthState.Unavailable)]
    [InlineData(AiProviderHealthState.Misconfigured)]
    public async Task FindNearestCandidatesAsync_ProviderAlreadyUnhealthy_ReportsDegradedWithoutCallingIt(
        AiProviderHealthState state)
    {
        // Arrange
        var generator = new ScriptedTextEmbeddingGenerator(Identity(), maximumPassagesPerCall: 8);
        var search = SearchOver(new InMemoryEmailVectorSearchIndex(), ActiveProfile(), generator, state);

        // Act
        var outcome = await FindAsync(search);

        // Assert
        Assert.Equal(SemanticSearchCapability.Degraded, outcome.Capability);
        Assert.Null(outcome.Candidates);
        Assert.Empty(generator.RequestedBatches);
    }

    /// <summary>The query itself is what is embedded, and it reaches the index under the profile both sides belong to.</summary>
    [Fact]
    public async Task FindNearestCandidatesAsync_HealthyProfile_EmbedsTheQueryAndRanksUnderThatProfile()
    {
        // Arrange
        var nearest = SyntheticEmailSummaries.Create(FirstJuly);
        var vectorIndex = new InMemoryEmailVectorSearchIndex().With(nearest, distance: 0.1f);
        var generator = new ScriptedTextEmbeddingGenerator(Identity(), maximumPassagesPerCall: 8);
        var search = SearchOver(vectorIndex, ActiveProfile(), generator);

        // Act
        var outcome = await FindAsync(search);

        // Assert
        Assert.Equal(SemanticSearchCapability.Available, outcome.Capability);
        Assert.Equal(nearest.StoredEmailId, Assert.Single(outcome.Candidates!).StoredEmailId);
        Assert.Equal(["water damage"], Assert.Single(generator.RequestedBatches));
        Assert.Equal(ProfileId, Assert.Single(vectorIndex.Calls).Profile.Id);
    }

    /// <summary>A mailbox mid-backfill was ranked semantically and found nothing, which is not the same as never ranking.</summary>
    [Fact]
    public async Task FindNearestCandidatesAsync_NothingEmbeddedYet_ReportsAnEmptyRankingRatherThanNone()
    {
        // Arrange
        var search = SearchOver(
            new InMemoryEmailVectorSearchIndex(),
            ActiveProfile(),
            new ScriptedTextEmbeddingGenerator(Identity(), maximumPassagesPerCall: 8));

        // Act
        var outcome = await FindAsync(search);

        // Assert
        Assert.Equal(SemanticSearchCapability.Available, outcome.Capability);
        Assert.Empty(outcome.Candidates!);
    }

    /// <summary>Recovery needs no restart: the state the workers' own calls wrote is what the next search reads.</summary>
    [Fact]
    public async Task FindNearestCandidatesAsync_ProviderHealthyAgain_RanksSemanticallyWithoutARestart()
    {
        // Arrange
        var nearest = SyntheticEmailSummaries.Create(FirstJuly);
        var vectorIndex = new InMemoryEmailVectorSearchIndex().With(nearest, distance: 0.1f);
        var generator = new ScriptedTextEmbeddingGenerator(Identity(), maximumPassagesPerCall: 8);
        var health = HealthReaderReporting(AiProviderHealthState.Misconfigured, Now);
        var search = new SemanticEmailSearch(
            ProfileReaderReturning(ActiveProfile()),
            vectorIndex,
            health,
            new FakeTimeProvider(Now),
            generator);

        var whileUnhealthy = await FindAsync(search);

        // Act
        health.Read(AiProviderRole.Embedding)
            .Returns(new AiProviderHealth(AiProviderRole.Embedding, AiProviderHealthState.Serving, Now));

        var afterRecovery = await FindAsync(search);

        // Assert
        Assert.Equal(SemanticSearchCapability.Degraded, whileUnhealthy.Capability);
        Assert.Equal(SemanticSearchCapability.Available, afterRecovery.Capability);
        Assert.Equal(nearest.StoredEmailId, Assert.Single(afterRecovery.Candidates!).StoredEmailId);
    }

    /// <summary>
    /// The gate is a window rather than a latch. Nothing else is guaranteed to call the provider — an instance whose
    /// mail is fully embedded makes none — so a recorded failure that nobody refreshed must not withhold retrieval
    /// forever after the cause is gone.
    /// </summary>
    [Fact]
    public async Task FindNearestCandidatesAsync_AFailureOlderThanTheRecheckInterval_LetsOneQueryThrough()
    {
        // Arrange
        var nearest = SyntheticEmailSummaries.Create(FirstJuly);
        var vectorIndex = new InMemoryEmailVectorSearchIndex().With(nearest, distance: 0.1f);
        var generator = new ScriptedTextEmbeddingGenerator(Identity(), maximumPassagesPerCall: 8);
        var search = new SemanticEmailSearch(
            ProfileReaderReturning(ActiveProfile()),
            vectorIndex,
            HealthReaderReporting(AiProviderHealthState.Misconfigured, Now.AddHours(-1)),
            new FakeTimeProvider(Now),
            generator);

        // Act
        var outcome = await FindAsync(search);

        // Assert
        Assert.Equal(SemanticSearchCapability.Available, outcome.Capability);
        Assert.Equal(nearest.StoredEmailId, Assert.Single(outcome.Candidates!).StoredEmailId);
        Assert.Single(generator.RequestedBatches);
    }

    /// <summary>A stale observation admits a call, and a provider still refusing costs that one call and no more.</summary>
    [Fact]
    public async Task FindNearestCandidatesAsync_AStaleFailureAndAProviderStillRefusing_StaysDegraded()
    {
        // Arrange
        var generator = new ScriptedTextEmbeddingGenerator(Identity(), maximumPassagesPerCall: 8)
        {
            Failure = EmbeddingGenerationFailure.CredentialRejected,
        };
        var search = new SemanticEmailSearch(
            ProfileReaderReturning(ActiveProfile()),
            new InMemoryEmailVectorSearchIndex(),
            HealthReaderReporting(AiProviderHealthState.Misconfigured, Now.AddHours(-1)),
            new FakeTimeProvider(Now),
            generator);

        // Act
        var outcome = await FindAsync(search);

        // Assert
        Assert.Equal(SemanticSearchCapability.Degraded, outcome.Capability);
        Assert.Null(outcome.Candidates);
        Assert.Single(generator.RequestedBatches);
    }

    /// <summary>Reporting reads the recorded state whatever its age, because it makes no call that could establish a newer one.</summary>
    [Fact]
    public async Task ReadCapabilityAsync_AFailureOlderThanTheRecheckInterval_StillReportsDegraded()
    {
        // Arrange
        var search = new SemanticEmailSearch(
            ProfileReaderReturning(ActiveProfile()),
            new InMemoryEmailVectorSearchIndex(),
            HealthReaderReporting(AiProviderHealthState.Unavailable, Now.AddHours(-1)),
            new FakeTimeProvider(Now),
            new ScriptedTextEmbeddingGenerator(Identity(), maximumPassagesPerCall: 8));

        // Act
        var capability = await search.ReadCapabilityAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SemanticSearchCapability.Degraded, capability);
    }

    /// <summary>The capability an empty search reports is the deployment's own, established without spending a provider call.</summary>
    [Fact]
    public async Task ReadCapabilityAsync_AnActiveProfileAndAServingProvider_ReportsAvailableWithoutEmbeddingAnything()
    {
        // Arrange
        var generator = new ScriptedTextEmbeddingGenerator(Identity(), maximumPassagesPerCall: 8);
        var search = SearchOver(new InMemoryEmailVectorSearchIndex(), ActiveProfile(), generator);

        // Act
        var capability = await search.ReadCapabilityAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SemanticSearchCapability.Available, capability);
        Assert.Empty(generator.RequestedBatches);
    }

    private static Task<SemanticEmailSearchOutcome> FindAsync(SemanticEmailSearch search) =>
        search.FindNearestCandidatesAsync(
            UnfilteredSelection(),
            EmailSearchQueryText.Create("water damage"),
            limit: 20,
            TestContext.Current.CancellationToken);

    private static SemanticEmailSearch SearchOver(
        InMemoryEmailVectorSearchIndex vectorIndex,
        RegisteredEmbeddingProfile? profile,
        ScriptedTextEmbeddingGenerator? generator,
        AiProviderHealthState providerState = AiProviderHealthState.Serving) => new(
        ProfileReaderReturning(profile),
        vectorIndex,
        HealthReaderReporting(providerState, Now),
        new FakeTimeProvider(Now),
        generator);

    private static RegisteredEmbeddingProfile ActiveProfile() => new(ProfileId, Identity());

    private static IActiveEmbeddingProfileReader ProfileReaderReturning(RegisteredEmbeddingProfile? profile)
    {
        var reader = Substitute.For<IActiveEmbeddingProfileReader>();
        reader.FindActiveProfileAsync(Arg.Any<CancellationToken>()).Returns(profile);

        return reader;
    }

    private static IAiProviderHealthReader HealthReaderReporting(
        AiProviderHealthState state,
        DateTimeOffset? observedAt)
    {
        var reader = Substitute.For<IAiProviderHealthReader>();
        reader.Read(Arg.Any<AiProviderRole>())
            .Returns(call => new AiProviderHealth(call.Arg<AiProviderRole>(), state, observedAt));

        return reader;
    }

    private static EmbeddingProfileIdentity Identity(string modelIdentifier = "a-model") =>
        EmbeddingProfileIdentity.Create(
            "a-provider",
            modelIdentifier,
            modelVersion: null,
            dimension: 8,
            EmbeddingDistanceMetric.Cosine,
            EmbeddingInputPreparation.Create(2_000, passageInstruction: null, normalizesVector: true));

    private static MailboxEmailSelection UnfilteredSelection() => MailboxEmailSelection.Create(
        MailboxScope.NothingReadable,
        senderAddress: null,
        recipientAddress: null,
        subjectFragment: null,
        receivedOnOrAfter: null,
        receivedBefore: null,
        isRemotelySeen: null,
        hasAttachments: null);
}
