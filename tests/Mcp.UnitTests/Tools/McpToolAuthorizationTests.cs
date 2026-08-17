// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Mcp.Tools;
using MailFathom.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NSubstitute;
using Xunit;

namespace MailFathom.Mcp.UnitTests.Tools;

/// <summary>Covers which tools a caller is offered and what a call it may not make is answered with.</summary>
/// <remarks>
/// The decision is made per request from the grant the caller was admitted under, so what a test varies is the
/// principal and nothing about the composition: the same surface answers two callers differently within one process.
/// </remarks>
public sealed class McpToolAuthorizationTests
{
    private const string UnpublishedToolName = "delete_everything";

    [Fact]
    public async Task WithoutUnauthorizedToolsAsync_ACallerGrantedTheWholeSurface_IsOfferedEveryTool()
    {
        // Arrange
        var authorization = AccessAuthorizations.ForCallerGranted(
            MailFathomPermission.MailRead,
            MailFathomPermission.MailAsk);

        // Act
        var listing = await FilteredListingAsync(authorization, ListingOf(EveryPublishedToolName));

        // Assert
        Assert.Equal(EveryPublishedToolName, listing.Tools.Select(static tool => tool.Name));
    }

    /// <summary>Withholding the answering grant is what decides that mail content does not reach a chat provider for this caller.</summary>
    [Fact]
    public async Task WithoutUnauthorizedToolsAsync_ACallerGrantedOnlyMailRead_IsNotOfferedTheAnsweringTool()
    {
        // Arrange
        var authorization = AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead);

        // Act
        var listing = await FilteredListingAsync(authorization, ListingOf(EveryPublishedToolName));

