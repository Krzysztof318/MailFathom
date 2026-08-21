// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Retrieval;
using MailFathom.Host.Configuration.Chat;
using MailFathom.Infrastructure.Secrets.Discovery;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Chat;

/// <summary>Covers the step between a bound relevance-filter declaration and the value the second pass is allowed to assume.</summary>
public sealed class PassageRelevanceFilterPlanMapperTests
{
    private static readonly EmailKnowledgeBounds RetrievalBounds = EmailKnowledgeBounds.Default;

    [Fact]
    public void Map_AnEnabledFilter_CarriesItsBoundAndItsThreshold()
    {
        // Arrange
        var settings = Declared();
        settings.RelevanceFilter.Enabled = true;
        settings.RelevanceFilter.MaxCandidates = 6;
        settings.RelevanceFilter.MinimumRelevance = 65;

        // Act
        var plan = PassageRelevanceFilterPlanMapper.Map(settings, RetrievalBounds);

        // Assert
        Assert.NotNull(plan);
        Assert.Equal(6, plan.MaximumCandidates);
        Assert.Equal(65, plan.MinimumRelevance);
    }

    /// <summary>An unwritten candidate count means every passage the retrieval hands over, which is the only place both numbers are known at once.</summary>
    [Fact]
    public void Map_AnEnabledFilterWithNoCandidateBound_JudgesEverythingTheRetrievalHandsOver()
    {
        // Arrange
        var settings = Declared();
        settings.RelevanceFilter.Enabled = true;
        var narrowed = EmailKnowledgeBounds.Create(maximumPassages: 3, maximumCharactersPerPassage: 1_200);

        // Act
        var plan = PassageRelevanceFilterPlanMapper.Map(settings, narrowed);

        // Assert
        Assert.NotNull(plan);
        Assert.Equal(3, plan.MaximumCandidates);
    }

    /// <summary>Declaring a chat endpoint and leaving the pass off is the default deployment, and it registers no filter at all.</summary>
    [Fact]
    public void Map_AChatEndpointWithThePassOff_MapsNothing()
    {
        // Act
        var plan = PassageRelevanceFilterPlanMapper.Map(Declared(), RetrievalBounds);

        // Assert
        Assert.Null(plan);
    }

    /// <summary>A block turned on beside no endpoint is refused by validation, and mapping it would build a plan nothing could judge with.</summary>
    [Fact]
    public void Map_AnEnabledFilterWithoutAChatEndpoint_MapsNothing()
    {
        // Arrange
        var settings = new ChatModelOptions();
        settings.RelevanceFilter.Enabled = true;

        // Act
        var plan = PassageRelevanceFilterPlanMapper.Map(settings, RetrievalBounds);

        // Assert
        Assert.Null(plan);
    }

    [Fact]
    public void Map_WithoutADeclaration_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => PassageRelevanceFilterPlanMapper.Map(null!, RetrievalBounds));
    }

    [Fact]
    public void Map_WithoutRetrievalBounds_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => PassageRelevanceFilterPlanMapper.Map(Declared(), null!));
    }

    private static ChatModelOptions Declared() => new()
    {
        Alias = "answering",
        Model = "a-chat-model",
        ApiKey = new ConfiguredSecret { SecretReference = "env:CHAT_KEY" },
    };
}
