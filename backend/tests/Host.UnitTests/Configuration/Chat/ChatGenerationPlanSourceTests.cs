// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Chat;
using MailFathom.Host.UnitTests.TestDoubles;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Chat;

/// <summary>Covers the value that turns a reloadable declaration into the plan a question is answered through.</summary>
/// <remarks>
/// What these establish is the reload contract of the chat section: a corrected model reaches the next question without
/// a restart, and a question already holding a plan is unaffected by the correction.
/// </remarks>
public sealed class ChatGenerationPlanSourceTests
{
    [Fact]
    public void Current_ADeclaredEndpoint_BuildsThePlanThatDeclarationDescribes()
    {
        // Arrange
        var source = new ChatGenerationPlanSource(
            new StubSettingsSnapshot<ChatModelOptions>(Declaring("a-chat-model")));

        // Act
        var plan = source.Current;

        // Assert
        Assert.Equal("a-chat-model", plan.Endpoint.RoutedModelName);
    }

    /// <summary>The whole point of the source: an operator correcting a model the provider refused pays an edit rather than a restart.</summary>
    [Fact]
    public void Current_AfterTheDeclarationIsRepublished_BuildsThePlanTheNewOneDescribes()
    {
        // Arrange
        var published = new StubSettingsSnapshot<ChatModelOptions>(Declaring("a-refused-model"));
        var source = new ChatGenerationPlanSource(published);

        _ = source.Current;

        // Act
        published.Current = Declaring("a-corrected-model");

        // Assert
        Assert.Equal("a-corrected-model", source.Current.Endpoint.RoutedModelName);
    }

    /// <summary>A run holds the plan it began with, so a reload that lands mid-question changes the next question and not that one.</summary>
    [Fact]
    public void Current_APlanTakenBeforeTheDeclarationMoved_GoesOnDescribingTheModelItWasTakenWith()
    {
        // Arrange
        var published = new StubSettingsSnapshot<ChatModelOptions>(Declaring("the-model-the-run-began-with"));
        var source = new ChatGenerationPlanSource(published);
        var planTheRunHolds = source.Current;

        // Act
        published.Current = Declaring("a-model-declared-mid-run");

        // Assert
        Assert.Equal("the-model-the-run-began-with", planTheRunHolds.Endpoint.RoutedModelName);
        Assert.Equal("a-model-declared-mid-run", source.Current.Endpoint.RoutedModelName);
    }

    /// <summary>One published declaration is one plan, so reading it on every request and every client the factory builds costs nothing.</summary>
    [Fact]
    public void Current_ReadTwiceOverOneDeclaration_AnswersWithTheSamePlan()
    {
        // Arrange
        var source = new ChatGenerationPlanSource(
            new StubSettingsSnapshot<ChatModelOptions>(Declaring("a-chat-model")));

        // Act
        var first = source.Current;
        var second = source.Current;

        // Assert
        Assert.Same(first, second);
    }

    /// <summary>Registered only where an endpoint was declared and protected by a reload rule that refuses its removal, so the absence is a contradiction rather than a state.</summary>
    [Fact]
    public void Current_ADeclarationCarryingNoEndpoint_RefusesRatherThanAnswering()
    {
        // Arrange
        var source = new ChatGenerationPlanSource(
            new StubSettingsSnapshot<ChatModelOptions>(new ChatModelOptions()));

        // Act
        var refusal = Assert.Throws<InvalidOperationException>(() => source.Current);

        // Assert
        Assert.Contains("declared at registration", refusal.Message, StringComparison.Ordinal);
    }

    private static ChatModelOptions Declaring(string model) => new()
    {
        Alias = "answering",
        Model = model,
    };
}
