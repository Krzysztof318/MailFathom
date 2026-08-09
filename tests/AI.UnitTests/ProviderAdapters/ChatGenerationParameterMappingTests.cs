// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.ProviderAdapters;
using MailFathom.AI.UnitTests.TestDoubles;
using Xunit;

namespace MailFathom.AI.UnitTests.ProviderAdapters;

/// <summary>Covers the one place a declaration becomes the parameters a request carries.</summary>
/// <remarks>
/// Both the single-request adapter and the answering run send through this, which is what stops a parameter reaching one
/// path and not the other. What each test states is whether a member appears on the options at all, because an absent
/// member is what keeps the parameter off a request that a model would reject for carrying it.
/// <para>
/// The reasoning effort is deliberately not asserted here beyond whether the hook exists. It is stated through the
/// client library's own request options, so what it produces is a property of the request rather than of this object,
/// and <see cref="ProviderChatModelClientTests" /> asserts it on the wire over both APIs and over a level no build knows.
/// </para>
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
            reasoningEffort: "medium");

        // Act
        var options = ChatGenerationParameterMapping.ToChatOptions(plan);

        // Assert
        Assert.Equal(512, options.MaxOutputTokens);
        Assert.Equal(0.2f, options.Temperature);
        Assert.Equal(0.9f, options.TopP);
        Assert.NotNull(options.RawRepresentationFactory);
    }

    /// <summary>A model that does not reason rejects the parameter, so an unwritten effort has to leave the hook off entirely.</summary>
    [Fact]
    public void ToChatOptions_ADeclarationWithoutAReasoningEffort_CarriesNoRequestHook()
    {
        // Act
        var options = ChatGenerationParameterMapping.ToChatOptions(ChatDeclarations.Plan());

        // Assert
        Assert.Null(options.RawRepresentationFactory);
        Assert.Null(options.Temperature);
        Assert.Null(options.TopP);
    }

    [Fact]
    public void ToChatOptions_WithoutAPlan_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => ChatGenerationParameterMapping.ToChatOptions(null!));
    }
}