        // Assert
        Assert.Equal(
            [
                ListAccountsTool.ToolName,
                ListEmailsTool.ToolName,
                GetEmailContentTool.ToolName,
                SearchEmailsTool.ToolName,
            ],
            listing.Tools.Select(static tool => tool.Name));
    }

    /// <summary>Neither permission implies the other, so the answering grant alone offers the answering tool alone.</summary>
    [Fact]
    public async Task WithoutUnauthorizedToolsAsync_ACallerGrantedOnlyMailAsk_IsOfferedTheAnsweringToolAlone()
    {
        // Arrange
        var authorization = AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailAsk);

        // Act
        var listing = await FilteredListingAsync(authorization, ListingOf(EveryPublishedToolName));

        // Assert
        Assert.Equal([AskMailTool.ToolName], listing.Tools.Select(static tool => tool.Name));
    }

    /// <summary>An entry whose grant was emptied retires a credential without deleting it, so the surface it reaches is empty.</summary>
    [Fact]
    public async Task WithoutUnauthorizedToolsAsync_ACallerGrantedNothing_IsOfferedNoTool()
    {
        // Arrange
        var authorization = AccessAuthorizations.ForCallerGranted();

        // Act
        var listing = await FilteredListingAsync(authorization, ListingOf(EveryPublishedToolName));

        // Assert
        Assert.Empty(listing.Tools);
    }

    /// <summary>Nothing declared what reaching a tool this surface does not publish would require, so nobody may.</summary>
    [Fact]
    public async Task WithoutUnauthorizedToolsAsync_ADescriptorNoToolDeclaredAPermissionFor_IsWithheld()
    {
        // Arrange
        var authorization = AccessAuthorizations.ForCallerGranted(
            MailFathomPermission.MailRead,
            MailFathomPermission.MailAsk);

        // Act
        var listing = await FilteredListingAsync(
            authorization,
            ListingOf([UnpublishedToolName, SearchEmailsTool.ToolName]));

        // Assert
        Assert.Equal([SearchEmailsTool.ToolName], listing.Tools.Select(static tool => tool.Name));
    }

    /// <summary>Withholding a descriptor must not disturb the rest of what a listing carries.</summary>
    [Fact]
    public async Task WithoutUnauthorizedToolsAsync_AWithheldTool_LeavesTheContinuationCursorInPlace()
    {
        // Arrange
        var authorization = AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead);
        var listing = ListingOf(EveryPublishedToolName);
        listing.NextCursor = "a-cursor";

        // Act
        var filtered = await FilteredListingAsync(authorization, listing);

        // Assert
        Assert.Equal("a-cursor", filtered.NextCursor);
    }

    /// <summary>A listing the grant reaches in full is the SDK's own result, cursor and metadata included.</summary>
    [Fact]
    public async Task WithoutUnauthorizedToolsAsync_AListingTheGrantReachesInFull_IsReturnedUnchanged()
    {
        // Arrange
        var authorization = AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead);
        var listing = ListingOf([SearchEmailsTool.ToolName]);

        // Act
        var filtered = await FilteredListingAsync(authorization, listing);

        // Assert
        Assert.Same(listing, filtered);
    }

    /// <summary>Serving a listing nobody could apply a grant to would publish tools this caller was never granted.</summary>
    [Fact]
    public async Task WithoutUnauthorizedToolsAsync_AListingWithNoServiceProvider_IsRefused()
    {
        // Arrange
        var request = new RequestContext<ListToolsRequestParams>(
            Substitute.For<McpServer>(),
            new JsonRpcRequest { Method = "tools/list" },
            new ListToolsRequestParams());
        var listing = ListingOf(EveryPublishedToolName);

        // Act, Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            McpToolAuthorization.WithoutUnauthorizedToolsAsync(
                (_, _) => new ValueTask<ListToolsResult>(listing),
                request,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RefuseUnauthorizedToolAsync_ACallTheGrantPermits_ReachesTheTool()
    {
        // Arrange
        var authorization = AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead);
        var served = new CallToolResult { Content = [new TextContentBlock { Text = "served" }] };

        // Act
        var result = await CalledAsync(authorization, SearchEmailsTool.ToolName, served);

        // Assert
        Assert.Same(served, result);
    }

    /// <summary>The refusal is the answer a call naming a tool that does not exist already receives, down to its wording.</summary>
    [Theory]
    [InlineData(AskMailTool.ToolName)]
    [InlineData(SearchEmailsTool.ToolName)]
    public async Task RefuseUnauthorizedToolAsync_ACallTheGrantDoesNotPermit_IsAnsweredAsAnUnknownTool(string toolName)
    {
        // Arrange
        var authorization = AccessAuthorizations.ForCallerGranted();

        // Act
        var refusal = await Assert.ThrowsAsync<McpProtocolException>(() =>
            CalledAsync(authorization, toolName, ServedResult));

        // Assert
        Assert.Equal($"Unknown tool: '{toolName}'", refusal.Message);
        Assert.Equal(McpErrorCode.InvalidParams, refusal.ErrorCode);
    }

    /// <summary>Reaching the tool at all would let a caller measure what it was refused, whatever the answer said.</summary>
    [Fact]
    public async Task RefuseUnauthorizedToolAsync_ACallTheGrantDoesNotPermit_NeverReachesTheTool()
    {
        // Arrange
        var authorization = AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead);
        var reached = false;

        // Act
        await Assert.ThrowsAsync<McpProtocolException>(() => CalledAsync(
            authorization,
            AskMailTool.ToolName,
            served: null,
            onReached: () => reached = true));

        // Assert
        Assert.False(reached);
    }

    /// <summary>One unknown-tool answer is written in one place, which is the server's own.</summary>
    [Fact]
    public async Task RefuseUnauthorizedToolAsync_ACallNamingNoPublishedTool_IsLeftToTheServer()
    {
        // Arrange
        var authorization = AccessAuthorizations.ForCallerGranted();
        var reached = false;

        // Act
        await CalledAsync(
            authorization,
            UnpublishedToolName,
            ServedResult,
            onReached: () => reached = true);

        // Assert
        Assert.True(reached);
    }

    /// <summary>Serving a call nobody could apply a grant to would reach a tool this caller was never granted.</summary>
    [Fact]
    public async Task RefuseUnauthorizedToolAsync_ACallWithNoServiceProvider_IsRefused()
    {
        // Arrange
        var request = new RequestContext<CallToolRequestParams>(
            Substitute.For<McpServer>(),
            new JsonRpcRequest { Method = "tools/call" },
            new CallToolRequestParams { Name = AskMailTool.ToolName });

        // Act, Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            McpToolAuthorization.RefuseUnauthorizedToolAsync(
                (_, _) => new ValueTask<CallToolResult>(ServedResult),
                request,
                TestContext.Current.CancellationToken));
    }

    private static string[] EveryPublishedToolName =>
    [
        ListAccountsTool.ToolName,
        ListEmailsTool.ToolName,
        GetEmailContentTool.ToolName,
        SearchEmailsTool.ToolName,
        AskMailTool.ToolName,
    ];

    private static CallToolResult ServedResult => new() { Content = [new TextContentBlock { Text = "served" }] };

    private static async Task<ListToolsResult> FilteredListingAsync(
        AccessAuthorization authorization,
        ListToolsResult listing)
    {
        await using var provider = ProviderOver(authorization);

        var request = new RequestContext<ListToolsRequestParams>(
            Substitute.For<McpServer>(),
            new JsonRpcRequest { Method = "tools/list" },
            new ListToolsRequestParams())
        {
            Services = provider,
        };

        return await McpToolAuthorization.WithoutUnauthorizedToolsAsync(
            (_, _) => new ValueTask<ListToolsResult>(listing),
            request,
            TestContext.Current.CancellationToken);
    }

    private static async Task<CallToolResult> CalledAsync(
        AccessAuthorization authorization,
        string toolName,
        CallToolResult? served,
        Action? onReached = null)
    {
        await using var provider = ProviderOver(authorization);

        var request = new RequestContext<CallToolRequestParams>(
            Substitute.For<McpServer>(),
            new JsonRpcRequest { Method = "tools/call" },
            new CallToolRequestParams { Name = toolName })
        {
            Services = provider,
        };

        return await McpToolAuthorization.RefuseUnauthorizedToolAsync(
            (_, _) =>
            {
                onReached?.Invoke();

                return new ValueTask<CallToolResult>(served ?? ServedResult);
            },
            request,
            TestContext.Current.CancellationToken);
    }

    private static ServiceProvider ProviderOver(AccessAuthorization authorization)
    {
        var services = new ServiceCollection();
        services.AddSingleton(authorization);

        return services.BuildServiceProvider();
    }

    private static ListToolsResult ListingOf(IEnumerable<string> toolNames) => new()
    {
        Tools = [.. toolNames.Select(static name => new Tool { Name = name })],
    };
}
