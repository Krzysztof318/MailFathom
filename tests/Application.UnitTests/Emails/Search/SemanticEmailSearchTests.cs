// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.UnitTests.TestDoubles;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Search;

/// <summary>Covers when a search is ranked semantically at all, which is the whole of what this type decides.</summary>
public sealed class SemanticEmailSearchTests
{
    private static readonly DateTimeOffset FirstJuly = new(2026, 7, 1, 8, 0, 0, TimeSpan.Zero);

    private static readonly EmbeddingProfileId ProfileId =
        EmbeddingProfileId.Create(new Guid("0f9d6b0b-2f1e-4c2a-9a3d-7c8e5f4a1b20"));

    /// <summary>Serving lexical search alone is a supported deployment rather than a degraded one.</summary>
    [Fact]
    public async Task FindNearestCandidatesAsync_NoEmbeddingProviderConfigured_ReportsNoSemanticRanking()
    {
        // Arrange
        var vectorIndex = new InMemoryEmailVectorSearchIndex().With(SyntheticEmailSummaries.Create(FirstJuly), 0.1f);
        var search = new SemanticEmailSearch(
            ProfileReaderReturning(new ActiveEmbeddingProfile(ProfileId, Identity())),
            vectorIndex,
            textEmbeddingGenerator: null);

        // Act
        var candidates = await FindAsync(search);

        // Assert
        Assert.Null(candidates);
        Assert.Empty(vectorIndex.Calls);
    }

    /// <summary>An instance that has activated nothing has no space to measure a distance in.</summary>
    [Fact]
    public async Task FindNearestCandidatesAsync_NoActiveProfile_ReportsNoSemanticRanking()
    {
        // Arrange
        var vectorIndex = new InMemoryEmailVectorSearchIndex();
        var search = new SemanticEmailSearch(
            ProfileReaderReturning(null),
            vectorIndex,
            new ScriptedTextEmbeddingGenerator(Identity(), maximumPassagesPerCall: 8));

        // Act
        var candidates = await FindAsync(search);

        // Assert
        Assert.Null(candidates);
        Assert.Empty(vectorIndex.Calls);
    }

    /// <summary>A generator of another space would place the query where the stored vectors do not live.</summary>
    [Fact]
    public async Task FindNearestCandidatesAsync_GeneratorDisagreeingWithTheProfile_ReportsNoSemanticRanking()
    {
        // Arrange
        var vectorIndex = new InMemoryEmailVectorSearchIndex();
        var search = new SemanticEmailSearch(
            ProfileReaderReturning(new ActiveEmbeddingProfile(ProfileId, Identity())),
            vectorIndex,
            new ScriptedTextEmbeddingGenerator(Identity("another-model"), maximumPassagesPerCall: 8));

        // Act
        var candidates = await FindAsync(search);

        // Assert
        Assert.Null(candidates);
        Assert.Empty(vectorIndex.Calls);
    }

    /// <summary>A provider outage costs one call its second ranking rather than turning a mailbox search into an error.</summary>
    [Fact]
    public async Task FindNearestCandidatesAsync_ProviderFailure_ReportsNoSemanticRankingRatherThanRaising()
    {
        // Arrange
        var vectorIndex = new InMemoryEmailVectorSearchIndex();
        var generator = new ScriptedTextEmbeddingGenerator(Identity(), maximumPassagesPerCall: 8)
        {
            Failure = EmbeddingGenerationFailure.RateLimited,
        };
        var search = new SemanticEmailSearch(
            ProfileReaderReturning(new ActiveEmbeddingProfile(ProfileId, Identity())),
            vectorIndex,
            generator);

        // Act
        var candidates = await FindAsync(search);

        // Assert
        Assert.Null(candidates);
        Assert.Empty(vectorIndex.Calls);
    }

    /// <summary>The query itself is what is embedded, and it reaches the index under the profile both sides belong to.</summary>
    [Fact]
    public async Task FindNearestCandidatesAsync_HealthyProfile_EmbedsTheQueryAndRanksUnderThatProfile()
    {
        // Arrange
        var nearest = SyntheticEmailSummaries.Create(FirstJuly);
        var vectorIndex = new InMemoryEmailVectorSearchIndex().With(nearest, distance: 0.1f);
        var generator = new ScriptedTextEmbeddingGenerator(Identity(), maximumPassagesPerCall: 8);
        var search = new SemanticEmailSearch(
            ProfileReaderReturning(new ActiveEmbeddingProfile(ProfileId, Identity())),
            vectorIndex,
            generator);

        // Act
        var candidates = await FindAsync(search);

        // Assert
        Assert.Equal(nearest.StoredEmailId, Assert.Single(candidates!).StoredEmailId);
        Assert.Equal(["water damage"], Assert.Single(generator.RequestedBatches));
        Assert.Equal(ProfileId, Assert.Single(vectorIndex.Calls).Profile.Id);
    }

    /// <summary>A mailbox mid-backfill was ranked semantically and found nothing, which is not the same as never ranking.</summary>
    [Fact]
    public async Task FindNearestCandidatesAsync_NothingEmbeddedYet_ReportsAnEmptyRankingRatherThanNone()
    {
        // Arrange
        var search = new SemanticEmailSearch(
            ProfileReaderReturning(new ActiveEmbeddingProfile(ProfileId, Identity())),
            new InMemoryEmailVectorSearchIndex(),
            new ScriptedTextEmbeddingGenerator(Identity(), maximumPassagesPerCall: 8));

        // Act
        var candidates = await FindAsync(search);

        // Assert
        Assert.Empty(candidates!);
    }

    private static Task<IReadOnlyList<RankedEmailCandidate>?> FindAsync(SemanticEmailSearch search) =>
        search.FindNearestCandidatesAsync(
            UnfilteredSelection(),
            EmailSearchQueryText.Create("water damage"),
            limit: 20,
            TestContext.Current.CancellationToken);

    private static IActiveEmbeddingProfileReader ProfileReaderReturning(ActiveEmbeddingProfile? profile)
    {
        var reader = Substitute.For<IActiveEmbeddingProfileReader>();
        reader.FindActiveProfileAsync(Arg.Any<CancellationToken>()).Returns(profile);

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
        MailboxScope.Unrestricted,
        senderAddress: null,
        recipientAddress: null,
        subjectFragment: null,
        receivedOnOrAfter: null,
        receivedBefore: null,
        isRemotelySeen: null,
        hasAttachments: null);
}
