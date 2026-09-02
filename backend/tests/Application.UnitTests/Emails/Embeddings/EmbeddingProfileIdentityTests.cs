// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Embeddings;

/// <summary>
/// Covers what a profile is allowed to claim about a vector space. The identity is written once and never revised, so a
/// value refused here is one that could otherwise describe stored vectors permanently and wrongly.
/// </summary>
public sealed class EmbeddingProfileIdentityTests
{
    /// <summary>A geometry is exactly what was declared, with nothing normalized on the way in.</summary>
    [Fact]
    public void Create_ADeclaredGeometry_KeepsEveryValueAsGiven()
    {
        // Arrange
        var preparation = EmbeddingInputPreparation.Create(8000, "Passage:", normalizesVector: true);

        // Act
        var identity = EmbeddingProfileIdentity.Create(
            "example-vendor",
            "text-embedding-3-small",
            "2026-01-01",
            1536,
            EmbeddingDistanceMetric.Cosine,
            preparation);

        // Assert
        Assert.Equal("example-vendor", identity.Provider);
        Assert.Equal("text-embedding-3-small", identity.ModelIdentifier);
        Assert.Equal("2026-01-01", identity.ModelVersion);
        Assert.Equal(1536, identity.Dimension);
        Assert.Equal(EmbeddingDistanceMetric.Cosine, identity.DistanceMetric);
        Assert.Same(preparation, identity.InputPreparation);
    }

    /// <summary>Most providers replace a model rather than version it, so an absent version is the ordinary case.</summary>
    [Fact]
    public void Create_AProviderThatVersionsNothing_RecordsNoVersion()
    {
        // Act
        var identity = Identity(modelVersion: null);

        // Assert
        Assert.Null(identity.ModelVersion);
    }

    /// <summary>A blank name identifies nothing, and would make two unrelated declarations fingerprint alike.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ABlankName_IsRefused(string blank)
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => Identity(provider: blank));
        Assert.Throws<ArgumentException>(() => Identity(modelIdentifier: blank));
        Assert.Throws<ArgumentException>(() => Identity(modelVersion: blank));
    }

    /// <summary>A name longer than its column would be refused by the database at the write instead of at the declaration.</summary>
    [Fact]
    public void Create_ANameLongerThanItsColumn_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() =>
            Identity(provider: new string('p', EmbeddingProfileIdentity.MaximumProviderLength + 1)));
        Assert.Throws<ArgumentException>(() =>
            Identity(modelIdentifier: new string('m', EmbeddingProfileIdentity.MaximumModelIdentifierLength + 1)));
        Assert.Throws<ArgumentException>(() =>
            Identity(modelVersion: new string('v', EmbeddingProfileIdentity.MaximumModelVersionLength + 1)));
    }

    /// <summary>A space has a width, and a profile claiming none would describe vectors that cannot exist.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_ADimensionThatIsNotPositive_IsRefused(int dimension)
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => Identity(dimension: dimension));
    }

    /// <summary>An identity without its preparation would claim a geometry that is only partly stated.</summary>
    [Fact]
    public void Create_MissingInputPreparation_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => EmbeddingProfileIdentity.Create(
            "example-vendor",
            "text-embedding-3-small",
            modelVersion: null,
            1536,
            EmbeddingDistanceMetric.Cosine,
            inputPreparation: null!));
    }

    private static EmbeddingProfileIdentity Identity(
        string provider = "example-vendor",
        string modelIdentifier = "text-embedding-3-small",
        string? modelVersion = "2026-01-01",
        int dimension = 1536,
        EmbeddingDistanceMetric distanceMetric = EmbeddingDistanceMetric.Cosine) =>
        EmbeddingProfileIdentity.Create(
            provider,
            modelIdentifier,
            modelVersion,
            dimension,
            distanceMetric,
            EmbeddingInputPreparation.Create(8000, passageInstruction: null, normalizesVector: true));
}
