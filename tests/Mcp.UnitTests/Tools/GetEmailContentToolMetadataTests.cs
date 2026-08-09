// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Mcp.Tools;
using MailFathom.Mcp.UnitTests.TestDoubles;
using ModelContextProtocol.Protocol;
using Xunit;

namespace MailFathom.Mcp.UnitTests.Tools;

/// <summary>Covers the descriptor MailFathom advertises for <c>get_email_content</c>.</summary>
/// <remarks>
/// The annotations are contract metadata rather than documentation: a client decides whether a tool is safe to call,
/// safe to retry, and confined to local state by reading them before it calls anything. The description matters as much
/// here as on any tool and more than on most, because it is where a model learns that this surface returns message
/// content in full, and — for a call that asks to describe the attachments — the files themselves.
/// </remarks>
public sealed class GetEmailContentToolMetadataTests
{
    [Fact]
    public void AddMailFathomServer_AdvertisesTheGetEmailContentToolUnderItsProtocolName()
    {
        // Arrange, Act
        var advertisedTool = AdvertisedGetEmailContentTool();

        // Assert
        Assert.Equal("get_email_content", advertisedTool.Name);
        Assert.Equal("Get email content", advertisedTool.Title);
    }

    /// <summary>The four hints the architecture draft requires of every read-only tool.</summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesTheReadOnlyLocalStateAnnotations()
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

