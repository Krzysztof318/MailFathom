// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Mcp.Tools;
using MailFathom.Mcp.UnitTests.TestDoubles;
using ModelContextProtocol.Protocol;
using Xunit;

namespace MailFathom.Mcp.UnitTests.Tools;

/// <summary>Covers the descriptor MailFathom advertises for <c>list_accounts</c>.</summary>
/// <remarks>
/// The description carries more weight on this tool than on most, because it is where a model learns that the account
/// filter every other tool takes can be filled in at all, and that either of the two names it returns will do.
/// </remarks>
public sealed class ListAccountsToolMetadataTests
{
    [Fact]
    public void AddMailFathomServer_AdvertisesTheListAccountsToolUnderItsProtocolName()
    {
        // Arrange, Act
        var advertisedTool = AdvertisedListAccountsTool();

        // Assert
        Assert.Equal("list_accounts", advertisedTool.Name);
        Assert.Equal("List accounts", advertisedTool.Title);
    }

    /// <summary>The four hints the tool descriptor conventions require of every read-only tool.</summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesTheReadOnlyLocalStateAnnotations()
    {
        // Arrange, Act
        var annotations = AdvertisedListAccountsTool().Annotations;

        // Assert
        Assert.NotNull(annotations);
        Assert.True(annotations.ReadOnlyHint);
        Assert.False(annotations.DestructiveHint);
        Assert.True(annotations.IdempotentHint);
        Assert.False(annotations.OpenWorldHint);
    }

    /// <summary>A model reads this before it calls anything, so it states what the tool is for and what it never returns.</summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesADescriptionStatingWhatTheToolIsForAndWhatItWithholds()
    {
        // Arrange, Act
        var description = AdvertisedListAccountsTool().Description;

        // Assert
        Assert.NotNull(description);
        Assert.Contains("local", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("display name", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no credential", description, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A client stores one of the two names, so the descriptor states how far either one is unique before the client
    /// decides what to do with it. Both are the owner's own words and neither is a deployment-wide name.
    /// </summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesADescriptionScopingBothNamesToTheirOwner()
    {
        // Arrange, Act
        var description = AdvertisedListAccountsTool().Description;

        // Assert
        Assert.NotNull(description);
        Assert.Contains("unique within that owner", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rather than across the deployment", description, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The output schema travels with a stored value where the tool description does not, so each published name says
    /// on its own that it is unique within its owner and nowhere wider.
    /// </summary>
    [Theory]
    [InlineData("accountId")]
    [InlineData("displayName")]
    public void AddMailFathomServer_AdvertisesEachPublishedNameAsUniqueWithinItsOwner(string publishedName)
    {
        // Arrange, Act
        var outputSchema = AdvertisedListAccountsTool().OutputSchema;

        // Assert
        Assert.NotNull(outputSchema);
        var description = outputSchema.Value
            .GetProperty("properties")
            .GetProperty("accounts")
            .GetProperty("items")
            .GetProperty("properties")
            .GetProperty(publishedName)
            .GetProperty("description")
            .GetString();

        Assert.NotNull(description);
        Assert.Contains("unique within the account's owner", description, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The tool answers about the deployment rather than about a request, so there is nothing for a caller to get wrong.</summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesNoInputSchemaProperty()
    {
        // Arrange, Act
        var inputSchema = AdvertisedListAccountsTool().InputSchema;

        // Assert
        Assert.False(
            inputSchema.TryGetProperty("properties", out var properties) && properties.EnumerateObject().Any(),
            "list_accounts advertises an argument, so a caller can now get one wrong.");
    }

    /// <summary>The output schema is the contract, so the two names and the freshness a caller acts on are part of what is advertised.</summary>
    [Theory]
    [InlineData("accountId")]
    [InlineData("displayName")]
    [InlineData("synchronizationMode")]
    [InlineData("folders")]
    [InlineData("synchronizationEnabled")]
    public void AddMailFathomServer_AdvertisesTheAccountShapeInItsOutputSchema(string expectedProperty)
    {
        // Arrange, Act
        var outputSchema = AdvertisedListAccountsTool().OutputSchema;

        // Assert
        Assert.NotNull(outputSchema);
        Assert.Contains($"\"{expectedProperty}\"", outputSchema.ToString(), StringComparison.Ordinal);
    }

    /// <summary>The modes travel as names rather than as ordinals, so a client never has to know which number MailFathom gave one.</summary>
    [Theory]
    [InlineData("polling")]
    [InlineData("push")]
    public void AddMailFathomServer_AdvertisesTheSynchronizationModesAsNames(string expectedValue)
    {
        // Arrange, Act
        var outputSchema = AdvertisedListAccountsTool().OutputSchema;

        // Assert
        Assert.NotNull(outputSchema);
        Assert.Contains($"\"{expectedValue}\"", outputSchema.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Nothing about how MailFathom reaches a mailbox is part of the contract, so a later field cannot arrive unnoticed.</summary>
    [Theory]
    [InlineData("host")]
    [InlineData("port")]
    [InlineData("userName")]
    [InlineData("password")]
    [InlineData("secret")]
    public void AddMailFathomServer_AdvertisesNoConnectionDetailInItsOutputSchema(string forbiddenProperty)
    {
        // Arrange, Act
        var outputSchema = AdvertisedListAccountsTool().OutputSchema;

        // Assert
        Assert.NotNull(outputSchema);
        Assert.DoesNotContain($"\"{forbiddenProperty}\"", outputSchema.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static Tool AdvertisedListAccountsTool() =>
        RegisteredMcpToolSurface.AdvertisedTool(ListAccountsTool.ToolName);
}
