// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Mcp.Tools;
using MailFathom.Mcp.Tools.Categories;
using MailFathom.Mcp.Tools.Contacts;
using MailFathom.Mcp.Tools.Drafts;
using MailFathom.Mcp.UnitTests.TestDoubles;
using ModelContextProtocol;
using Xunit;

namespace MailFathom.Mcp.UnitTests.Tools.Categories;

/// <summary>Covers what a request is served once the deployment's selection and the client's header have both been read.</summary>
/// <remarks>
/// Everything here is driven through the filters the registration composed, so what is asserted is the pipeline a host
/// has rather than one the test assembled. The caller is granted the whole mail surface throughout and the deployment
/// answers questions, so a tool missing from a listing below is missing for want of a published category and for no
/// other reason.
/// </remarks>
public sealed class McpToolCategoryPublicationTests
{
    /// <summary>The behaviour a deployment has without the setting, which is what makes its arrival cost an operator nothing.</summary>
    [Fact]
    public async Task ListTools_ADeploymentNamingNoCategory_IsServedEveryToolTheSurfacePublishes()
    {
        // Arrange
        await using var provider = RegisteredMcpToolSurface.ComposedPublishing(
            PublishedToolCategorySelection.Of([]));

        // Act
        var listing = await RegisteredMcpToolSurface.ListedToolsAsync(provider);

        // Assert
        Assert.Equal(
            RegisteredMcpToolSurface.Tools().Select(static tool => tool.ProtocolTool.Name).Order(StringComparer.Ordinal),
            listing.Tools.Select(static tool => tool.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ListTools_ADeploymentPublishingTheMailboxAlone_IsServedTheReadingToolsAndNoOther()
    {
        // Arrange
        await using var provider = RegisteredMcpToolSurface.ComposedPublishing(
            PublishedToolCategorySelection.Of([McpToolCategory.Mailbox]));

        // Act
        var listing = await RegisteredMcpToolSurface.ListedToolsAsync(provider);

        // Assert
        Assert.Equal(
            [
                GetEmailContentTool.ToolName,
                ListAccountsTool.ToolName,
                ListEmailsTool.ToolName,
                SearchEmailsTool.ToolName,
            ],
            listing.Tools.Select(static tool => tool.Name).Order(StringComparer.Ordinal));
    }

    /// <summary>The posture the split between the two categories exists for: an agent that composes mail somebody then reads, and dispatches nothing.</summary>
    [Fact]
    public async Task ListTools_ADeploymentPublishingDraftsWithoutSending_IsServedNoToolThatDispatchesAnything()
    {
        // Arrange
        await using var provider = RegisteredMcpToolSurface.ComposedPublishing(
            PublishedToolCategorySelection.Of([McpToolCategory.Drafts]));

        // Act
        var listing = await RegisteredMcpToolSurface.ListedToolsAsync(provider);

        // Assert
        Assert.Equal(
            [
                DeleteDraftTool.ToolName,
                SaveDraftTool.ToolName,
                UpdateDraftTool.ToolName,
            ],
            listing.Tools.Select(static tool => tool.Name).Order(StringComparer.Ordinal));
    }

    /// <summary>A tool the deployment does not publish answers exactly as a name no tool answers to, so a caller learns nothing from asking.</summary>
    [Fact]
    public async Task CallTool_AToolOutsideThePublishedCategories_IsAnsweredAsAnUnknownTool()
    {
        // Arrange
        await using var provider = RegisteredMcpToolSurface.ComposedPublishing(
            PublishedToolCategorySelection.Of([McpToolCategory.Mailbox]));

        // Act
        var refusal = await Assert.ThrowsAsync<McpProtocolException>(() =>
            RegisteredMcpToolSurface.CalledAsync(provider, DeleteContactTool.ToolName));

        // Assert
        Assert.Equal($"Unknown tool: '{DeleteContactTool.ToolName}'", refusal.Message);
        Assert.Equal(McpErrorCode.InvalidParams, refusal.ErrorCode);
    }

    [Fact]
    public async Task CallTool_AToolThePublishedCategoriesCarry_IsNotRefusedByTheFilter()
    {
        // Arrange
        await using var provider = RegisteredMcpToolSurface.ComposedPublishing(
            PublishedToolCategorySelection.Of([McpToolCategory.Mailbox]));

        // Act
        var result = await RegisteredMcpToolSurface.CalledAsync(provider, ListAccountsTool.ToolName);

        // Assert
        Assert.NotNull(result);
    }

    /// <summary>One endpoint serving a client that only reads beside one that does everything, which is what the header is for.</summary>
    [Fact]
    public async Task ListTools_AClientNamingOneCategory_IsServedThatCategoryOutOfEverythingPublished()
    {
        // Arrange
        await using var provider = RegisteredMcpToolSurface.ComposedPublishing(
            PublishedToolCategorySelection.Everything,
            requestedCategories: "contacts");

        // Act
        var listing = await RegisteredMcpToolSurface.ListedToolsAsync(provider);

        // Assert
        Assert.Equal(
            [
                CreateContactTool.ToolName,
                DeleteContactTool.ToolName,
                GetContactTool.ToolName,
                ListContactsTool.ToolName,
                PromoteContactTool.ToolName,
                UpdateContactTool.ToolName,
            ],
            listing.Tools.Select(static tool => tool.Name).Order(StringComparer.Ordinal));
    }

    /// <summary>The claim the design rests on, proved through the pipeline: a header takes away and never grants.</summary>
    [Fact]
    public async Task ListTools_AClientNamingACategoryTheDeploymentExcluded_IsServedNothingRatherThanIt()
    {
        // Arrange
        await using var provider = RegisteredMcpToolSurface.ComposedPublishing(
            PublishedToolCategorySelection.Of([McpToolCategory.Mailbox]),
            requestedCategories: "contacts");

        // Act
        var listing = await RegisteredMcpToolSurface.ListedToolsAsync(provider);

        // Assert
        Assert.Empty(listing.Tools);
    }

    /// <summary>Asking for an excluded category is not a way to call one of its tools either.</summary>
    [Fact]
    public async Task CallTool_AClientNamingACategoryTheDeploymentExcluded_IsAnsweredAsAnUnknownTool()
    {
        // Arrange
        await using var provider = RegisteredMcpToolSurface.ComposedPublishing(
            PublishedToolCategorySelection.Of([McpToolCategory.Mailbox]),
            requestedCategories: "contacts");

        // Act
        var refusal = await Assert.ThrowsAsync<McpProtocolException>(() =>
            RegisteredMcpToolSurface.CalledAsync(provider, GetContactTool.ToolName));

        // Assert
        Assert.Equal($"Unknown tool: '{GetContactTool.ToolName}'", refusal.Message);
    }

    /// <summary>A header nothing here can act on leaves the deployment's own selection standing, rather than narrowing the endpoint to silence.</summary>
    [Fact]
    public async Task ListTools_AClientNamingNothingThisSurfacePublishes_IsServedWhatTheDeploymentPublishes()
    {
        // Arrange
        await using var provider = RegisteredMcpToolSurface.ComposedPublishing(
            PublishedToolCategorySelection.Of([McpToolCategory.Mailbox]),
            requestedCategories: "rules, folders");

        // Act
        var listing = await RegisteredMcpToolSurface.ListedToolsAsync(provider);

        // Assert
        Assert.Equal(
            [
                GetEmailContentTool.ToolName,
                ListAccountsTool.ToolName,
                ListEmailsTool.ToolName,
                SearchEmailsTool.ToolName,
            ],
            listing.Tools.Select(static tool => tool.Name).Order(StringComparer.Ordinal));
    }
}
