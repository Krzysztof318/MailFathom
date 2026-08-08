// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using MailFathom.AI.UnitTests.TestDoubles;
using Xunit;

namespace MailFathom.AI.UnitTests.Chat;

/// <summary>Covers what a declaration has to say before the adapter is allowed to assume it.</summary>
/// <remarks>
/// The plan is built once at startup, so everything refused here is refused before a request is ever made. A value the
/// provider would have rejected on every call is learned from configuration rather than from a paid request.
/// </remarks>
public sealed class ChatGenerationPlanTests
{
    [Fact]
    public void Create_ADeclaredEndpoint_CarriesItsParameters()
    {
        // Act
        var plan = ChatDeclarations.Plan(maximumOutputTokens: 512, temperature: 0.2f, topP: 0.9f);

        // Assert
        Assert.Equal("answering", plan.Endpoint.Alias);
        Assert.Equal(512, plan.MaximumOutputTokens);
        Assert.Equal(0.2f, plan.Temperature);
        Assert.Equal(0.9f, plan.TopP);
        Assert.Equal(ChatDeclarations.RequestTimeout, plan.RequestTimeout);
    }

    /// <summary>Several current models reject the sampling parameters outright, so not sending one has to stay expressible.</summary>
    [Fact]
    public void Create_WithoutSamplingParameters_LeavesThemUnset()
    {
        // Act
        var plan = ChatDeclarations.Plan();

        // Assert
        Assert.Null(plan.Temperature);
        Assert.Null(plan.TopP);
    }

    [Fact]
    public void Create_WithoutAnEndpoint_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => ChatGenerationPlan.Create(
            endpoint: null!,
            maximumOutputTokens: 256,
            temperature: null,
            topP: null,
            maximumMessagesPerRequest: 8,
            maximumRequestCharacters: 4000,
            requestTimeout: TimeSpan.FromSeconds(5)));
    }

    [Theory]
    [InlineData("", "a-chat-model")]
    [InlineData("   ", "a-chat-model")]
    [InlineData("answering", "")]
    [InlineData("answering", "   ")]
    public void Create_AnEndpointMissingAName_IsRefused(string alias, string routedModelName)
    {
        // Arrange
        var endpoint = ChatDeclarations.Endpoint(alias, routedModelName: routedModelName);

        // Act, Assert
        Assert.Throws<ArgumentException>(() => ChatDeclarations.Plan(endpoint));
    }

    [Fact]
    public void Create_AnOutputBudgetThatIsNotPositive_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => ChatDeclarations.Plan(maximumOutputTokens: 0));
    }

    [Theory]
    [InlineData(-0.1f)]
    [InlineData(2.1f)]
    public void Create_ATemperatureOutsideTheAcceptedRange_IsRefused(float temperature)
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => ChatDeclarations.Plan(temperature: temperature));
    }

    [Theory]
    [InlineData(-0.1f)]
    [InlineData(1.1f)]
    public void Create_ANucleusThresholdOutsideTheAcceptedRange_IsRefused(float topP)
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => ChatDeclarations.Plan(topP: topP));
    }

    [Fact]
    public void Create_ABoundThatIsNotPositive_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => ChatDeclarations.Plan(maximumMessagesPerRequest: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ChatDeclarations.Plan(maximumRequestCharacters: 0));
    }

    [Fact]
    public void Create_ARequestTimeoutThatIsNotPositive_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => ChatGenerationPlan.Create(
            ChatDeclarations.Endpoint(),
            maximumOutputTokens: 256,
            temperature: null,
            topP: null,
            maximumMessagesPerRequest: 8,
            maximumRequestCharacters: 4000,
            requestTimeout: TimeSpan.Zero));
    }
}
