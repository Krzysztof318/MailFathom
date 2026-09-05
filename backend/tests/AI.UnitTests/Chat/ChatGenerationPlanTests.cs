// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
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
            reasoningEffort: "high");

        // Assert
        Assert.Equal("answering", plan.Endpoint.Alias);
        Assert.Equal(512, plan.MaximumOutputTokens);
        Assert.Equal(0.2f, plan.Temperature);
        Assert.Equal(0.9f, plan.TopP);
        Assert.Equal("high", plan.ReasoningEffort);
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
        var plan = ChatDeclarations.Plan(reasoningEffort: "none");

        // Assert
        Assert.Equal("none", plan.ReasoningEffort);
    }

    /// <summary>
    /// The vocabulary belongs to the model, so a level this build has never heard of is carried unchanged. `xhigh`
    /// arrived after the levels beneath it, and the next one must not cost a release to use.
    /// </summary>
    [Theory]
    [InlineData("none")]
    [InlineData("minimal")]
    [InlineData("xhigh")]
    [InlineData("a-level-released-later")]
    [InlineData("some_future_level")]
    public void Create_AnEffortThisBuildNeverHeardOf_IsCarriedUnchanged(string effort)
    {
        // Act
        var plan = ChatDeclarations.Plan(reasoningEffort: effort);

        // Assert
        Assert.Equal(effort, plan.ReasoningEffort);
    }

    /// <summary>
    /// The shape is checked and the vocabulary is not, so what is refused is a value no provider could read as a level
    /// whatever it supports — learning that from a paid request would be learning it late.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" high")]
    [InlineData("high ")]
    [InlineData("two words")]
    // A value provisioned from a file ends in a newline, and a regex anchored with `$` would accept it.
    [InlineData("high\n")]
    [InlineData("high\r\n")]
    [InlineData("-high")]
    [InlineData("high-")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void Create_AnEffortNoProviderCouldReadAsALevel_IsRefused(string effort)
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => ChatDeclarations.Plan(reasoningEffort: effort));
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
    /// rather than reaching a request as a path naming nothing. The API is a closed set where the effort is not, because
    /// it selects which client this build constructs rather than a word the provider reads.
    /// </summary>
    [Fact]
    public void Create_AnApiNamingNoValue_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ChatDeclarations.Plan(ChatDeclarations.Endpoint(api: (ChatProviderApi)7)));
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
            maximumRequestImageOctets: 1024,
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
            maximumRequestImageOctets: 1024,
            requestTimeout: TimeSpan.Zero));
    }
}
