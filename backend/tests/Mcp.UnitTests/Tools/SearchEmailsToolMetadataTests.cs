// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Mcp.Tools;
using MailFathom.Mcp.UnitTests.TestDoubles;
using ModelContextProtocol.Protocol;
using Xunit;

namespace MailFathom.Mcp.UnitTests.Tools;

/// <summary>Covers the descriptor MailFathom advertises for <c>search_emails</c>.</summary>
/// <remarks>
/// The annotations are contract metadata rather than documentation: a client decides whether a tool is safe to call,
/// safe to retry, and confined to local state by reading them before it calls anything. <c>openWorldHint</c> matters
/// most on this tool, because a search is the one a model reaches for when it does not know where an answer is and would
/// otherwise be entitled to assume the server went looking for it.
/// </remarks>
public sealed class SearchEmailsToolMetadataTests
{
    [Fact]
    public void AddMailFathomServer_AdvertisesTheSearchEmailsToolUnderItsProtocolName()
    {
        // Arrange, Act
        var advertisedTool = AdvertisedSearchEmailsTool();

        // Assert
        Assert.Equal("search_emails", advertisedTool.Name);
        Assert.Equal("Search emails", advertisedTool.Title);
    }

    /// <summary>The four hints the tool descriptor conventions require of every read-only tool.</summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesTheReadOnlyLocalStateAnnotations()
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
    public void AddMailFathomServer_AdvertisesADescriptionStatingTheLocalLexicalBoundsOfTheTool()
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
    public void AddMailFathomServer_AdvertisesTheQueryAndEveryFilterAsAnInputSchemaProperty()
    {
        // Arrange
        string[] expectedProperties =
        [
            "queryText",
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
    public void AddMailFathomServer_AdvertisesNoArgumentThatWidensWhatOneResultShowsOfAMessage()
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
    public void AddMailFathomServer_AdvertisesTheQueryTextAsTheOneRequiredArgument()
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

    /// <summary>
    /// Matching compares words rather than translating them, so a caller that words every search in the language it was
    /// asked in reads a multilingual mailbox as one that holds nothing. The schema is where that is said, because the
    /// caller writing the query is the only party that knows which languages the question could be about.
    /// </summary>
    [Fact]
    public void AddMailFathomServer_DescribesTheQueryTextAsWordedInTheLanguageTheMailWasWrittenIn()
    {
        // Arrange, Act
        var queryText = AdvertisedSearchEmailsTool()
            .InputSchema
            .GetProperty("properties")
            .GetProperty("queryText")
            .GetProperty("description")
            .GetString();

        // Assert
        Assert.NotNull(queryText);
        Assert.Contains(
            "in the language it was written in rather than the language of your request",
            queryText,
            StringComparison.Ordinal);
    }

    /// <summary>An argument nobody can interpret is an argument a model guesses at, so every one carries its own description.</summary>
    [Fact]
    public void AddMailFathomServer_DescribesEveryInputSchemaProperty()
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
    public void AddMailFathomServer_DoesNotAdvertiseTheCancellationTokenAsAnArgument()
    {
        // Arrange, Act
        var advertisedProperties = AdvertisedSearchEmailsTool().InputSchema.GetProperty("properties");

        // Assert
        Assert.False(advertisedProperties.TryGetProperty("cancellationToken", out _));
    }

    [Fact]
    public void AddMailFathomServer_AdvertisesTheResultShapeAsAnOutputSchema()
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
    /// The retrieval mode is advertised as a closed set of named values, so a client can branch on it rather than
    /// inferring how a server retrieves from its version. Both values are advertised by every server, because which one
    /// a call reports depends on that call rather than on how the instance is configured.
    /// </summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesTheRetrievalModeAsItsNamedValues()
    {
        // Arrange
        var outputSchema = AdvertisedSearchEmailsTool().OutputSchema;

        // Act
        Assert.NotNull(outputSchema);
        var retrievalMode = outputSchema.Value.GetProperty("properties").GetProperty("retrievalMode");

        // Assert
        var advertisedValues = retrievalMode.GetProperty("enum").EnumerateArray().Select(value => value.ToString()).ToArray();
        Assert.Equal(["lexical", "hybrid"], advertisedValues);
    }

    private static Tool AdvertisedSearchEmailsTool() =>
        RegisteredMcpToolSurface.AdvertisedTool(SearchEmailsTool.ToolName);
}
