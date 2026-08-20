// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Mcp.Tools.Categories;
using Xunit;

namespace MailFathom.Mcp.UnitTests.Tools.Categories;

/// <summary>Covers what a deployment publishes and what a client is allowed to do to it.</summary>
/// <remarks>
/// The claim worth a test of its own is that narrowing only ever narrows. Everything else here — the unset selection,
/// the refusal of a value naming nothing — exists so that an endpoint nobody configured behaves as it did before the
/// setting existed.
/// </remarks>
public sealed class PublishedToolCategorySelectionTests
{
    [Fact]
    public void Everything_PublishesEveryCategoryTheSurfaceDeclares()
    {
        // Assert
        Assert.Equal(McpToolCategory.All, PublishedToolCategorySelection.Everything.Categories);
        Assert.All(McpToolCategory.All, category =>
            Assert.True(PublishedToolCategorySelection.Everything.Publishes(category)));
    }

    /// <summary>The absence of the setting is what a deployment has today, so naming nothing has to mean everything rather than nothing.</summary>
    [Fact]
    public void Of_NoCategoryAtAll_PublishesEverything()
    {
        // Act
        var selection = PublishedToolCategorySelection.Of([]);

        // Assert
        Assert.Same(PublishedToolCategorySelection.Everything, selection);
    }

    [Fact]
    public void Of_TheCategoriesNamed_PublishesThoseAndNoOther()
    {
        // Act
        var selection = PublishedToolCategorySelection.Of([McpToolCategory.Mailbox, McpToolCategory.Contacts]);

        // Assert
        Assert.True(selection.Publishes(McpToolCategory.Mailbox));
        Assert.True(selection.Publishes(McpToolCategory.Contacts));
        Assert.False(selection.Publishes(McpToolCategory.Sending));
    }

    /// <summary>The order is the declared one rather than the written one, so what a deployment reports about itself does not depend on how an operator typed it.</summary>
    [Fact]
    public void Categories_AreReportedInDeclarationOrderWhateverOrderTheyWereNamedIn()
    {
        // Act
        var selection = PublishedToolCategorySelection.Of(
            [McpToolCategory.Contacts, McpToolCategory.Mailbox, McpToolCategory.Contacts]);

        // Assert
        Assert.Equal([McpToolCategory.Mailbox, McpToolCategory.Contacts], selection.Categories);
    }

    /// <summary>The unspecified default names nothing to publish, so composing a selection from one is a defect rather than a narrower endpoint.</summary>
    [Fact]
    public void Of_TheUnspecifiedDefault_IsRefused()
    {
        // Assert
        Assert.Throws<ArgumentException>(() => PublishedToolCategorySelection.Of([McpToolCategory.Mailbox, default]));
    }

    /// <summary>A client asking for part of what the deployment publishes is served that part.</summary>
    [Fact]
    public void NarrowedBy_ACategoryTheDeploymentPublishes_IsServedThatOneAlone()
    {
        // Arrange
        var selection = PublishedToolCategorySelection.Of([McpToolCategory.Mailbox, McpToolCategory.Contacts]);

        // Act
        var narrowed = selection.NarrowedBy(new HashSet<McpToolCategory> { McpToolCategory.Mailbox });

        // Assert
        Assert.Equal([McpToolCategory.Mailbox], narrowed.Categories);
    }

    /// <summary>The claim the whole design rests on: a request cannot reach a category the deployment excluded.</summary>
    [Fact]
    public void NarrowedBy_ACategoryTheDeploymentExcluded_PublishesNothingRatherThanWidening()
    {
        // Arrange
        var selection = PublishedToolCategorySelection.Of([McpToolCategory.Mailbox]);

        // Act
        var narrowed = selection.NarrowedBy(new HashSet<McpToolCategory> { McpToolCategory.Sending });

        // Assert
        Assert.Empty(narrowed.Categories);
        Assert.False(narrowed.Publishes(McpToolCategory.Sending));
        Assert.False(narrowed.Publishes(McpToolCategory.Mailbox));
    }

    /// <summary>An excluded category asked for beside a published one takes nothing with it.</summary>
    [Fact]
    public void NarrowedBy_APublishedCategoryBesideAnExcludedOne_IsServedThePublishedOne()
    {
        // Arrange
        var selection = PublishedToolCategorySelection.Of([McpToolCategory.Mailbox]);

        // Act
        var narrowed = selection.NarrowedBy(
            new HashSet<McpToolCategory> { McpToolCategory.Mailbox, McpToolCategory.Sending });

        // Assert
        Assert.Equal([McpToolCategory.Mailbox], narrowed.Categories);
    }

    /// <summary>A request that named nothing this surface publishes leaves the deployment's own selection standing, rather than narrowing it to silence.</summary>
    [Fact]
    public void NarrowedBy_NoCategoryAtAll_LeavesTheDeploymentSelectionInForce()
    {
        // Arrange
        var selection = PublishedToolCategorySelection.Of([McpToolCategory.Mailbox]);

        // Act
        var narrowed = selection.NarrowedBy(new HashSet<McpToolCategory>());

        // Assert
        Assert.Same(selection, narrowed);
    }
}
