// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.UnitTests.Agent.Search;
using MailFathom.Application.UnitTests.TestDoubles;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Search;

/// <summary>Covers placing several texts at once, which the history search's embedding of a turn is the first caller to do.</summary>
/// <remarks>When text is placed at all, and which capability is reported, is covered through <see cref="SemanticEmailSearchTests" />.</remarks>
public sealed class ActiveEmbeddingSpaceTests
{
    /// <summary>More texts than one call accepts are sent in as many calls as the generator's bound requires, in order.</summary>
    [Fact]
    public async Task PlaceAsync_MoreTextsThanOneCallAccepts_PlacesThemAllAcrossSeveralCalls()
    {
        // Arrange
        var generator = new ScriptedTextEmbeddingGenerator(EmbeddingSpaceExample.Profile.Identity, maximumPassagesPerCall: 2);

        // Act
        var placement = await EmbeddingSpaceExample.Serving(generator).PlaceAsync(["one", "two", "three"], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([["one", "two"], ["three"]], generator.RequestedBatches);
        Assert.Equal(3, placement.Vectors!.Count);
        Assert.Equal(EmbeddingSpaceExample.Profile, placement.Profile);
    }

    /// <summary>A call failing part-way leaves nothing placed rather than some of the texts, which a caller could not reconcile.</summary>
    [Fact]
    public async Task PlaceAsync_ALaterCallFails_PlacesNothingAndReportsDegraded()
    {
        // Arrange
        var generator = new ScriptedTextEmbeddingGenerator(EmbeddingSpaceExample.Profile.Identity, maximumPassagesPerCall: 2)
        {
            Failure = EmbeddingGenerationFailure.RateLimited,
            FailingCallNumber = 2,
        };

        // Act
        var placement = await EmbeddingSpaceExample.Serving(generator).PlaceAsync(["one", "two", "three"], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SemanticSearchCapability.Degraded, placement.Capability);
        Assert.Null(placement.Vectors);
        Assert.Null(placement.Profile);
    }
}
