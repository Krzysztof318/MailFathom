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
        var plan = ChatDeclarations.Plan(
            maximumOutputTokens: 512,
            temperature: 0.2f,
            topP: 0.9f,
            reasoningEffort: ChatReasoningEffort.High);

        // Assert
        Assert.Equal("answering", plan.Endpoint.Alias);
        Assert.Equal(512, plan.MaximumOutputTokens);
        Assert.Equal(0.2f, plan.Temperature);
        Assert.Equal(0.9f, plan.TopP);
        Assert.Equal(ChatReasoningEffort.High, plan.ReasoningEffort);
        Assert.Equal(ChatDeclarations.RequestTimeout, plan.RequestTimeout);
    }

    /// <summary>
    /// Several current models reject the sampling parameters outright, and one that does not reason rejects the effort,
    /// so not sending any of the three has to stay expressible.
    /// </summary>
    [Fact]
    public void Create_WithoutSamplingParametersOrAReasoningEffort_LeavesThemUnset()
    {
        // Act
        var plan = ChatDeclarations.Plan();

        // Assert
        Assert.Null(plan.Temperature);
        Assert.Null(plan.TopP);
        Assert.Null(plan.ReasoningEffort);
    }

    /// <summary>An effort of none is a stated effort rather than an absent one, which is what a provider refusing an unstated one asks for.</summary>
    [Fact]
    public void Create_AnEffortOfNone_IsCarriedRatherThanTreatedAsUnset()
    {
        // Act
        var plan = ChatDeclarations.Plan(reasoningEffort: ChatReasoningEffort.None);

        // Assert
        Assert.Equal(ChatReasoningEffort.None, plan.ReasoningEffort);
    }

    /// <summary>The API is part of the endpoint, so a plan carries whichever surface the deployment declared.</summary>
    [Theory]
    [InlineData(ChatProviderApi.ChatCompletions)]
    [InlineData(ChatProviderApi.Responses)]
    public void Create_ADeclaredApi_IsCarriedOnTheEndpoint(ChatProviderApi api)
    {
        // Act
        var plan = ChatDeclarations.Plan(ChatDeclarations.Endpoint(api: api));

        // Assert
        Assert.Equal(api, plan.Endpoint.Api);
    }

    /// <summary>
    /// A configuration binder accepts any number for an enum, so a value no member declares has to be refused here
    /// rather than reaching a request as a path or a parameter naming nothing.
    /// </summary>
    [Fact]
    public void Create_AnApiOrAnEffortNamingNoValue_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ChatDeclarations.Plan(ChatDeclarations.Endpoint(api: (ChatProviderApi)7)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ChatDeclarations.Plan(reasoningEffort: (ChatReasoningEffort)9));
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
            reasoningEffort: null,
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
            reasoningEffort: null,
            maximumMessagesPerRequest: 8,
            maximumRequestCharacters: 4000,
            requestTimeout: TimeSpan.Zero));
    }
}
