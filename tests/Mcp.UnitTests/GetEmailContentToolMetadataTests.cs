// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailMcp.Mcp.Tools;
using ModelContextProtocol.Protocol;
using Xunit;

namespace MailMcp.Mcp.UnitTests;

/// <summary>Covers the descriptor MailMcp advertises for <c>get_email_content</c>.</summary>
/// <remarks>
/// The annotations are contract metadata rather than documentation: a client decides whether a tool is safe to call,
/// safe to retry, and confined to local state by reading them before it calls anything. The description matters as much
/// here as on any tool and more than on most, because it is where a model learns that this surface returns message
/// content and returns no attachment bytes with it.
/// </remarks>
public sealed class GetEmailContentToolMetadataTests
{
    [Fact]
    public void AddMailMcpServer_AdvertisesTheGetEmailContentToolUnderItsProtocolName()
    {
        // Arrange, Act
        var advertisedTool = AdvertisedGetEmailContentTool();

        // Assert
        Assert.Equal("get_email_content", advertisedTool.Name);
        Assert.Equal("Get email content", advertisedTool.Title);
    }

    /// <summary>The four hints the architecture draft requires of every read-only tool.</summary>
    [Fact]
    public void AddMailMcpServer_AdvertisesTheReadOnlyLocalStateAnnotations()
    {
        // Arrange, Act
        var annotations = AdvertisedGetEmailContentTool().Annotations;

        // Assert
        Assert.NotNull(annotations);
        Assert.True(annotations.ReadOnlyHint);
        Assert.False(annotations.DestructiveHint);
        Assert.True(annotations.IdempotentHint);
        Assert.False(annotations.OpenWorldHint);
    }

    /// <summary>A model reads this before it calls anything, so it states both what the tool serves and what it never returns.</summary>
    [Fact]
    public void AddMailMcpServer_AdvertisesADescriptionStatingTheLocalReadOnlyBoundsOfTheTool()
    {
        // Arrange, Act
        var description = AdvertisedGetEmailContentTool().Description;

        // Assert
        Assert.NotNull(description);
        Assert.Contains("local", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("attachment content is never returned", description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddMailMcpServer_AdvertisesTheEmailIdentifierAndTheHtmlFlagAsInputSchemaProperties()
    {
        // Arrange
        string[] expectedProperties = ["storedEmailId", "includeSanitizedHtml"];

        // Act
        var advertisedProperties = AdvertisedGetEmailContentTool()
            .InputSchema
            .GetProperty("properties")
            .EnumerateObject()
            .Select(property => property.Name)
            .ToArray();

        // Assert
        Assert.Equal([.. expectedProperties.Order(StringComparer.Ordinal)], [.. advertisedProperties.Order(StringComparer.Ordinal)]);
    }

    /// <summary>An argument nobody can interpret is an argument a model guesses at, so every one carries its own description.</summary>
    [Fact]
    public void AddMailMcpServer_DescribesEveryInputSchemaProperty()
    {
        // Arrange, Act
        var describedProperties = AdvertisedGetEmailContentTool()
            .InputSchema
            .GetProperty("properties")
            .EnumerateObject()
            .Select(property => (property.Name, HasDescription: property.Value.TryGetProperty("description", out var description) && description.GetString()?.Length > 20))
            .ToArray();

        // Assert
        Assert.All(describedProperties, property => Assert.True(property.HasDescription, $"'{property.Name}' carries no usable description."));
    }

    /// <summary>The email is named rather than defaulted, so a call that names none is refused by the schema instead of answering about some email.</summary>
    [Fact]
    public void AddMailMcpServer_AdvertisesTheEmailIdentifierAsRequiredAndTheHtmlFlagAsOptional()
    {
        // Arrange, Act
        var inputSchema = AdvertisedGetEmailContentTool().InputSchema;

        // Assert
        var requiredProperties = inputSchema.GetProperty("required").EnumerateArray().Select(value => value.ToString()).ToArray();
        Assert.Equal(["storedEmailId"], requiredProperties);
    }

    /// <summary>The cancellation token the tool takes is the host's concern and must never become a protocol argument.</summary>
    [Fact]
    public void AddMailMcpServer_DoesNotAdvertiseTheCancellationTokenAsAnArgument()
    {
        // Arrange, Act
        var advertisedProperties = AdvertisedGetEmailContentTool().InputSchema.GetProperty("properties");

        // Assert
        Assert.False(advertisedProperties.TryGetProperty("cancellationToken", out _));
    }

    [Fact]
    public void AddMailMcpServer_AdvertisesTheResultShapeAsAnOutputSchema()
    {
        // Arrange, Act
        var outputSchema = AdvertisedGetEmailContentTool().OutputSchema;

        // Assert
        Assert.NotNull(outputSchema);
        var properties = outputSchema.Value.GetProperty("properties");
        Assert.True(properties.TryGetProperty("headers", out _));
        Assert.True(properties.TryGetProperty("body", out _));
        Assert.True(properties.TryGetProperty("attachments", out _));
        Assert.True(properties.TryGetProperty("attachmentCounts", out _));
        Assert.True(properties.TryGetProperty("remoteFlags", out _));
    }

    private static Tool AdvertisedGetEmailContentTool() =>
        RegisteredMcpToolSurface.AdvertisedTool(GetEmailContentTool.ToolName);
}
