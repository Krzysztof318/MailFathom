// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using MailFathom.Host.Configuration.Chat;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Secrets.Discovery;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Chat;

/// <summary>Covers the step between a bound declaration and the value the adapter is allowed to assume.</summary>
public sealed class ChatGenerationPlanMapperTests
{
    [Fact]
    public void Map_ADeclaredModel_CarriesEveryDeclaredParameter()
    {
        // Arrange
        var settings = DeclaredChatModels.Section(new ChatModelDeclarationOptions
        {
            Alias = "answering",
            Model = "a-chat-model",
            Address = "https://provider.invalid/v1/",
            Api = ChatProviderApi.Responses,
            MaxOutputTokens = 512,
            Temperature = 0.3f,
            TopP = 0.8f,
            ReasoningEffort = "high",
            MaxMessagesPerRequest = 12,
            MaxRequestCharacters = 60_000,
            MaxRequestImageOctets = 2_000_000,
            RequestTimeout = TimeSpan.FromSeconds(90),
            ApiKey = new ConfiguredSecret { SecretReference = "env:CHAT_KEY" },
        });

        // Act
        var plan = ChatGenerationPlanMapper.Map(settings);

        // Assert
        Assert.NotNull(plan);
        Assert.Equal("answering", plan.Endpoint.Alias);
        Assert.Equal("a-chat-model", plan.Endpoint.RoutedModelName);
        Assert.Equal(ChatProviderApi.Responses, plan.Endpoint.Api);
        Assert.Equal(512, plan.MaximumOutputTokens);
        Assert.Equal(0.3f, plan.Temperature);
        Assert.Equal(0.8f, plan.TopP);
        Assert.Equal("high", plan.ReasoningEffort);
        Assert.Equal(12, plan.MaximumMessagesPerRequest);
        Assert.Equal(60_000, plan.MaximumRequestCharacters);
        Assert.Equal(2_000_000, plan.MaximumRequestImageOctets);
        Assert.Equal(TimeSpan.FromSeconds(90), plan.RequestTimeout);
    }

    /// <summary>Nothing declared is a working deployment, so the composition root registers no client rather than one that fails at first use.</summary>
    [Fact]
    public void Map_AnAbsentSection_ProducesNoPlan()
    {
        // Act
        var plan = ChatGenerationPlanMapper.Map(new ChatModelOptions());

        // Assert
        Assert.Null(plan);
    }

    /// <summary>A section declaring one model and naming none resolves to that one, which is what keeps the ordinary deployment to a single block.</summary>
    [Fact]
    public void Map_OneModelAndNoMainModelNamed_ResolvesToThatModel()
    {
        // Arrange
        var settings = DeclaredChatModels.Section(DeclaredChatModels.Model("the-only-one"));

        // Act
        var plan = ChatGenerationPlanMapper.Map(settings);

        // Assert
        Assert.NotNull(plan);
        Assert.Equal("the-only-one", plan.Endpoint.Alias);
    }

    /// <summary>A declared fallback reaches the plan, so one call that the first model could not answer has somewhere else to go.</summary>
    [Fact]
    public void Map_AMainModelWithAFallback_CarriesTheFallbackBehindIt()
    {
        // Arrange
        var settings = DeclaredChatModels.SectionWithFallback();

        // Act
        var plan = ChatGenerationPlanMapper.Map(settings);

        // Assert
        Assert.NotNull(plan);
        Assert.Equal("answering", plan.Endpoint.Alias);
        Assert.Equal("standby", plan.Fallback?.Endpoint.Alias);
        Assert.Equal(["answering", "standby"], plan.Chain.Select(model => model.Endpoint.Alias));
    }

    /// <summary>A capability naming a model of its own is routed to that one rather than to the model questions are answered by.</summary>
    [Fact]
    public void Map_AReferenceNamingItsOwnModel_ResolvesToThatModel()
    {
        // Arrange
        var settings = DeclaredChatModels.Section(
            DeclaredChatModels.Model("answering"),
            DeclaredChatModels.Model("cheap", model: "a-small-fast-model"));

        // Act
        var plan = ChatGenerationPlanMapper.Map(settings, new ChatModelReferenceOptions { Alias = "cheap" });

        // Assert
        Assert.NotNull(plan);
        Assert.Equal("a-small-fast-model", plan.Endpoint.RoutedModelName);
    }

    /// <summary>A reference that names no model takes the main model and the fallback behind it, rather than half of the arrangement.</summary>
    [Fact]
    public void Map_AReferenceNamingNoModel_TakesTheMainModelAndItsFallback()
    {
        // Arrange
        var settings = DeclaredChatModels.SectionWithFallback();

        // Act
        var plan = ChatGenerationPlanMapper.Map(settings, new ChatModelReferenceOptions());

        // Assert
        Assert.NotNull(plan);
        Assert.Equal("answering", plan.Endpoint.Alias);
        Assert.Equal("standby", plan.Fallback?.Endpoint.Alias);
    }

    /// <summary>Chat completions is what a model stating no API runs on, because every OpenAI-compatible server offers it.</summary>
    [Fact]
    public void Map_AModelStatingNoApi_ReachesTheProviderThroughChatCompletions()
    {
        // Act
        var plan = ChatGenerationPlanMapper.Map(DeclaredChatModels.Section());

        // Assert
        Assert.NotNull(plan);
        Assert.Equal(ChatProviderApi.ChatCompletions, plan.Endpoint.Api);
    }

    /// <summary>
    /// An unset sampling parameter and an unset reasoning effort both have to survive the mapping, because a model that
    /// rejects one of them rejects every call a deployment that sent it would make.
    /// </summary>
    [Fact]
    public void Map_WithoutSamplingParameters_LeavesThemUnset()
    {
        // Act
        var plan = ChatGenerationPlanMapper.Map(DeclaredChatModels.Section());

        // Assert
        Assert.NotNull(plan);
        Assert.Null(plan.Temperature);
        Assert.Null(plan.TopP);
        Assert.Null(plan.ReasoningEffort);
    }

    [Fact]
    public void Map_WithoutSettings_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => ChatGenerationPlanMapper.Map(null!));
    }
}
