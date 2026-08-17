// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Mcp.Tools.Contacts;
using MailFathom.Mcp.UnitTests.TestDoubles;
using ModelContextProtocol.Protocol;
using Xunit;

namespace MailFathom.Mcp.UnitTests.Tools.Contacts;

/// <summary>Covers the descriptors MailFathom advertises for the five contact tools.</summary>
/// <remarks>
/// The annotations carry more weight here than anywhere else on this surface, because these are the first tools that
/// change state: a client decides from them whether it may call one unattended, and <c>delete_contact</c> is the clearest
/// destructive tool the deployment has. Asserting the advertised descriptor is therefore asserting a promise, and a
/// descriptor that drifts is a broken promise no other test would notice.
/// </remarks>
public sealed class ContactToolMetadataTests
{
    [Theory]
    [InlineData(ListContactsTool.ToolName, "List contacts")]
    [InlineData(GetContactTool.ToolName, "Get contact")]
    [InlineData(CreateContactTool.ToolName, "Create contact")]
    [InlineData(UpdateContactTool.ToolName, "Update contact")]
    [InlineData(DeleteContactTool.ToolName, "Delete contact")]
    public void AddMailFathomServer_AdvertisesEachContactToolUnderItsProtocolNameAndTitle(
        string toolName,
        string expectedTitle)
    {
        // Arrange, Act
        var advertisedTool = AdvertisedTool(toolName);

        // Assert
        Assert.Equal(toolName, advertisedTool.Name);
        Assert.Equal(expectedTitle, advertisedTool.Title);
    }

    /// <summary>The read half carries the same four hints every read tool on this surface carries.</summary>
    [Theory]
    [InlineData(ListContactsTool.ToolName)]
    [InlineData(GetContactTool.ToolName)]
    public void AddMailFathomServer_AdvertisesTheReadOnlyLocalStateAnnotations(string toolName)
    {
        // Arrange, Act
        var annotations = AdvertisedTool(toolName).Annotations;

        // Assert
        Assert.NotNull(annotations);
        Assert.True(annotations.ReadOnlyHint);
        Assert.False(annotations.DestructiveHint);
        Assert.True(annotations.IdempotentHint);
        Assert.False(annotations.OpenWorldHint);
    }

    /// <summary>
    /// The write half changes state and reaches nothing outside this process, which is the pair of hints that tells a
    /// client these are unlike <c>send_email</c> and unlike a read.
    /// </summary>
    [Theory]
    [InlineData(CreateContactTool.ToolName)]
    [InlineData(UpdateContactTool.ToolName)]
    [InlineData(DeleteContactTool.ToolName)]
    public void AddMailFathomServer_AdvertisesTheWriteToolsAsClosedWorldStateChanges(string toolName)
    {
        // Arrange, Act
        var annotations = AdvertisedTool(toolName).Annotations;

        // Assert
        Assert.NotNull(annotations);
        Assert.False(annotations.ReadOnlyHint);
        Assert.False(annotations.OpenWorldHint);
    }

    /// <summary>Erasing somebody is the one act on this surface a client must not auto-approve, and the hint is how it learns that.</summary>
    [Theory]
    [InlineData(CreateContactTool.ToolName, false)]
    [InlineData(UpdateContactTool.ToolName, false)]
    [InlineData(DeleteContactTool.ToolName, true)]
    public void AddMailFathomServer_AdvertisesDestructivenessOnTheErasureAlone(string toolName, bool expectedHint)
    {
        // Arrange, Act
        var annotations = AdvertisedTool(toolName).Annotations;

        // Assert
        Assert.NotNull(annotations);
        Assert.Equal(expectedHint, annotations.DestructiveHint);
    }

    /// <summary>
    /// An amendment and an erasure state the state they want, so repeating one reaches it again; a creation mints an
    /// identity, so the second call records nobody and refuses.
    /// </summary>
    [Theory]
    [InlineData(CreateContactTool.ToolName, false)]
    [InlineData(UpdateContactTool.ToolName, true)]
    [InlineData(DeleteContactTool.ToolName, true)]
    public void AddMailFathomServer_AdvertisesIdempotencyPerWhatTheToolRepeats(string toolName, bool expectedHint)
    {
        // Arrange, Act
        var annotations = AdvertisedTool(toolName).Annotations;

        // Assert
        Assert.NotNull(annotations);
        Assert.Equal(expectedHint, annotations.IdempotentHint);
    }