    /// <summary>A model reads this before it calls anything, so it states what the tool serves and what bounds it.</summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesADescriptionStatingTheLocalReadOnlyBoundsOfTheTool()
    {
        // Arrange, Act
        var description = AdvertisedGetEmailContentTool().Description;

        // Assert
        Assert.NotNull(description);
        Assert.Contains("local", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("never marks", description, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A model decides whether to ask for attachments from this text alone, so it has to say that the content comes
    /// back, in what form, and that a bound can withhold it. A caller told only that attachments exist would ask for
    /// them expecting names and receive a response several times the size it planned for.
    /// </summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesADescriptionStatingThatAttachmentContentComesBackAsBase64AndIsBounded()
    {
        // Arrange, Act
        var description = AdvertisedGetEmailContentTool().Description;

        // Assert
        Assert.NotNull(description);
        Assert.Contains("contentBase64", description, StringComparison.Ordinal);
        Assert.Contains("bounded per attachment", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("never returned in part", description, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A model reads the count bound and the attachment default here, so both are part of what is advertised.</summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesADescriptionStatingTheCountBoundAndTheAttachmentDefault()
    {
        // Arrange, Act
        var description = AdvertisedGetEmailContentTool().Description;

        // Assert
        Assert.NotNull(description);
        Assert.Contains("up to 10 emails", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("includeAttachmentContent", description, StringComparison.Ordinal);

        // The default is that attachments are described and not decoded, which is what stops a model from asking for
        // content merely to learn what a file is called.
        Assert.Contains("described by file name, media type, and size", description, StringComparison.Ordinal);
        Assert.Contains("not returned by default", description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddMailFathomServer_AdvertisesTheEmailIdentifiersAndTheTwoFlagsAsInputSchemaProperties()
    {
        // Arrange
        string[] expectedProperties = ["storedEmailIds", "includeSanitizedHtml", "includeAttachmentContent"];

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
    public void AddMailFathomServer_DescribesEveryInputSchemaProperty()
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

    /// <summary>The emails are named rather than defaulted, so a call that names none is refused instead of answering about some email.</summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesTheEmailIdentifiersAsRequiredAndBothFlagsAsOptional()
    {
        // Arrange, Act
        var inputSchema = AdvertisedGetEmailContentTool().InputSchema;

        // Assert
        var requiredProperties = inputSchema.GetProperty("required").EnumerateArray().Select(value => value.ToString()).ToArray();
        Assert.Equal(["storedEmailIds"], requiredProperties);
    }

    /// <summary>Several emails per call is the whole point of the contract, so the argument is advertised as a list.</summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesTheEmailIdentifiersAsAListOfStrings()
    {
        // Arrange, Act
        var storedEmailIds = AdvertisedGetEmailContentTool()
            .InputSchema
            .GetProperty("properties")
            .GetProperty("storedEmailIds");

        // Assert
        Assert.Equal("array", storedEmailIds.GetProperty("type").GetString());
        Assert.Equal("string", storedEmailIds.GetProperty("items").GetProperty("type").GetString());
    }

    /// <summary>The cancellation token the tool takes is the host's concern and must never become a protocol argument.</summary>
    [Fact]
    public void AddMailFathomServer_DoesNotAdvertiseTheCancellationTokenAsAnArgument()
    {
        // Arrange, Act
        var advertisedProperties = AdvertisedGetEmailContentTool().InputSchema.GetProperty("properties");

        // Assert
        Assert.False(advertisedProperties.TryGetProperty("cancellationToken", out _));
    }

    [Fact]
    public void AddMailFathomServer_AdvertisesTheResultShapeAsAnOutputSchema()
    {
        // Arrange, Act
        var outputSchema = AdvertisedGetEmailContentTool().OutputSchema;

        // Assert
        Assert.NotNull(outputSchema);
        var emails = outputSchema.Value.GetProperty("properties").GetProperty("emails");
        Assert.Equal("array", emails.GetProperty("type").GetString());
    }

    /// <summary>The per-email entry is what a client writes against, so its two mutually exclusive halves are advertised.</summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesEachEmailEntryAsEitherContentOrAFailure()
    {
        // Arrange, Act
        var outputSchema = AdvertisedGetEmailContentTool().OutputSchema;

        // Assert
        Assert.NotNull(outputSchema);
        var advertisedProperties = SchemaText(outputSchema.Value);
        Assert.Contains("storedEmailId", advertisedProperties, StringComparison.Ordinal);
        Assert.Contains("content", advertisedProperties, StringComparison.Ordinal);
        Assert.Contains("failure", advertisedProperties, StringComparison.Ordinal);
        Assert.Contains("attachmentCounts", advertisedProperties, StringComparison.Ordinal);
        Assert.Contains("truncatedBy", advertisedProperties, StringComparison.Ordinal);
    }

    /// <summary>The member names are the wire values, so a rename inside the boundary must fail the build rather than the client.</summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesTheTruncationCausesUnderTheirPublishedSpellings()
    {
        // Arrange, Act
        var outputSchema = AdvertisedGetEmailContentTool().OutputSchema;

        // Assert
        Assert.NotNull(outputSchema);
        var advertisedSchema = SchemaText(outputSchema.Value);
        Assert.Contains("\"none\"", advertisedSchema, StringComparison.Ordinal);
        Assert.Contains("\"bodyCharacterLimit\"", advertisedSchema, StringComparison.Ordinal);
        Assert.Contains("\"readCharacterBudget\"", advertisedSchema, StringComparison.Ordinal);
    }

    /// <summary>The same holds for the attachment states, which is how a client tells a withheld file from a missing one.</summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesTheAttachmentContentStatesUnderTheirPublishedSpellings()
    {
        // Arrange, Act
        var outputSchema = AdvertisedGetEmailContentTool().OutputSchema;

        // Assert
        Assert.NotNull(outputSchema);
        var advertisedSchema = SchemaText(outputSchema.Value);
        Assert.Contains("contentBase64", advertisedSchema, StringComparison.Ordinal);
        Assert.Contains("\"notRequested\"", advertisedSchema, StringComparison.Ordinal);
        Assert.Contains("\"returned\"", advertisedSchema, StringComparison.Ordinal);
        Assert.Contains("\"exceededAttachmentByteLimit\"", advertisedSchema, StringComparison.Ordinal);
        Assert.Contains("\"readByteBudgetExhausted\"", advertisedSchema, StringComparison.Ordinal);
    }

    private static string SchemaText(JsonElement schema) => schema.GetRawText();

    private static Tool AdvertisedGetEmailContentTool() =>
        RegisteredMcpToolSurface.AdvertisedTool(GetEmailContentTool.ToolName);
}
