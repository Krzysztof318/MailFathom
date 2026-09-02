// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Embeddings;
using MailFathom.Application.Emails.Embeddings;
using Xunit;

namespace MailFathom.AI.UnitTests.Embeddings;

/// <summary>Covers the generator everything downstream of the port is provable against at zero provider cost.</summary>
public sealed class DeterministicTextEmbeddingGeneratorTests
{
    private const int Dimension = 32;

    private readonly DeterministicTextEmbeddingGenerator generator = new(Dimension, inputCharacterLimit: 64);

    /// <summary>An idempotent write and a re-run that changes nothing are only testable if a passage embeds to one vector.</summary>
    [Fact]
    public async Task GenerateAsync_TheSamePassageTwice_ProducesTheSameVector()
    {
        // Act
        var first = await this.generator.GenerateAsync(["a quarterly invoice"], TestContext.Current.CancellationToken);
        var second = await this.generator.GenerateAsync(["a quarterly invoice"], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(first[0].Components.ToArray(), second[0].Components.ToArray());
    }

    [Fact]
    public async Task GenerateAsync_DifferentPassages_ProduceDifferentVectors()
    {
        // Act
        var vectors = await this.generator.GenerateAsync(
            ["a quarterly invoice", "a delivery notice"],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(vectors[0].Components.ToArray(), vectors[1].Components.ToArray());
    }

    /// <summary>The batch's order is the caller's mapping from chunk to vector, so it has to survive the call.</summary>
    [Fact]
    public async Task GenerateAsync_ABatch_AnswersOneVectorPerPassageInOrder()
    {
        // Arrange
        var passages = Enumerable.Range(0, 5).Select(index => $"passage {index}").ToArray();

        // Act
        var vectors = await this.generator.GenerateAsync(passages, TestContext.Current.CancellationToken);
        var singly = await Task.WhenAll(passages.Select(async passage =>
            (await this.generator.GenerateAsync([passage], TestContext.Current.CancellationToken))[0]));

        // Assert
        Assert.Equal(
            singly.Select(vector => vector.Components.ToArray()),
            vectors.Select(vector => vector.Components.ToArray()));
    }

    [Fact]
    public async Task GenerateAsync_EveryVector_HasTheDeclaredWidthAndUnitLength()
    {
        // Act
        var vectors = await this.generator.GenerateAsync(["a quarterly invoice"], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(Dimension, vectors[0].Dimension);
        Assert.Equal(1d, Length(vectors[0]), 5);
    }

    /// <summary>The profile row is where a deployment that activated a hash by accident becomes visible.</summary>
    [Fact]
    public void Identity_NamesAProviderNoVendorPublishes()
    {
        // Assert
        Assert.Equal(DeterministicTextEmbeddingGenerator.ProviderName, this.generator.Identity.Provider);
        Assert.Equal(DeterministicTextEmbeddingGenerator.ModelName, this.generator.Identity.ModelIdentifier);
        Assert.Equal(Dimension, this.generator.Identity.Dimension);
    }

    /// <summary>A caller written against this generator has to meet the same refusal when it is pointed at a provider.</summary>
    [Fact]
    public async Task GenerateAsync_MorePassagesThanOneCallServes_IsRefused()
    {
        // Arrange
        var passages = Enumerable
            .Range(0, DeterministicTextEmbeddingGenerator.PassagesPerCall + 1)
            .Select(index => $"passage {index}")
            .ToArray();

        // Act, Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            this.generator.GenerateAsync(passages, TestContext.Current.CancellationToken));
    }

    /// <summary>The vector of nothing is a point every unrelated chunk sits equally near, and a provider bills for producing it.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GenerateAsync_ABlankPassage_IsRefused(string passage)
    {
        // Act, Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            this.generator.GenerateAsync([passage], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GenerateAsync_NoPassages_IsRefused()
    {
        // Act, Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            this.generator.GenerateAsync([], TestContext.Current.CancellationToken));
    }

    /// <summary>Cutting a passage is part of what the identity records, so two passages sharing a prefix past the limit are one point.</summary>
    [Fact]
    public async Task GenerateAsync_PassagesDifferingOnlyPastTheInputLimit_ProduceTheSameVector()
    {
        // Arrange
        var prefix = new string('x', 64);

        // Act
        var vectors = await this.generator.GenerateAsync(
            [prefix + "first tail", prefix + "second tail"],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(vectors[0].Components.ToArray(), vectors[1].Components.ToArray());
    }

    private static double Length(EmbeddingVector vector) =>
        Math.Sqrt(vector.Components.ToArray().Sum(component => (double)component * component));
}
