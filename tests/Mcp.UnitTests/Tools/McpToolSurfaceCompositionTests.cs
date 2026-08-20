// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Mcp.Tools;
using MailFathom.Mcp.Tools.Contacts;
using MailFathom.Mcp.Tools.Drafts;
using MailFathom.Mcp.UnitTests.TestDoubles;
using MailFathom.TestSupport;
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

    /// <summary>The two halves of the contact grant are separate permissions, so a reader's grant must reach the readers and stop there.</summary>
    [Fact]
    public async Task AddMailFathomServer_ACallerGrantedOnlyTheContactReadingGrant_IsListedTheContactReadersAlone()
    {
        // Arrange
        await using var provider = RegisteredMcpToolSurface.ComposedForCallerGranted(
            MailFathomPermission.MailContactsRead);

        // Act
        var listing = await ListedToolsAsync(provider);

        // Assert
        var listed = listing.Tools.Select(static tool => tool.Name).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal([GetContactTool.ToolName, ListContactsTool.ToolName], listed);
    }

    /// <summary>Taking on a collected record is a write to the book, so the writing grant is what is offered it.</summary>
    [Fact]
    public async Task AddMailFathomServer_ACallerGrantedOnlyTheContactWritingGrant_IsListedThePromotionBesideTheOtherWriters()
    {
        // Arrange
        await using var provider = RegisteredMcpToolSurface.ComposedForCallerGranted(
            MailFathomPermission.MailContactsWrite);

        // Act
        var listing = await ListedToolsAsync(provider);

        // Assert
        var listed = listing.Tools.Select(static tool => tool.Name).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(
            [
                CreateContactTool.ToolName,
                DeleteContactTool.ToolName,
                PromoteContactTool.ToolName,
                UpdateContactTool.ToolName,
            ],
            listed);
    }

    /// <summary>Drafting and sending are separate grants, so a caller holding the first sees the three tools that send nothing and no others.</summary>
    /// <remarks>
    /// This is the arrangement the draft tools exist for: an agent that may prepare mail and may not send any. What
    /// proves it is the listing rather than a refusal, because a capability withheld from a caller is one it is never
    /// told about — and <c>send_draft</c> being absent here is the whole of what makes the drafting grant the safe
    /// half.
    /// </remarks>
    [Fact]
    public async Task AddMailFathomServer_ACallerGrantedOnlyTheDraftingGrant_IsListedTheDraftToolsThatSendNothing()
    {
        // Arrange
        await using var provider = RegisteredMcpToolSurface.ComposedForCallerGranted(
            MailFathomPermission.MailDraftsWrite);

        // Act
        var listing = await ListedToolsAsync(provider);

        // Assert
        var listed = listing.Tools.Select(static tool => tool.Name).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(
            [
                DeleteDraftTool.ToolName,
                SaveDraftTool.ToolName,
                UpdateDraftTool.ToolName,
            ],
            listed);
    }

    /// <summary>Sending a draft is behind the sending grant, so a caller that may only draft cannot reach it by naming it.</summary>
    [Fact]
    public async Task AddMailFathomServer_TheDraftSendingToolCalledWithTheDraftingGrant_IsAnsweredAsAnUnknownTool()
    {
        // Arrange
        await using var provider = RegisteredMcpToolSurface.ComposedForCallerGranted(
            MailFathomPermission.MailDraftsWrite);

        // Act
        var refusal = await Assert.ThrowsAsync<McpProtocolException>(() =>
            CalledAsync(provider, SendDraftTool.ToolName));

        // Assert
        Assert.Equal($"Unknown tool: '{SendDraftTool.ToolName}'", refusal.Message);
        Assert.Equal(McpErrorCode.InvalidParams, refusal.ErrorCode);
    }

    /// <summary>The sending grant reaches the promotion and none of the three tools that write a draft.</summary>
    [Fact]
    public async Task AddMailFathomServer_ACallerGrantedOnlyTheSendingGrant_IsListedNoToolThatWritesADraft()
    {
        // Arrange
        await using var provider = RegisteredMcpToolSurface.ComposedForCallerGranted(MailFathomPermission.MailSend);

        // Act
        var listing = await ListedToolsAsync(provider);

        // Assert
        var listed = listing.Tools.Select(static tool => tool.Name).ToArray();

        Assert.Contains(SendDraftTool.ToolName, listed);
        Assert.DoesNotContain(SaveDraftTool.ToolName, listed);
        Assert.DoesNotContain(UpdateDraftTool.ToolName, listed);
        Assert.DoesNotContain(DeleteDraftTool.ToolName, listed);
    }

    /// <summary>Erasing a person is behind the writing grant, so a reader asking for it is answered as it is about any tool it was not offered.</summary>
    [Fact]
    public async Task AddMailFathomServer_TheErasingToolCalledWithTheContactReadingGrant_IsAnsweredAsAnUnknownTool()
    {
        // Arrange
        await using var provider = RegisteredMcpToolSurface.ComposedForCallerGranted(
            MailFathomPermission.MailContactsRead);

        // Act
        var refusal = await Assert.ThrowsAsync<McpProtocolException>(() =>
            CalledAsync(provider, DeleteContactTool.ToolName));

        // Assert
        Assert.Equal($"Unknown tool: '{DeleteContactTool.ToolName}'", refusal.Message);
        Assert.Equal(McpErrorCode.InvalidParams, refusal.ErrorCode);
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
    /// <remarks>
    /// Swapping the two registrations, or an SDK release that composed them the other way, would let the refusal leave
    /// the pipeline without passing the reporter — no metric, no log line, and an operator sent to diagnose a client
    /// from a record that never mentioned the call. Nothing else in this suite would notice, because both filters would
    /// still produce the right answer to the caller.
    /// </remarks>
    [Fact]
    public async Task AddMailFathomServer_ACallTheGrantDoesNotPermit_IsRecordedByTheReporter()
    {
        // Arrange
        await using var provider = RegisteredMcpToolSurface.ComposedForCallerGranted(MailFathomPermission.MailRead);

        // Act
        await Assert.ThrowsAsync<McpProtocolException>(() => CalledAsync(provider, AskMailTool.ToolName));

        // Assert
        var recorded = provider.GetRequiredService<RecordingLoggerProvider>().Records;

        Assert.Contains(
            recorded,
            record => record.Properties.TryGetValue("ToolName", out var toolName)
                && Equals(toolName, AskMailTool.ToolName)
                && record.Properties.TryGetValue("JsonRpcErrorCode", out var errorCode)
                && Equals(errorCode, (int)McpErrorCode.InvalidParams));
    }

    /// <summary>A call the grant permits passes both registered filters untouched and is served whatever lies behind them.</summary>
    /// <remarks>
    /// The pipeline here ends at a stand-in handler rather than at SDK dispatch, so what this establishes is that
    /// neither filter refused the call — not that a tool ran. Named under <c>list_accounts</c> because a call that
    /// completes is recorded on the process-wide meter, and the telemetry tests are a separate collection running
    /// alongside this one: a name whose measurements one of them counts exactly would fail there on a change made here.
    /// <c>search_emails</c> and <c>ask_mail</c> are both counted; this one is not.
    /// </remarks>
    [Fact]
    public async Task AddMailFathomServer_ACallTheGrantPermits_IsNotRefusedByTheFilters()
    {
        // Arrange
        await using var provider = RegisteredMcpToolSurface.ComposedForCallerGranted(MailFathomPermission.MailRead);
        var served = new CallToolResult { Content = [new TextContentBlock { Text = "served" }] };

        // Act
        var result = await CalledAsync(provider, ListAccountsTool.ToolName, served);

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
