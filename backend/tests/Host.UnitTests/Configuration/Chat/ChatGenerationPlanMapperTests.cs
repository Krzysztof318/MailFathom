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

    /// <summary>
    /// Every capability reads its own key, which is the whole of the routing: a model named under one leaves the rest on
    /// the model questions are answered with.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryCapability))]
    public void Map_ACapabilityNamingItsOwnModel_RoutesOnlyThatCapability(ChatCapability capability)
    {
        // Arrange
        var settings = RoutedSection();
        settings.ReferenceFor(capability).Alias = "cheap";

        // Act
        var routed = ChatGenerationPlanMapper.Map(settings, capability);
        var others = Enum.GetValues<ChatCapability>()
            .Where(other => other != capability)
            .Select(other => ChatGenerationPlanMapper.Map(settings, other)?.Endpoint.Alias);

        // Assert
        Assert.NotNull(routed);
        Assert.Equal("a-small-fast-model", routed.Endpoint.RoutedModelName);
        Assert.All(others, alias => Assert.Equal("answering", alias));
    }

    /// <summary>A capability the deployment routed nowhere runs on the main model, which is what a declaration writing none of these keys does.</summary>
    [Fact]
    public void Map_ACapabilityNamingNoModel_RoutesToTheMainModel()
    {
        // Act
        var plan = ChatGenerationPlanMapper.Map(RoutedSection(), ChatCapability.BodyCleanup);

        // Assert
        Assert.NotNull(plan);
        Assert.Equal("answering", plan.Endpoint.Alias);
    }

    /// <summary>
    /// The three-step chain: the model the capability was routed to, the fallback beside it, and the model the
    /// deployment answers questions with — so a failing cheap endpoint degrades to the model an operator trusts rather
    /// than turning the capability off.
    /// </summary>
    [Fact]
    public void Map_ACapabilityNamingAModelAndAFallback_EndsAtTheMainModel()
    {
        // Arrange
        var settings = RoutedSection();
        settings.Enrichment.Model.Alias = "cheap";
        settings.Enrichment.Model.Fallback = "standby";

        // Act
        var plan = ChatGenerationPlanMapper.Map(settings, ChatCapability.Enrichment);

        // Assert
        Assert.NotNull(plan);
        Assert.Equal(["cheap", "standby", "answering"], plan.Chain.Select(model => model.Endpoint.Alias));
    }

    /// <summary>The main model's own fallback is not attempted behind a capability's chain, so one call reaches at most three models.</summary>
    [Fact]
    public void Map_ACapabilityNamingAModel_DoesNotAttemptTheMainModelsOwnFallback()
    {
        // Arrange
        var settings = RoutedSection();
        settings.MainModel.Fallback = "standby";
        settings.Enrichment.Model.Alias = "cheap";

        // Act
        var plan = ChatGenerationPlanMapper.Map(settings, ChatCapability.Enrichment);

        // Assert
        Assert.NotNull(plan);
        Assert.Equal(["cheap", "answering"], plan.Chain.Select(model => model.Endpoint.Alias));
    }

    /// <summary>A capability routed to the main model asks that endpoint once rather than twice, because the chain has nowhere further to go.</summary>
    [Fact]
    public void Map_ACapabilityNamingTheMainModel_AttemptsThatModelOnce()
    {
        // Arrange
        var settings = RoutedSection();
        settings.ThreadState.Model.Alias = "answering";

        // Act
        var plan = ChatGenerationPlanMapper.Map(settings, ChatCapability.ThreadState);

        // Assert
        Assert.NotNull(plan);
        Assert.Equal(["answering"], plan.Chain.Select(model => model.Endpoint.Alias));
    }

    /// <summary>A capability whose fallback is already the main model stops there as well, for the same reason.</summary>
    [Fact]
    public void Map_ACapabilityWhoseFallbackIsTheMainModel_StopsAtTwoModels()
    {
        // Arrange
        var settings = RoutedSection();
        settings.ImageDescription.Model.Alias = "cheap";
        settings.ImageDescription.Model.Fallback = "answering";

        // Act
        var plan = ChatGenerationPlanMapper.Map(settings, ChatCapability.ImageDescription);

        // Assert
        Assert.NotNull(plan);
        Assert.Equal(["cheap", "answering"], plan.Chain.Select(model => model.Endpoint.Alias));
    }

    /// <summary>An alias written with whitespace around it is the same alias, because an operator's configuration file is read rather than parsed twice.</summary>
    [Fact]
    public void Map_ACapabilityNamingAModelWithSurroundingSpace_ReachesTheTrimmedAlias()
    {
        // Arrange
        var settings = RoutedSection();
        settings.SearchPhrasing.Model.Alias = "  cheap  ";

        // Act
        var plan = ChatGenerationPlanMapper.Map(settings, ChatCapability.SearchPhrasing);

        // Assert
        Assert.NotNull(plan);
        Assert.Equal("a-small-fast-model", plan.Endpoint.RoutedModelName);
    }

    /// <summary>A model named beside no declaration is refused by validation, and mapping it would build a plan with nowhere to send a request.</summary>
    [Fact]
    public void Map_ACapabilityWithoutAChatEndpoint_MapsNothing()
    {
        // Arrange
        var settings = new ChatModelOptions();
        settings.BodyCleanup.Model.Alias = "cheap";

        // Act
        var plan = ChatGenerationPlanMapper.Map(settings, ChatCapability.BodyCleanup);

        // Assert
        Assert.Null(plan);
    }

    [Fact]
    public void Map_ACapabilityWithoutSettings_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(
            () => ChatGenerationPlanMapper.Map(null!, ChatCapability.MailAnswering));
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

    /// <summary>Every capability the section declares a reference for, so a capability added without its key fails here rather than silently running on the main model.</summary>
    public static TheoryData<ChatCapability> EveryCapability() => [.. Enum.GetValues<ChatCapability>()];

    /// <summary>A section declaring the model questions are answered with, a cheap one to route a capability to, and a third to stand behind it.</summary>
    private static ChatModelOptions RoutedSection() => DeclaredChatModels.Section(
        DeclaredChatModels.Model("answering"),
        DeclaredChatModels.Model("cheap", model: "a-small-fast-model"),
        DeclaredChatModels.Model("standby", model: "a-standby-model"));
}
