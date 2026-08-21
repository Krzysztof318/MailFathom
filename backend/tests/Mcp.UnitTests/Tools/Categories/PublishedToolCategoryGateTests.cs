// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Mcp.Tools;
using MailFathom.Mcp.Tools.Categories;
using MailFathom.Mcp.UnitTests.TestDoubles;
using ModelContextProtocol.Server;
using Xunit;

namespace MailFathom.Mcp.UnitTests.Tools.Categories;

/// <summary>Covers the refusal that keeps a tool nobody categorized from reaching a deployment.</summary>
/// <remarks>
/// A tool without a category has no answer to the question a selection asks, so it would be published by every
/// deployment or by none depending on which default somebody chose. Refusing to start is what puts that decision in
/// front of the person adding the tool rather than in front of an operator reading a listing.
/// </remarks>
public sealed class PublishedToolCategoryGateTests
{
    /// <summary>The set the gate judges is the one a host serves, so this is the claim that the surface as registered can start at all.</summary>
    [Fact]
    public async Task StartAsync_TheToolsTheRegistrationComposes_AreEveryOneCategorized()
    {
        // Arrange
        var gate = new PublishedToolCategoryGate(RegisteredMcpToolSurface.Tools());

        // Act, Assert
        await gate.StartAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StartAsync_ARegisteredToolThatDeclaresNoCategory_RefusesToStartAndNamesIt()
    {
        // Arrange
        var uncategorized = McpServerTool.Create(
            () => "answered",
            new McpServerToolCreateOptions { Name = "summarize_thread" });

        var gate = new PublishedToolCategoryGate([.. RegisteredMcpToolSurface.Tools(), uncategorized]);

        // Act
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gate.StartAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("summarize_thread", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>Nothing about a category is decided while the host stops, so stopping is not a second place the check could disagree with itself.</summary>
    [Fact]
    public async Task StopAsync_LeavesTheSurfaceAlone()
    {
        // Arrange
        var gate = new PublishedToolCategoryGate([]);

        // Act, Assert
        await gate.StopAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>The gate reads the same declaration a listing does, so a name it accepts is one the filters can decide about.</summary>
    [Fact]
    public void TryGetCategory_ANameNoToolAnswersTo_DeclaresNoCategory()
    {
        // Assert
        Assert.False(PublishedTools.TryGetCategory("summarize_thread", out var category));
        Assert.False(category.IsSpecified);
    }
}
