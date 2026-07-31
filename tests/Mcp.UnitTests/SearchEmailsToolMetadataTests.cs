// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Mcp.Tools;
using ModelContextProtocol.Protocol;
using Xunit;

namespace MailMcp.Mcp.UnitTests;

/// <summary>Covers the descriptor MailMcp advertises for <c>search_emails</c>.</summary>
/// <remarks>
/// The annotations are contract metadata rather than documentation: a client decides whether a tool is safe to call,
/// safe to retry, and confined to local state by reading them before it calls anything. <c>openWorldHint</c> matters
/// most on this tool, because a search is the one a model reaches for when it does not know where an answer is and would
/// otherwise be entitled to assume the server went looking for it.
/// </remarks>
public sealed class SearchEmailsToolMetadataTests
{
    [Fact]
    public void AddMailMcpServer_AdvertisesTheSearchEmailsToolUnderItsProtocolName()
    {
        // Arrange, Act
        var advertisedTool = AdvertisedSearchEmailsTool();

        // Assert
        Assert.Equal("search_emails", advertisedTool.Name);
        Assert.Equal("Search emails", advertisedTool.Title);
    }

    /// <summary>The four hints the architecture draft requires of every read-only tool.</summary>
    [Fact]
    public void AddMailMcpServer_AdvertisesTheReadOnlyLocalStateAnnotations()
    {
        // Arrange, Act
        var annotations = AdvertisedSearchEmailsTool().Annotations;

        // Assert
        Assert.NotNull(annotations);
        Assert.True(annotations.ReadOnlyHint);
        Assert.False(annotations.DestructiveHint);
        Assert.True(annotations.IdempotentHint);
        Assert.False(annotations.OpenWorldHint);
    }

    /// <summary>A model reads this to decide whether the tool answers its question, so it states what retrieval can and cannot find.</summary>
    [Fact]
    public void AddMailMcpServer_AdvertisesADescriptionStatingTheLocalLexicalBoundsOfTheTool()
    {
        // Arrange, Act
        var description = AdvertisedSearchEmailsTool().Description;

        // Assert
        Assert.NotNull(description);
        Assert.Contains("local", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("lexical", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("50", description, StringComparison.Ordinal);
    }

    [Fact]
    public void AddMailMcpServer_AdvertisesTheQueryAndEveryFilterAsAnInputSchemaProperty()
    {
        // Arrange
        string[] expectedProperties =
        [
            "queryText",
            "accountIds",
            "folderAliases",
            "senderAddress",
            "recipientAddress",
            "subjectFragment",
            "receivedOnOrAfter",
            "receivedBefore",
            "isRemotelySeen",
            "hasAttachments",
            "resultLimit",
        ];

        // Act
        var advertisedProperties = AdvertisedSearchEmailsTool()
            .InputSchema
            .GetProperty("properties")
            .EnumerateObject()
            .Select(property => property.Name)
            .ToArray();

        // Assert
        Assert.Equal([.. expectedProperties.Order(StringComparer.Ordinal)], [.. advertisedProperties.Order(StringComparer.Ordinal)]);
    }

    /// <summary>The snippet bounds are a deployment control, so no argument may exist through which a caller could raise them.</summary>
    [Fact]
    public void AddMailMcpServer_AdvertisesNoArgumentThatWidensWhatOneResultShowsOfAMessage()
    {
        // Arrange
        string[] snippetArguments = ["snippetsPerEmail", "wordsPerSnippet", "snippetLength", "includeBody"];

        // Act
        var advertisedProperties = AdvertisedSearchEmailsTool().InputSchema.GetProperty("properties");

        // Assert
        Assert.All(
            snippetArguments,
            argument => Assert.False(
                advertisedProperties.TryGetProperty(argument, out _),
                $"'{argument}' is advertised, which would let a caller widen a deployment-wide privacy bound."));
    }

    /// <summary>The text is the one argument a search cannot be called without, so the schema has to say so.</summary>
    [Fact]
    public void AddMailMcpServer_AdvertisesTheQueryTextAsTheOneRequiredArgument()
    {
        // Arrange, Act
        var required = AdvertisedSearchEmailsTool()
            .InputSchema
            .GetProperty("required")
            .EnumerateArray()
            .Select(value => value.ToString())
            .ToArray();

        // Assert
        Assert.Equal(["queryText"], required);
    }

    /// <summary>An argument nobody can interpret is an argument a model guesses at, so every one carries its own description.</summary>
    [Fact]
    public void AddMailMcpServer_DescribesEveryInputSchemaProperty()
    {
        // Arrange, Act
        var describedProperties = AdvertisedSearchEmailsTool()
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
    public void AddMailMcpServer_DoesNotAdvertiseTheCancellationTokenAsAnArgument()
    {
        // Arrange, Act
        var advertisedProperties = AdvertisedSearchEmailsTool().InputSchema.GetProperty("properties");

        // Assert
        Assert.False(advertisedProperties.TryGetProperty("cancellationToken", out _));
    }

    [Fact]
    public void AddMailMcpServer_AdvertisesTheResultShapeAsAnOutputSchema()
    {
        // Arrange, Act
        var outputSchema = AdvertisedSearchEmailsTool().OutputSchema;

        // Assert
        Assert.NotNull(outputSchema);
        var properties = outputSchema.Value.GetProperty("properties");
        Assert.True(properties.TryGetProperty("matches", out _));
        Assert.True(properties.TryGetProperty("retrievalMode", out _));
        Assert.True(properties.TryGetProperty("folderFreshness", out _));
    }

    /// <summary>
    /// The retrieval mode is advertised as a closed set of named values, so a client can write a branch on it today and
    /// see the later hybrid work widen the set rather than reshape the response.
    /// </summary>
    [Fact]
    public void AddMailMcpServer_AdvertisesTheRetrievalModeAsItsNamedValues()
    {
        // Arrange
        var outputSchema = AdvertisedSearchEmailsTool().OutputSchema;

        // Act
        Assert.NotNull(outputSchema);
        var retrievalMode = outputSchema.Value.GetProperty("properties").GetProperty("retrievalMode");

        // Assert
        var advertisedValues = retrievalMode.GetProperty("enum").EnumerateArray().Select(value => value.ToString()).ToArray();
        Assert.Equal(["lexical"], advertisedValues);
    }

    private static Tool AdvertisedSearchEmailsTool() =>
        RegisteredMcpToolSurface.AdvertisedTool(SearchEmailsTool.ToolName);
}
