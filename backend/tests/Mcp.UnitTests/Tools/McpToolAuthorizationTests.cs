// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Observability;
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
    /// <summary>A name no tool answers to, which is what a caller sends; never the placeholder such a name is measured under.</summary>
    private const string UndeclaredToolName = "delete_everything";

    /// <summary>What the shared helper admits every test caller as, which is what a refusal names in a log.</summary>
    private const string CallerIdentity = "test-caller";

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
            ListingOf([UndeclaredToolName, SearchEmailsTool.ToolName]));

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

    /// <summary>A tool registered without an entry declaring what reaching it requires is refused rather than served ungoverned.</summary>
    [Fact]
    public async Task RefuseUnauthorizedToolAsync_ACallNamingNoPublishedTool_IsRefusedWithoutReachingTheTool()
    {
        // Arrange
        var authorization = AccessAuthorizations.ForCallerGranted(
            [.. MailFathomPermission.PublishedFor(ProtectedSurface.Mail)]);
        var reached = false;

        // Act
        var refusal = await Assert.ThrowsAsync<McpProtocolException>(() => CalledAsync(
            authorization,
            UndeclaredToolName,
            ServedResult,
            onReached: () => reached = true));

        // Assert
        Assert.Equal($"Unknown tool: '{UndeclaredToolName}'", refusal.Message);
        Assert.Equal(McpErrorCode.InvalidParams, refusal.ErrorCode);
        Assert.False(reached);
    }

    /// <summary>The caller is told nothing, so the deployment's own record is the only place this boundary is visible.</summary>
    [Fact]
    public async Task RefuseUnauthorizedToolAsync_ACallTheGrantDoesNotPermit_IsRecordedNamingTheToolAndThePermission()
    {
        // Arrange
        var refusals = Substitute.For<IAuthorizationRefusalTelemetry>();

        // Act
        await Assert.ThrowsAsync<McpProtocolException>(() => CalledAsync(
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead),
            AskMailTool.ToolName,
            ServedResult,
            refusals: refusals));

        // Assert
        refusals.Received(1).RecordRefusal(
            ProtectedSurface.Mail,
            AskMailTool.ToolName,
            MailFathomPermission.MailAsk,
            CallerIdentity);
    }

    /// <summary>A name a caller invented must not become a dimension, or a client looping over misspellings mints one apiece.</summary>
    [Fact]
    public async Task RefuseUnauthorizedToolAsync_ACallNamingNoPublishedTool_IsRecordedUnderThePlaceholderAndNoPermission()
    {
        // Arrange
        var refusals = Substitute.For<IAuthorizationRefusalTelemetry>();

        // Act
        await Assert.ThrowsAsync<McpProtocolException>(() => CalledAsync(
            AccessAuthorizations.ForCallerGranted([.. MailFathomPermission.PublishedFor(ProtectedSurface.Mail)]),
            UndeclaredToolName,
            ServedResult,
            refusals: refusals));

        // Assert
        refusals.Received(1).RecordRefusal(
            ProtectedSurface.Mail,
            PublishedTools.UnpublishedToolName,
            Arg.Is<MailFathomPermission>(static permission => !permission.IsSpecified),
            CallerIdentity);
    }

    /// <summary>The use case is the authority, so a refusal it raises behind a permitted tool is recorded here too.</summary>
    [Fact]
    public async Task RefuseUnauthorizedToolAsync_AUseCaseRefusingBehindAPermittedTool_IsRecorded()
    {
        // Arrange
        var refusals = Substitute.For<IAuthorizationRefusalTelemetry>();
        var authorization = AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead);

        // Act
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() => CalledAsync(
            authorization,
            SearchEmailsTool.ToolName,
            ServedResult,
            onReached: () => authorization.RequirePermission(MailFathomPermission.MailAsk),
            refusals: refusals));

        // Assert
        refusals.Received(1).RecordRefusal(
            ProtectedSurface.Mail,
            SearchEmailsTool.ToolName,
            MailFathomPermission.MailAsk,
            CallerIdentity);
    }

    /// <summary>A call the grant permits is the ordinary path, and the ordinary path costs nothing new.</summary>
    [Fact]
    public async Task RefuseUnauthorizedToolAsync_ACallTheGrantPermits_RecordsNoRefusal()
    {
        // Arrange
        var refusals = Substitute.For<IAuthorizationRefusalTelemetry>();

        // Act
        await CalledAsync(
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead),
            SearchEmailsTool.ToolName,
            ServedResult,
            refusals: refusals);

        // Assert
        refusals.DidNotReceiveWithAnyArgs().RecordRefusal(default, default!, default, default);
    }

    /// <summary>
    /// A tool withheld from a listing is not a refusal. Nothing was refused, every narrowed caller would report one on
    /// every listing, and the omission has no operation to partition by — so the record worth alerting on would sit
    /// under the steady state.
    /// </summary>
    [Fact]
    public async Task WithoutUnauthorizedToolsAsync_AWithheldTool_RecordsNoRefusal()
    {
        // Arrange
        var refusals = Substitute.For<IAuthorizationRefusalTelemetry>();

        // Act
        await FilteredListingAsync(
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead),
            ListingOf(EveryPublishedToolName),
            refusals);

        // Assert
        refusals.DidNotReceiveWithAnyArgs().RecordRefusal(default, default!, default, default);
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
        ListToolsResult listing,
        IAuthorizationRefusalTelemetry? refusals = null)
    {
        await using var provider = ProviderOver(authorization, refusals);

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
        Action? onReached = null,
        IAuthorizationRefusalTelemetry? refusals = null)
    {
        await using var provider = ProviderOver(authorization, refusals);

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

    private static ServiceProvider ProviderOver(
        AccessAuthorization authorization,
        IAuthorizationRefusalTelemetry? refusals)
    {
        var services = new ServiceCollection();
        services.AddSingleton(authorization);
        services.AddSingleton(refusals ?? Substitute.For<IAuthorizationRefusalTelemetry>());

        return services.BuildServiceProvider();
    }

    private static ListToolsResult ListingOf(IEnumerable<string> toolNames) => new()
    {
        Tools = [.. toolNames.Select(static name => new Tool { Name = name })],
    };
}
