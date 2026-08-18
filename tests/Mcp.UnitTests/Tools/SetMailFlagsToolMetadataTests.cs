// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Mcp.Tools;
using MailFathom.Mcp.UnitTests.TestDoubles;
using ModelContextProtocol.Protocol;
using Xunit;

namespace MailFathom.Mcp.UnitTests.Tools;

/// <summary>Covers the descriptor MailFathom advertises for <c>set_mail_flags</c>.</summary>
/// <remarks>
/// It is the one tool on this surface whose annotations do not say read-only, so what they do say is the contract a
/// client decides by: that the call changes state, that it is safe to retry, and that what it changes is not confined
/// to this process. The description carries the two things no annotation can — that the answer is a record rather than
/// a mailbox that has already changed, and that a replacement states the whole keyword set.
/// </remarks>
public sealed class SetMailFlagsToolMetadataTests
{
    [Fact]
    public void AddMailFathomServer_AdvertisesTheSetMailFlagsToolUnderItsProtocolName()
    {
        // Arrange, Act
        var advertisedTool = AdvertisedSetMailFlagsTool();

        // Assert
        Assert.Equal("set_mail_flags", advertisedTool.Name);
        Assert.Equal("Set mail flags", advertisedTool.Title);
    }

    /// <summary>A writing tool that is neither destructive nor unsafe to repeat, and whose effect leaves this process.</summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesTheWritingIdempotentOpenWorldAnnotations()
    {
        // Arrange, Act
        var annotations = AdvertisedSetMailFlagsTool().Annotations;

        // Assert
        Assert.NotNull(annotations);
        Assert.False(annotations.ReadOnlyHint);
        Assert.False(annotations.DestructiveHint);
        Assert.True(annotations.IdempotentHint);
        Assert.True(annotations.OpenWorldHint);
    }

    /// <summary>What comes back is a record, and a caller told otherwise would report a star that is not on the message yet.</summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesADescriptionStatingThatTheChangeIsRecordedRatherThanApplied()
    {
        // Arrange, Act
        var description = AdvertisedSetMailFlagsTool().Description;

        // Assert
        Assert.NotNull(description);
        Assert.Contains("written down durably", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("changeRecordId", description, StringComparison.Ordinal);
    }

    /// <summary>A replacement can drop labels the caller never saw, which is the one call worth a second thought.</summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesADescriptionStatingWhatAKeywordReplacementDoes()
    {
        // Arrange, Act
        var description = AdvertisedSetMailFlagsTool().Description;

        // Assert
        Assert.NotNull(description);
        Assert.Contains("states the whole keyword set", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reversible", description, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The three values are the whole of what this surface writes, so the bound is advertised rather than discovered.</summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesADescriptionStatingWhatCannotBeWritten()
    {
        // Arrange, Act
        var description = AdvertisedSetMailFlagsTool().Description;

        // Assert
        Assert.NotNull(description);
        Assert.Contains("never sets the answered or draft flags", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("never deletes mail", description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddMailFathomServer_AdvertisesTheEmailTheThreeValuesAndTheRequestIdentityAsInputSchemaProperties()
    {
        // Arrange
        string[] expectedProperties = ["storedEmailId", "seen", "flagged", "keywordChange", "keywords", "requestId"];

        // Act
        var advertisedProperties = AdvertisedSetMailFlagsTool()
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

    /// <summary>An argument nobody can interpret is an argument a model guesses at, so every one carries its own description.</summary>
    [Fact]
    public void AddMailFathomServer_DescribesEveryInputSchemaProperty()
    {
        // Arrange, Act
        var describedProperties = AdvertisedSetMailFlagsTool()
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

    /// <summary>The email is the one thing every call has to name; which values it writes is the call's own choice.</summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesTheEmailAloneAsRequired()
    {
        // Arrange, Act
        var inputSchema = AdvertisedSetMailFlagsTool().InputSchema;

        // Assert
        var requiredProperties = inputSchema
            .GetProperty("required")
            .EnumerateArray()
            .Select(value => value.GetString());
        Assert.Equal(["storedEmailId"], requiredProperties);
    }

    /// <summary>The three directions are a closed set, so a client reads them from the schema rather than from the prose.</summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesTheKeywordDirectionsUnderTheirPublishedSpellings()
    {
        // Arrange, Act
        var keywordChange = AdvertisedSetMailFlagsTool()
            .InputSchema
            .GetProperty("properties")
            .GetProperty("keywordChange")
            .GetRawText();

        // Assert
        Assert.Contains("\"add\"", keywordChange, StringComparison.Ordinal);
        Assert.Contains("\"remove\"", keywordChange, StringComparison.Ordinal);
        Assert.Contains("\"replace\"", keywordChange, StringComparison.Ordinal);
    }

    /// <summary>Several keywords per call is what makes one triage one call, so the argument is advertised as a list.</summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesTheKeywordsAsAListOfStrings()
    {
        // Arrange, Act
        var keywords = AdvertisedSetMailFlagsTool()
            .InputSchema
            .GetProperty("properties")
            .GetProperty("keywords");

        // Assert
        Assert.Equal(["array", "null"], TypesOf(keywords));
        Assert.Contains("string", TypesOf(keywords.GetProperty("items")), StringComparer.Ordinal);
    }

    /// <summary>The cancellation token the tool takes is the host's concern and must never become a protocol argument.</summary>
    [Fact]
    public void AddMailFathomServer_DoesNotAdvertiseTheCancellationTokenAsAnArgument()
    {
        // Arrange, Act
        var advertisedProperties = AdvertisedSetMailFlagsTool().InputSchema.GetProperty("properties");

        // Assert
        Assert.False(advertisedProperties.TryGetProperty("cancellationToken", out _));
    }

    /// <summary>What a caller writes against is the record, so the record identity and its state are part of the contract.</summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesTheRecordedChangesAsAnOutputSchema()
    {
        // Arrange, Act
        var outputSchema = AdvertisedSetMailFlagsTool().OutputSchema;

        // Assert
        Assert.NotNull(outputSchema);
        var advertisedSchema = outputSchema.Value.GetRawText();
        Assert.Equal(
            "array",
            outputSchema.Value.GetProperty("properties").GetProperty("recordedChanges").GetProperty("type").GetString());
        Assert.Contains("changeRecordId", advertisedSchema, StringComparison.Ordinal);
        Assert.Contains("folderAlias", advertisedSchema, StringComparison.Ordinal);
    }

    /// <summary>Reads the types a property admits, which an argument a caller may omit states as a list rather than as one name.</summary>
    private static string[] TypesOf(JsonElement property)
    {
        var type = property.GetProperty("type");

        return type.ValueKind is JsonValueKind.Array
            ? [.. type.EnumerateArray().Select(value => value.GetString() ?? string.Empty)]
            : [type.GetString() ?? string.Empty];
    }

    private static Tool AdvertisedSetMailFlagsTool() =>
        RegisteredMcpToolSurface.AdvertisedTool(SetMailFlagsTool.ToolName);
}
