// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Mcp.Tools;
using MailFathom.Mcp.UnitTests.TestDoubles;
using ModelContextProtocol.Protocol;
using Xunit;

namespace MailFathom.Mcp.UnitTests.Tools;

/// <summary>Covers the descriptor MailFathom advertises for <c>list_emails</c>.</summary>
/// <remarks>
/// The annotations are contract metadata rather than documentation: a client decides whether a tool is safe to call, safe
/// to retry, and confined to local state by reading them before it calls anything. Asserting the advertised descriptor is
/// therefore asserting a promise, and a descriptor that drifts is a broken promise no other test would notice.
/// </remarks>
public sealed class ListEmailsToolMetadataTests
{
    [Fact]
    public void AddMailFathomServer_AdvertisesTheListEmailsToolUnderItsProtocolName()
    {
        // Arrange, Act
        var advertisedTool = AdvertisedListEmailsTool();

        // Assert
        Assert.Equal("list_emails", advertisedTool.Name);
        Assert.Equal("List emails", advertisedTool.Title);
    }

    /// <summary>The four hints the architecture draft requires of every read-only tool.</summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesTheReadOnlyLocalStateAnnotations()
    {
        // Arrange, Act
        var annotations = AdvertisedListEmailsTool().Annotations;

        // Assert
        Assert.NotNull(annotations);
        Assert.True(annotations.ReadOnlyHint);
        Assert.False(annotations.DestructiveHint);
        Assert.True(annotations.IdempotentHint);
        Assert.False(annotations.OpenWorldHint);
    }

