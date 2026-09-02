// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.AiProviders;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Mcp.Tools;
using MailFathom.Mcp.UnitTests.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NSubstitute;
using Xunit;

namespace MailFathom.Mcp.UnitTests.Tools;

/// <summary>Covers which listings carry the <c>ask_mail</c> descriptor and which withhold it.</summary>
/// <remarks>
/// The decision is made per listing rather than at registration, so what a test varies is the deployment's state and not
/// its composition. That is what makes the transition observable without a restart: the same server answers two
/// listings differently once a provider stops or starts answering.
/// </remarks>
public sealed class AskMailAdvertisementTests
{
    [Fact]
    public async Task WithoutUnavailableAnsweringAsync_ADeploymentThatCanAnswer_AdvertisesTheTool()
    {
        // Arrange
        var capability = AnsweringDeployment.Capability(new StubMailQuestionAnswerer());

        // Act
        var listing = await FilteredListingAsync(capability, ListingOf(AskMailTool.ToolName, "search_emails"));

        // Assert
        Assert.Equal(
            [AskMailTool.ToolName, "search_emails"],
            listing.Tools.Select(static tool => tool.Name));
    }

    [Fact]
    public async Task WithoutUnavailableAnsweringAsync_ADeploymentThatAnswersNoQuestions_WithholdsTheTool()
    {
        // Arrange
        var capability = AnsweringDeployment.Capability(answerer: null);

        // Act
        var listing = await FilteredListingAsync(capability, ListingOf(AskMailTool.ToolName, "search_emails"));

        // Assert
        Assert.Equal(["search_emails"], listing.Tools.Select(static tool => tool.Name));
    }

    /// <summary>A tool a client can see is a tool it will call, so one that could only fail is not offered.</summary>
    [Theory]
    [InlineData(AiProviderHealthState.Unavailable)]
    [InlineData(AiProviderHealthState.Misconfigured)]
    public async Task WithoutUnavailableAnsweringAsync_ADeploymentWhoseProviderIsRefusing_WithholdsTheTool(
        AiProviderHealthState chatState)
    {
        // Arrange
        var capability = AnsweringDeployment.Capability(new StubMailQuestionAnswerer(), chatState);

        // Act
        var listing = await FilteredListingAsync(capability, ListingOf(AskMailTool.ToolName, "search_emails"));

        // Assert
        Assert.Equal(["search_emails"], listing.Tools.Select(static tool => tool.Name));
    }

    /// <summary>Withholding one descriptor must not disturb the rest of what a listing carries.</summary>
    [Fact]
    public async Task WithoutUnavailableAnsweringAsync_AWithheldTool_LeavesTheContinuationCursorInPlace()
    {
        // Arrange
        var capability = AnsweringDeployment.Capability(answerer: null);
        var listing = ListingOf(AskMailTool.ToolName, "search_emails");
        listing.NextCursor = "a-cursor";

        // Act
        var filtered = await FilteredListingAsync(capability, listing);

        // Assert
        Assert.Equal("a-cursor", filtered.NextCursor);
    }

    /// <summary>The capability is read only where the descriptor is present, so a paged listing without it costs nothing.</summary>
    [Fact]
    public async Task WithoutUnavailableAnsweringAsync_AListingWithoutTheTool_IsReturnedUnchanged()
    {
        // Arrange
        var listing = ListingOf("search_emails");

        // Act
        var filtered = await FilteredListingAsync(capability: null, listing);

        // Assert
        Assert.Same(listing, filtered);
    }

    /// <summary>Advertising a capability nobody established would offer a tool this deployment may be unable to serve.</summary>
    [Fact]
    public async Task WithoutUnavailableAnsweringAsync_AListingWithNoServiceProvider_IsRefused()
    {
        // Arrange
        var request = new RequestContext<ListToolsRequestParams>(
            Substitute.For<McpServer>(),
            new JsonRpcRequest { Method = "tools/list" },
            new ListToolsRequestParams());
        var listing = ListingOf(AskMailTool.ToolName);

        // Act, Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AskMailAdvertisement.WithoutUnavailableAnsweringAsync(
                (_, _) => new ValueTask<ListToolsResult>(listing),
                request,
                TestContext.Current.CancellationToken));
    }

    private static async Task<ListToolsResult> FilteredListingAsync(
        MailAnsweringCapability? capability,
        ListToolsResult listing)
    {
        var services = new ServiceCollection();
        if (capability is not null)
        {
            services.AddSingleton(capability);
        }

        await using var provider = services.BuildServiceProvider();

        var request = new RequestContext<ListToolsRequestParams>(
            Substitute.For<McpServer>(),
            new JsonRpcRequest { Method = "tools/list" },
            new ListToolsRequestParams())
        {
            Services = provider,
        };

        return await AskMailAdvertisement.WithoutUnavailableAnsweringAsync(
            (_, _) => new ValueTask<ListToolsResult>(listing),
            request,
            TestContext.Current.CancellationToken);
    }

    private static ListToolsResult ListingOf(params string[] toolNames) => new()
    {
        Tools = [.. toolNames.Select(static name => new Tool { Name = name })],
    };
}