    /// <summary>A model reads the description before it calls anything, so each states what the tool does to somebody's data.</summary>
    [Theory]
    [InlineData(ListContactsTool.ToolName, "contact book")]
    [InlineData(GetContactTool.ToolName, "exactly one of the two")]
    [InlineData(CreateContactTool.ToolName, "contact book")]
    [InlineData(UpdateContactTool.ToolName, "whole record")]
    [InlineData(DeleteContactTool.ToolName, "cannot be undone")]
    public void AddMailFathomServer_AdvertisesADescriptionStatingWhatTheToolDoes(string toolName, string expectedPhrase)
    {
        // Arrange, Act
        var description = AdvertisedTool(toolName).Description;

        // Assert
        Assert.NotNull(description);
        Assert.Contains(expectedPhrase, description, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Every argument is a top-level property, so a caller reads what it may send from the schema alone.</summary>
    [Theory]
    [InlineData(ListContactsTool.ToolName, "search")]
    [InlineData(ListContactsTool.ToolName, "origin")]
    [InlineData(ListContactsTool.ToolName, "pageSize")]
    [InlineData(ListContactsTool.ToolName, "cursor")]
    [InlineData(GetContactTool.ToolName, "contactId")]
    [InlineData(GetContactTool.ToolName, "address")]
    [InlineData(CreateContactTool.ToolName, "displayName")]
    [InlineData(CreateContactTool.ToolName, "addresses")]
    [InlineData(CreateContactTool.ToolName, "preferredAddress")]
    [InlineData(CreateContactTool.ToolName, "note")]
    [InlineData(UpdateContactTool.ToolName, "contactId")]
    [InlineData(UpdateContactTool.ToolName, "addresses")]
    [InlineData(DeleteContactTool.ToolName, "contactId")]
    public void AddMailFathomServer_AdvertisesEachArgumentAsANamedInputProperty(string toolName, string argumentName)
    {
        // Arrange, Act
        var inputSchema = AdvertisedTool(toolName).InputSchema;

        // Assert
        Assert.Contains($"\"{argumentName}\"", inputSchema.ToString(), StringComparison.Ordinal);
    }

    /// <summary>The output schema is the contract, so the parts of a person a caller acts on are part of what is advertised.</summary>
    [Theory]
    [InlineData(ListContactsTool.ToolName, "contacts")]
    [InlineData(ListContactsTool.ToolName, "nextCursor")]
    [InlineData(ListContactsTool.ToolName, "preferredAddress")]
    [InlineData(GetContactTool.ToolName, "contact")]
    [InlineData(GetContactTool.ToolName, "displayName")]
    [InlineData(CreateContactTool.ToolName, "state")]
    [InlineData(CreateContactTool.ToolName, "addressHolderContactId")]
    [InlineData(UpdateContactTool.ToolName, "state")]
    [InlineData(DeleteContactTool.ToolName, "wasHeld")]
    [InlineData(DeleteContactTool.ToolName, "addressesErased")]
    public void AddMailFathomServer_AdvertisesTheAnswerShapeInItsOutputSchema(string toolName, string expectedProperty)
    {
        // Arrange, Act
        var outputSchema = AdvertisedTool(toolName).OutputSchema;

        // Assert
        Assert.NotNull(outputSchema);
        Assert.Contains($"\"{expectedProperty}\"", outputSchema.ToString(), StringComparison.Ordinal);
    }

    /// <summary>The enumerations travel as names rather than as ordinals, so a client never has to know which number MailFathom gave one.</summary>
    [Theory]
    [InlineData(ListContactsTool.ToolName, "asserted")]
    [InlineData(ListContactsTool.ToolName, "collected")]
    [InlineData(CreateContactTool.ToolName, "written")]
    [InlineData(CreateContactTool.ToolName, "notFound")]
    [InlineData(CreateContactTool.ToolName, "addressHeldByAnotherContact")]
    [InlineData(CreateContactTool.ToolName, "contactWasCollected")]
    public void AddMailFathomServer_AdvertisesTheEnumerationsAsNames(string toolName, string expectedValue)
    {
        // Arrange, Act
        var advertisedTool = AdvertisedTool(toolName);
        var advertised = advertisedTool.InputSchema.ToString() + advertisedTool.OutputSchema?.ToString();

        // Assert
        Assert.Contains($"\"{expectedValue}\"", advertised, StringComparison.Ordinal);
    }

    /// <summary>Nothing about how the book is stored is part of the contract, so a later field cannot arrive unnoticed.</summary>
    [Theory]
    [InlineData("displayNameSortKey")]
    [InlineData("normalizedAddress")]
    [InlineData("concurrencyVersion")]
    public void AddMailFathomServer_AdvertisesNoStorageDetailInTheContactShape(string forbiddenProperty)
    {
        // Arrange, Act
        var outputSchema = AdvertisedTool(GetContactTool.ToolName).OutputSchema;

        // Assert
        Assert.NotNull(outputSchema);
        Assert.DoesNotContain($"\"{forbiddenProperty}\"", outputSchema.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static Tool AdvertisedTool(string toolName) => RegisteredMcpToolSurface.AdvertisedTool(toolName);
}
