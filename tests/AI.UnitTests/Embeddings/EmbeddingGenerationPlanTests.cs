// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Embeddings;
using MailFathom.AI.UnitTests.TestDoubles;
using Xunit;

namespace MailFathom.AI.UnitTests.Embeddings;

/// <summary>Covers what the adapter is allowed to assume about the declaration it runs on.</summary>
public sealed class EmbeddingGenerationPlanTests
{
    [Fact]
    public void Create_AChain_ReadsTheGeometryFromIt()
    {
        // Arrange
        var endpoints = new[]
        {
            EmbeddingDeclarations.Endpoint("primary"),
            EmbeddingDeclarations.Endpoint("fallback", address: "https://second.invalid/v1/"),
        };

        // Act
        var plan = EmbeddingGenerationPlan.Create(endpoints, false, 16, TimeSpan.FromSeconds(30));

        // Assert
        Assert.Equal(EmbeddingDeclarations.Dimension, plan.Identity.Dimension);
        Assert.Equal(2, plan.Endpoints.Count);
    }

    /// <summary>The adapter never revalidates, so a chain that disagrees must not become a plan.</summary>
    [Fact]
    public void Create_AChainThatDoesNotReachOneVectorSpace_IsRefused()
    {
        // Arrange
        var endpoints = new[]
        {
            EmbeddingDeclarations.Endpoint("primary"),
            EmbeddingDeclarations.Endpoint("fallback", dimension: 8),
        };

        // Act
        var refusal = Assert.Throws<ArgumentException>(
            () => EmbeddingGenerationPlan.Create(endpoints, false, 16, TimeSpan.FromSeconds(30)));

        // Assert
        Assert.Contains("dimension", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_NoEndpoints_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(
            () => EmbeddingGenerationPlan.Create([], false, 16, TimeSpan.FromSeconds(30)));
    }

    /// <summary>An unbounded request holds the work behind it open for as long as an endpoint stays silent.</summary>
    [Fact]
    public void Create_ARequestTimeoutThatIsNotPositive_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => EmbeddingGenerationPlan.Create(
            [EmbeddingDeclarations.Endpoint()],
            false,
            16,
            TimeSpan.Zero));
    }

    [Fact]
    public void Create_ABatchBoundThatIsNotPositive_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => EmbeddingGenerationPlan.Create(
            [EmbeddingDeclarations.Endpoint()],
            false,
            0,
            TimeSpan.FromSeconds(30)));
    }
}
