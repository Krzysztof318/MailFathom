// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Chat;
using MailFathom.Infrastructure.Secrets.Discovery;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Chat;

/// <summary>Covers the step between a bound declaration and the value the adapter is allowed to assume.</summary>
public sealed class ChatGenerationPlanMapperTests
{
    [Fact]
    public void Map_ADeclaredEndpoint_CarriesEveryDeclaredParameter()
    {
        // Arrange
        var settings = new ChatModelOptions
        {
            Alias = "answering",
            Model = "a-chat-model",
            Address = "https://provider.invalid/v1/",
            MaxOutputTokens = 512,
            Temperature = 0.3f,
            TopP = 0.8f,
            MaxMessagesPerRequest = 12,
            MaxRequestCharacters = 60_000,
            RequestTimeout = TimeSpan.FromSeconds(90),
            ApiKey = new ConfiguredSecret { SecretReference = "env:CHAT_KEY" },
        };

        // Act
        var plan = ChatGenerationPlanMapper.Map(settings);

        // Assert
        Assert.NotNull(plan);
        Assert.Equal("answering", plan.Endpoint.Alias);
        Assert.Equal("a-chat-model", plan.Endpoint.RoutedModelName);
        Assert.Equal(512, plan.MaximumOutputTokens);
        Assert.Equal(0.3f, plan.Temperature);
        Assert.Equal(0.8f, plan.TopP);
        Assert.Equal(12, plan.MaximumMessagesPerRequest);
        Assert.Equal(60_000, plan.MaximumRequestCharacters);
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

    /// <summary>An unset sampling parameter has to survive the mapping, because several current models reject one that is sent.</summary>
    [Fact]
    public void Map_WithoutSamplingParameters_LeavesThemUnset()
    {
        // Arrange
        var settings = new ChatModelOptions
        {
            Alias = "answering",
            Model = "a-chat-model",
            ApiKey = new ConfiguredSecret { SecretReference = "env:CHAT_KEY" },
        };

        // Act
        var plan = ChatGenerationPlanMapper.Map(settings);

        // Assert
        Assert.NotNull(plan);
        Assert.Null(plan.Temperature);
        Assert.Null(plan.TopP);
    }

    [Fact]
    public void Map_WithoutSettings_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => ChatGenerationPlanMapper.Map(null!));
    }
}
