// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using MailFathom.AI.ProviderAdapters;
using MailFathom.AI.UnitTests.TestDoubles;
using Microsoft.Extensions.AI;
using Xunit;

namespace MailFathom.AI.UnitTests.ProviderAdapters;

/// <summary>Covers the one place a declaration becomes the parameters a request carries.</summary>
/// <remarks>
/// Both the single-request adapter and the answering run send through this, which is what stops a parameter reaching one
/// path and not the other. What each test states is whether a member appears on the options at all, because an absent
/// member is what keeps the parameter off a request that a model would reject for carrying it.
/// </remarks>
public sealed class ChatGenerationParameterMappingTests
{
    [Fact]
    public void ToChatOptions_ADeclarationWithEveryParameter_CarriesAllOfThem()
    {
        // Arrange
        var plan = ChatDeclarations.Plan(
            maximumOutputTokens: 512,
            temperature: 0.2f,
            topP: 0.9f,
            reasoningEffort: ChatReasoningEffort.Medium);

        // Act
        var options = ChatGenerationParameterMapping.ToChatOptions(plan);

        // Assert
        Assert.Equal(512, options.MaxOutputTokens);
        Assert.Equal(0.2f, options.Temperature);
        Assert.Equal(0.9f, options.TopP);
        Assert.Equal(ReasoningEffort.Medium, options.Reasoning?.Effort);
    }

    /// <summary>A model that does not reason rejects the parameter, so an unwritten effort has to leave the block off entirely.</summary>
    [Fact]
    public void ToChatOptions_ADeclarationWithoutAReasoningEffort_CarriesNoReasoningBlock()
    {
        // Act
        var options = ChatGenerationParameterMapping.ToChatOptions(ChatDeclarations.Plan());

        // Assert
        Assert.Null(options.Reasoning);
        Assert.Null(options.Temperature);
        Assert.Null(options.TopP);
    }

    /// <summary>Every declared effort maps onto one the request can carry, so none of them is a setting nothing reads.</summary>
    [Theory]
    [InlineData(ChatReasoningEffort.None, ReasoningEffort.None)]
    [InlineData(ChatReasoningEffort.Low, ReasoningEffort.Low)]
    [InlineData(ChatReasoningEffort.Medium, ReasoningEffort.Medium)]
    [InlineData(ChatReasoningEffort.High, ReasoningEffort.High)]
    [InlineData(ChatReasoningEffort.ExtraHigh, ReasoningEffort.ExtraHigh)]
    public void ToChatOptions_ADeclaredEffort_MapsOntoTheOneARequestCarries(
        ChatReasoningEffort declared,
        ReasoningEffort expected)
    {
        // Act
        var options = ChatGenerationParameterMapping.ToChatOptions(
            ChatDeclarations.Plan(reasoningEffort: declared));

        // Assert
        Assert.Equal(expected, options.Reasoning?.Effort);
    }

    [Fact]
    public void ToChatOptions_WithoutAPlan_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => ChatGenerationParameterMapping.ToChatOptions(null!));
    }
}
