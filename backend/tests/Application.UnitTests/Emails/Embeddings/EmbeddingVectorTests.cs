// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Embeddings;

/// <summary>Covers the arithmetic a stored vector's meaning depends on.</summary>
public sealed class EmbeddingVectorTests
{
    [Fact]
    public void Create_Components_AreCopiedFromTheSource()
    {
        // Arrange
        var components = new[] { 0.6f, 0.8f };

        // Act
        var vector = EmbeddingVector.Create(components);
        components[0] = 99f;

        // Assert
        Assert.Equal(0.6f, vector.Components.Span[0]);
        Assert.Equal(2, vector.Dimension);
    }

    [Fact]
    public void Create_NoComponents_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => EmbeddingVector.Create(ReadOnlySpan<float>.Empty));
    }

    /// <summary>
    /// A non-finite component survives every distance operator as a result that is neither an error nor a number, so
    /// the chunk carrying one would silently stop being retrievable instead of failing.
    /// </summary>
    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void Create_ComponentThatIsNotAFiniteNumber_IsRefused(float component)
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => EmbeddingVector.Create([0.5f, component]));
    }

    [Fact]
    public void Normalize_ScalesToUnitLength()
    {
        // Arrange
        var vector = EmbeddingVector.Create([3f, 4f]);

        // Act
        var normalized = vector.Normalize();

        // Assert
        Assert.Equal(0.6f, normalized.Components.Span[0], 5);
        Assert.Equal(0.8f, normalized.Components.Span[1], 5);
    }

    /// <summary>Renormalizing what a provider already normalized would only add a rounding pass, so it is skipped.</summary>
    [Fact]
    public void Normalize_AlreadyOfUnitLength_ReturnsTheSameVector()
    {
        // Arrange
        var vector = EmbeddingVector.Create([0.6f, 0.8f]);

        // Act
        var normalized = vector.Normalize();

        // Assert
        Assert.Same(vector, normalized);
    }

    [Fact]
    public void Normalize_EveryComponentZero_IsRefused()
    {
        // Arrange
        var vector = EmbeddingVector.Create([0f, 0f]);

        // Act, Assert
        Assert.Throws<InvalidOperationException>(vector.Normalize);
    }

    /// <summary>
    /// Dropping the tail of a unit vector leaves one shorter than unit length, and a cosine distance between vectors of
    /// differing lengths is a number rather than an error. Renormalization is therefore part of shortening.
    /// </summary>
    [Fact]
    public void Shorten_NarrowsAndRestoresUnitLength()
    {
        // Arrange
        var vector = EmbeddingVector.Create([0.6f, 0.8f, 0f, 0f]).Normalize();

        // Act
        var shortened = vector.Shorten(2);

        // Assert
        Assert.Equal(2, shortened.Dimension);
        Assert.Equal(1d, Length(shortened), 5);
    }

    [Fact]
    public void Shorten_ToItsOwnWidth_ReturnsTheSameVector()
    {
        // Arrange
        var vector = EmbeddingVector.Create([0.6f, 0.8f]);

        // Act
        var shortened = vector.Shorten(2);

        // Assert
        Assert.Same(vector, shortened);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3)]
    public void Shorten_ToAWidthTheVectorCannotHave_IsRefused(int dimension)
    {
        // Arrange
        var vector = EmbeddingVector.Create([0.6f, 0.8f]);

        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => vector.Shorten(dimension));
    }

    private static double Length(EmbeddingVector vector) =>
        Math.Sqrt(vector.Components.ToArray().Sum(component => (double)component * component));
}
