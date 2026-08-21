// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Retrieval;
using MailFathom.Application.Retrieval;
using Xunit;

namespace MailFathom.AI.UnitTests.Retrieval;

/// <summary>Covers what a relevance-filter declaration has to say before the pass is allowed to assume it.</summary>
/// <remarks>
/// The plan is built once at startup, so a value refused here is refused before any question pays for a judgement made
/// under it.
/// </remarks>
public sealed class PassageRelevanceFilterPlanTests
{
    private static readonly EmailKnowledgeBounds RetrievalBounds = EmailKnowledgeBounds.Default;

    [Fact]
    public void Create_ADeclaredFilter_CarriesItsBoundAndItsThreshold()
    {
        // Act
        var plan = PassageRelevanceFilterPlan.Create(RetrievalBounds, maximumCandidates: 6, minimumRelevance: 65);

        // Assert
        Assert.Equal(6, plan.MaximumCandidates);
        Assert.Equal(65, plan.MinimumRelevance);
    }

    /// <summary>
    /// A candidate count beyond what one retrieval hands over states a ceiling no question could reach, so a deployment
    /// writing one would be told it had widened a filter that had not moved.
    /// </summary>
    [Fact]
    public void Create_ACandidateBoundBeyondWhatOneRetrievalHandsOver_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => PassageRelevanceFilterPlan.Create(
            RetrievalBounds,
            RetrievalBounds.MaximumPassages + 1,
            minimumRelevance: 50));
    }

    /// <summary>The ceiling follows the retrieval bounds it was given, so narrowing a deployment's retrieval narrows what may be judged with it.</summary>
    [Fact]
    public void Create_ACandidateBoundBeyondANarrowedRetrieval_IsRefusedAtTheNarrowerCeiling()
    {
        // Arrange
        var narrowed = EmailKnowledgeBounds.Create(maximumPassages: 3, maximumCharactersPerPassage: 1_200);

        // Act, Assert
        Assert.Equal(3, PassageRelevanceFilterPlan.Create(narrowed, 3, minimumRelevance: 50).MaximumCandidates);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PassageRelevanceFilterPlan.Create(narrowed, 4, minimumRelevance: 50));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_ACandidateBoundBelowOne_IsRefused(int maximumCandidates)
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PassageRelevanceFilterPlan.Create(RetrievalBounds, maximumCandidates, minimumRelevance: 50));
    }

    /// <summary>A threshold of zero would pay for a judgement that can drop nothing, and one above the scale would drop everything the model could ever answer.</summary>
    [Theory]
    [InlineData(PassageRelevanceFilterPlan.LeastRelevance)]
    [InlineData(-1)]
    [InlineData(PassageRelevanceFilterPlan.GreatestRelevance + 1)]
    public void Create_AThresholdOffTheScale_IsRefused(int minimumRelevance)
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PassageRelevanceFilterPlan.Create(RetrievalBounds, maximumCandidates: 4, minimumRelevance));
    }

    /// <summary>Both ends of the scale a judgement is answered on stay reachable, so a deployment can demand a perfect score.</summary>
    [Theory]
    [InlineData(PassageRelevanceFilterPlan.LeastRelevance + 1)]
    [InlineData(PassageRelevanceFilterPlan.GreatestRelevance)]
    public void Create_AThresholdAtTheEdgeOfTheScale_IsAccepted(int minimumRelevance)
    {
        // Act
        var plan = PassageRelevanceFilterPlan.Create(RetrievalBounds, maximumCandidates: 4, minimumRelevance);

        // Assert
        Assert.Equal(minimumRelevance, plan.MinimumRelevance);
    }

    [Fact]
    public void ToString_APlan_ReadsAsWhatOneQuestionMaySpendAndDemand()
    {
        // Act
        var described = PassageRelevanceFilterPlan
            .Create(RetrievalBounds, maximumCandidates: 6, minimumRelevance: 65)
            .ToString();

        // Assert
        Assert.Equal("at most 6 candidates, each judged at least 65", described);
    }
}