    /// <summary>A description is what a model reads to decide whether the tool answers its question at all.</summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesADescriptionStatingTheLocalReadOnlyBoundsOfTheTool()
    {
        // Arrange, Act
        var description = AdvertisedListEmailsTool().Description;

        // Assert
        Assert.NotNull(description);
        Assert.Contains("local", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("100", description, StringComparison.Ordinal);
    }

    [Fact]
    public void AddMailFathomServer_AdvertisesEveryFilterAsAnInputSchemaProperty()
    {
        // Arrange
        string[] expectedProperties =
        [
            "accounts",
            "folders",
            "senderAddress",
            "recipientAddress",
            "subjectFragment",
            "receivedOnOrAfter",
            "receivedBefore",
            "isRemotelySeen",
            "isRemotelyFlagged",
            "keyword",
            "hasAttachments",
            "includeJunkMail",
            "direction",
            "pageSize",
            "cursor",
        ];

        // Act
        var advertisedProperties = AdvertisedListEmailsTool()
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
        var describedProperties = AdvertisedListEmailsTool()
            .InputSchema
            .GetProperty("properties")
            .EnumerateObject()
            .Select(property => (property.Name, HasDescription: property.Value.TryGetProperty("description", out var description) && description.GetString()?.Length > 20))
            .ToArray();

        // Assert
        Assert.All(describedProperties, property => Assert.True(property.HasDescription, $"'{property.Name}' carries no usable description."));
    }

    /// <summary>The cancellation token the tool takes is the host's concern and must never become a protocol argument.</summary>
    [Fact]
    public void AddMailFathomServer_DoesNotAdvertiseTheCancellationTokenAsAnArgument()
    {
        // Arrange, Act
        var advertisedProperties = AdvertisedListEmailsTool().InputSchema.GetProperty("properties");

        // Assert
        Assert.False(advertisedProperties.TryGetProperty("cancellationToken", out _));
    }

    /// <summary>An enumeration travels as its name, so a client reads a value rather than this assembly's declaration order.</summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesTheReadingDirectionAsItsNamedValues()
    {
        // Arrange, Act
        var direction = AdvertisedListEmailsTool()
            .InputSchema
            .GetProperty("properties")
            .GetProperty("direction");

        // Assert
        var advertisedValues = direction.GetProperty("enum").EnumerateArray().Select(value => value.ToString()).ToArray();
        Assert.Equal(["newestFirst", "oldestFirst"], advertisedValues);
    }

    /// <summary>
    /// The availability states are wire values as much as the reading direction is, and a rename of either member would
    /// change the published contract silently. Asserting the advertised spellings is what makes the member names safe to
    /// carry the protocol identity, so every enumeration this surface publishes is pinned rather than only the ones on
    /// the input side.
    /// </summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesTheContentAvailabilityAsItsNamedValues()
    {
        // Arrange
        var outputSchema = AdvertisedListEmailsTool().OutputSchema;

        // Act
        Assert.NotNull(outputSchema);
        var advertisedValues = AdvertisedEnumValues(outputSchema.Value, "contentAvailability");

        // Assert
        Assert.Equal(["available", "exceededSizeLimit", "awaitingStorageHeadroom"], advertisedValues);
    }

    [Fact]
    public void AddMailFathomServer_AdvertisesTheResultShapeAsAnOutputSchema()
    {
        // Arrange, Act
        var outputSchema = AdvertisedListEmailsTool().OutputSchema;

        // Assert
        Assert.NotNull(outputSchema);
        var properties = outputSchema.Value.GetProperty("properties");
        Assert.True(properties.TryGetProperty("emails", out _));
        Assert.True(properties.TryGetProperty("nextCursor", out _));
        Assert.True(properties.TryGetProperty("folderFreshness", out _));
    }

    /// <summary>The surface is the five read-only tools of this release, so a sixth arriving unnoticed is a change to the published contract.</summary>
    /// <remarks>
    /// Registration is not advertisement for one of them: <c>ask_mail</c> is registered by every deployment and listed
    /// only by one that can answer, which <see cref="AskMailAdvertisementTests" /> covers where that is decided.
    /// </remarks>
    [Fact]
    public void AddMailFathomServer_RegistersTheAccountsTheListingTheContentTheSearchAndTheAnsweringTool()
    {
        // Arrange, Act
        var registeredNames = RegisteredMcpToolSurface
            .Tools()
            .Select(tool => tool.ProtocolTool.Name)
            .Order(StringComparer.Ordinal);

        // Assert
        Assert.Equal(
            [
                AskMailTool.ToolName,
                GetEmailContentTool.ToolName,
                ListAccountsTool.ToolName,
                ListEmailsTool.ToolName,
                SearchEmailsTool.ToolName,
            ],
            registeredNames);
    }

    /// <summary>Reads the values one named property is advertised as accepting, wherever in the schema it sits.</summary>
    /// <remarks>
    /// The property is searched for rather than navigated to, because how deeply a result type nests and whether the
    /// generator inlines a subschema or publishes it behind a reference are the generator's decisions. What the contract
    /// promises is that a client reading this schema sees these spellings, and that is what the walk asserts.
    /// </remarks>
    private static IReadOnlyList<string> AdvertisedEnumValues(JsonElement schema, string propertyName)
    {
        var declaration = Subschemas(schema)
            .FirstOrDefault(candidate => candidate.Name == propertyName && candidate.Value.TryGetProperty("enum", out _));

        Assert.True(declaration.Value.ValueKind is JsonValueKind.Object, $"'{propertyName}' is advertised nowhere in the schema.");

        return [.. declaration.Value.GetProperty("enum").EnumerateArray().Select(value => value.ToString())];
    }

    /// <summary>Walks every named subschema of a schema document, however deeply the generator nested it.</summary>
    private static IEnumerable<JsonProperty> Subschemas(JsonElement schema)
    {
        if (schema.ValueKind is JsonValueKind.Object)
        {
            foreach (var member in schema.EnumerateObject())
            {
                yield return member;

                foreach (var nested in Subschemas(member.Value))
                {
                    yield return nested;
                }
            }
        }
        else if (schema.ValueKind is JsonValueKind.Array)
        {
            foreach (var nested in schema.EnumerateArray().SelectMany(Subschemas))
            {
                yield return nested;
            }
        }
    }

    private static Tool AdvertisedListEmailsTool() =>
        RegisteredMcpToolSurface.AdvertisedTool(ListEmailsTool.ToolName);
}
