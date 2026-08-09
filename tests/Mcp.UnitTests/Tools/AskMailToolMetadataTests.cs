// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Mcp.Tools;
using MailFathom.Mcp.UnitTests.TestDoubles;
using ModelContextProtocol.Protocol;
using Xunit;

namespace MailFathom.Mcp.UnitTests.Tools;

/// <summary>Covers the descriptor MailFathom advertises for <c>ask_mail</c>.</summary>
/// <remarks>
/// The annotations are contract metadata rather than documentation, and they matter most on this tool: it is the one
/// that spends a provider call, so a client reading <c>readOnlyHint</c> and <c>destructiveHint</c> is deciding whether
/// asking a question can change a mailbox. It cannot, and that is a property of what the run is composed of rather than
/// a claim these annotations make on its behalf.
/// </remarks>
public sealed class AskMailToolMetadataTests
{
    [Fact]
    public void AddMailFathomServer_AdvertisesTheAskMailToolUnderItsProtocolName()
    {
        // Arrange, Act
        var advertisedTool = AdvertisedAskMailTool();

        // Assert
        Assert.Equal("ask_mail", advertisedTool.Name);
        Assert.Equal("Ask about mail", advertisedTool.Title);
    }

    /// <summary>The four hints the architecture draft requires of every read-only tool.</summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesTheReadOnlyLocalStateAnnotations()
    {
        // Arrange, Act
        var annotations = AdvertisedAskMailTool().Annotations;

        // Assert
        Assert.NotNull(annotations);
        Assert.True(annotations.ReadOnlyHint);
        Assert.False(annotations.DestructiveHint);
        Assert.True(annotations.IdempotentHint);
        Assert.False(annotations.OpenWorldHint);
    }

    /// <summary>A model reads this to decide between asking and searching, so it states the cost and what the answer carries.</summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesADescriptionStatingWhatItCostsAndWhatItCites()
    {
        // Arrange, Act
        var description = AdvertisedAskMailTool().Description;

        // Assert
        Assert.NotNull(description);
        Assert.Contains("local", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cites", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("get_email_content", description, StringComparison.Ordinal);
        Assert.Contains("search_emails", description, StringComparison.Ordinal);
    }

    /// <summary>What a question cannot do is part of the contract a client reads before it calls anything.</summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesADescriptionStatingThatAskingChangesNothing()
    {
        // Arrange, Act
        var description = AdvertisedAskMailTool().Description;

        // Assert
        Assert.NotNull(description);
        Assert.Contains("never sends, deletes, moves, or marks mail as read", description, StringComparison.Ordinal);
    }

    [Fact]
    public void AddMailFathomServer_AdvertisesTheQuestionAndTheScopeAsInputSchemaProperties()
    {
        // Arrange
        string[] expectedProperties = ["question", "accounts", "folderAliases"];

        // Act
        var advertisedProperties = AdvertisedAskMailTool()
            .InputSchema
            .GetProperty("properties")
            .EnumerateObject()
            .Select(property => property.Name)
            .ToArray();

        // Assert
        Assert.Equal(
            [.. expectedProperties.Order(StringComparer.Ordinal)],
            [.. advertisedProperties.Order(StringComparer.Ordinal)]);
    }

    /// <summary>What one answer publishes is a deployment control, so no argument may exist through which a caller could raise it.</summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesNoArgumentThatWidensWhatOneAnswerPublishes()
    {
        // Arrange
        string[] boundArguments = ["maxCitations", "maxAnswerCharacters", "passageCount", "includePassages"];

        // Act
        var advertisedProperties = AdvertisedAskMailTool().InputSchema.GetProperty("properties");

        // Assert
        Assert.All(
            boundArguments,
            argument => Assert.False(
                advertisedProperties.TryGetProperty(argument, out _),
                $"'{argument}' is advertised, which would let a caller widen a deployment-wide bound."));
    }

    /// <summary>The question is the one argument the tool cannot be called without, so the schema has to say so.</summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesTheQuestionAsTheOneRequiredArgument()
    {
        // Arrange, Act
        var required = AdvertisedAskMailTool()
            .InputSchema
            .GetProperty("required")
            .EnumerateArray()
            .Select(value => value.ToString())
            .ToArray();

        // Assert
        Assert.Equal(["question"], required);
    }

    /// <summary>An argument nobody can interpret is an argument a model guesses at, so every one carries its own description.</summary>
    [Fact]
    public void AddMailFathomServer_DescribesEveryInputSchemaProperty()
    {
        // Arrange, Act
        var describedProperties = AdvertisedAskMailTool()
            .InputSchema
            .GetProperty("properties")
            .EnumerateObject()
            .Select(property => (
                property.Name,
                HasDescription: property.Value.TryGetProperty("description", out var description)
                    && description.GetString()?.Length > 20))
            .ToArray();

        // Assert
        Assert.All(
            describedProperties,
            property => Assert.True(property.HasDescription, $"'{property.Name}' carries no usable description."));
    }

    /// <summary>The cancellation token the tool takes is the host's concern and must never become a protocol argument.</summary>
    [Fact]
    public void AddMailFathomServer_DoesNotAdvertiseTheCancellationTokenAsAnArgument()
    {
        // Arrange, Act
        var advertisedProperties = AdvertisedAskMailTool().InputSchema.GetProperty("properties");

        // Assert
        Assert.False(advertisedProperties.TryGetProperty("cancellationToken", out _));
    }

    /// <summary>The citations and the two truncation flags are the contract, not diagnostics, so the schema publishes all three.</summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesTheAnswerTheCitationsAndBothTruncationFlags()
    {
        // Arrange, Act
        var outputSchema = AdvertisedAskMailTool().OutputSchema;

        // Assert
        Assert.NotNull(outputSchema);
        var properties = outputSchema.Value.GetProperty("properties");
        Assert.True(properties.TryGetProperty("answer", out _));
        Assert.True(properties.TryGetProperty("citations", out _));
        Assert.True(properties.TryGetProperty("answerTruncated", out _));
        Assert.True(properties.TryGetProperty("citationsTruncated", out _));
    }

    private static Tool AdvertisedAskMailTool() =>
        RegisteredMcpToolSurface.AdvertisedTool(AskMailTool.ToolName);
}
