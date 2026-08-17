// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Mcp.Tools;
using MailFathom.Mcp.UnitTests.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NSubstitute;
using Xunit;

namespace MailFathom.Mcp.UnitTests.Tools;

/// <summary>Covers that the registration composes the grant into the pipelines a request actually passes through.</summary>
/// <remarks>
/// The filters themselves are proved elsewhere; what this asserts is that they are wired at all, and in an order that
/// produces the right answer. A registration that dropped one would leave every other test in this suite green while the
/// surface served a caller tools its grant does not permit, which is the failure worth a test of its own.
/// </remarks>
public sealed class McpToolSurfaceCompositionTests
{
    [Fact]
    public async Task AddMailFathomServer_ACallerGrantedOnlyMailRead_IsListedNoAnsweringTool()
    {
        // Arrange
        await using var provider = RegisteredMcpToolSurface.ComposedForCallerGranted(MailFathomPermission.MailRead);

        // Act
        var listing = await ListedToolsAsync(provider);

        // Assert
        Assert.DoesNotContain(AskMailTool.ToolName, listing.Tools.Select(static tool => tool.Name));
        Assert.Contains(SearchEmailsTool.ToolName, listing.Tools.Select(static tool => tool.Name));
    }

    /// <summary>The deployment this is composed over answers questions, so the whole grant reaches the whole surface.</summary>
    [Fact]
    public async Task AddMailFathomServer_ACallerGrantedTheWholeSurface_IsListedEveryTool()
    {
        // Arrange
        await using var provider = RegisteredMcpToolSurface.ComposedForCallerGranted(
            MailFathomPermission.MailRead,
            MailFathomPermission.MailAsk);

        // Act
        var listing = await ListedToolsAsync(provider);

        // Assert
        Assert.Contains(AskMailTool.ToolName, listing.Tools.Select(static tool => tool.Name));
        Assert.Contains(SearchEmailsTool.ToolName, listing.Tools.Select(static tool => tool.Name));
    }

    [Fact]
    public async Task AddMailFathomServer_ACallerGrantedNothing_IsListedNoToolAtAll()
    {
        // Arrange
        await using var provider = RegisteredMcpToolSurface.ComposedForCallerGranted();

        // Act
        var listing = await ListedToolsAsync(provider);

        // Assert
        Assert.Empty(listing.Tools);
    }

    /// <summary>A call the grant does not permit reaches the same refusal through the registered pipeline as through the filter alone.</summary>
    [Fact]
    public async Task AddMailFathomServer_ACallTheGrantDoesNotPermit_IsAnsweredAsAnUnknownTool()
    {
        // Arrange
        await using var provider = RegisteredMcpToolSurface.ComposedForCallerGranted(MailFathomPermission.MailRead);

        // Act
        var refusal = await Assert.ThrowsAsync<McpProtocolException>(() =>
            CalledAsync(provider, AskMailTool.ToolName));

        // Assert
        Assert.Equal($"Unknown tool: '{AskMailTool.ToolName}'", refusal.Message);
        Assert.Equal(McpErrorCode.InvalidParams, refusal.ErrorCode);
    }

    /// <summary>Composing the two call filters must leave the reporter outermost, so a refusal is recorded exactly as an unknown tool already is.</summary>
    [Fact]
    public async Task AddMailFathomServer_ACallTheGrantPermits_ReachesTheTool()
    {
        // Arrange
        await using var provider = RegisteredMcpToolSurface.ComposedForCallerGranted(MailFathomPermission.MailRead);
        var served = new CallToolResult { Content = [new TextContentBlock { Text = "served" }] };

        // Act
        var result = await CalledAsync(provider, SearchEmailsTool.ToolName, served);

        // Assert
        Assert.Same(served, result);
    }

    /// <summary>Runs the listing through the filters the registration composed, over a handler standing in for the SDK's own.</summary>
    private static Task<ListToolsResult> ListedToolsAsync(IServiceProvider provider)
    {
        var everyDescriptor = new ListToolsResult
        {
            Tools =
            [
                .. RegisteredMcpToolSurface.Tools().Select(static tool => tool.ProtocolTool),
            ],
        };

        var pipeline = RequestFilters(provider).ListToolsFilters
            .Reverse()
            .Aggregate<McpRequestFilter<ListToolsRequestParams, ListToolsResult>, McpRequestHandler<ListToolsRequestParams, ListToolsResult>>(
                (_, _) => new ValueTask<ListToolsResult>(everyDescriptor),
                static (next, filter) => filter(next));

        var request = new RequestContext<ListToolsRequestParams>(
            Substitute.For<McpServer>(),
            new JsonRpcRequest { Method = "tools/list" },
            new ListToolsRequestParams())
        {
            Services = provider,
        };

        return pipeline(request, TestContext.Current.CancellationToken).AsTask();
    }

    /// <summary>Runs the call through the filters the registration composed, over a handler standing in for the tool.</summary>
    private static Task<CallToolResult> CalledAsync(
        IServiceProvider provider,
        string toolName,
        CallToolResult? served = null)
    {
        var result = served ?? new CallToolResult { Content = [new TextContentBlock { Text = "served" }] };

        var pipeline = RequestFilters(provider).CallToolFilters
            .Reverse()
            .Aggregate<McpRequestFilter<CallToolRequestParams, CallToolResult>, McpRequestHandler<CallToolRequestParams, CallToolResult>>(
                (_, _) => new ValueTask<CallToolResult>(result),
                static (next, filter) => filter(next));

        var request = new RequestContext<CallToolRequestParams>(
            Substitute.For<McpServer>(),
            new JsonRpcRequest { Method = "tools/call" },
            new CallToolRequestParams { Name = toolName })
        {
            Services = provider,
        };

        return pipeline(request, TestContext.Current.CancellationToken).AsTask();
    }

    /// <summary>Reads the filters the registration wrote onto the server options, in the order it registered them.</summary>
    private static McpRequestFilters RequestFilters(IServiceProvider provider) =>
        provider.GetRequiredService<IOptions<McpServerOptions>>().Value.Filters.Request;
}
