// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Chat;
using MailFathom.Host.UnitTests.TestDoubles;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Chat;

/// <summary>
/// Covers the step between a bound body-cleanup declaration and the plan the pass runs on. What is particular to this
/// mapper is which reference it reads: it is the first block under <c>Chat</c> that may name a model of its own, and an
/// unwritten one has to resolve to the deployment's main model rather than to nothing.
/// </summary>
public sealed class MailBodyCleanupPlanMapperTests
{
    [Fact]
    public void Map_AnEnabledPassNamingNoModel_RoutesToTheModelEverythingElseRunsOn()
    {
        // Arrange
        var settings = Declared();

        // Act
        var plan = MailBodyCleanupPlanMapper.Map(settings);

        // Assert
        Assert.NotNull(plan);
        Assert.Equal("a-chat-model", plan.Plan.Endpoint.RoutedModelName);
    }

    /// <summary>
    /// The point of the block: a reader is waiting, so an operator may put this pass on a cheaper model without moving
    /// the one that answers questions. The cheap model is a declaration of its own, so everything about how it is
    /// reached comes from its own block rather than from the model beside it.
    /// </summary>
    [Fact]
    public void Map_AnEnabledPassNamingItsOwnModel_RoutesToThatDeclarationRatherThanTheMainOne()
    {
        // Arrange
        var settings = Declared();
        settings.BodyCleanup.Model.Alias = "cheap";

        // Act
        var plan = MailBodyCleanupPlanMapper.Map(settings);
        var shared = ChatGenerationPlanMapper.Map(settings);

        // Assert
        Assert.NotNull(plan);
        Assert.NotNull(shared);
        Assert.Equal("cheap", plan.Plan.Endpoint.Alias);
        Assert.Equal("a-small-fast-model", plan.Plan.Endpoint.RoutedModelName);
        Assert.NotEqual(shared.Endpoint.Alias, plan.Plan.Endpoint.Alias);
    }

    /// <summary>An alias a deployment declared a fallback behind carries it here too, because a pass a reader is waiting in front of is the one worth answering from a second model.</summary>
    [Fact]
    public void Map_AnEnabledPassNamingAFallback_CarriesItBehindTheModel()
    {
        // Arrange
        var settings = Declared();
        settings.BodyCleanup.Model.Alias = "cheap";
        settings.BodyCleanup.Model.Fallback = "answering";

        // Act
        var plan = MailBodyCleanupPlanMapper.Map(settings);

        // Assert
        Assert.NotNull(plan);
        Assert.Equal("answering", plan.Plan.Fallback?.Endpoint.Alias);
    }

    /// <summary>An alias written with whitespace around it is the same alias, because an operator's configuration file is read rather than parsed twice.</summary>
    [Fact]
    public void Map_AnEnabledPassNamingAModelWithSurroundingSpace_ReachesTheTrimmedAlias()
    {
        // Arrange
        var settings = Declared();
        settings.BodyCleanup.Model.Alias = "  cheap  ";

        // Act
        var plan = MailBodyCleanupPlanMapper.Map(settings);

        // Assert
        Assert.NotNull(plan);
        Assert.Equal("a-small-fast-model", plan.Plan.Endpoint.RoutedModelName);
    }

    /// <summary>Whitespace alone is a key somebody left empty, so it routes to the main model rather than to nothing.</summary>
    [Fact]
    public void Map_AnEnabledPassNamingOnlyWhitespace_RoutesToTheModelEverythingElseRunsOn()
    {
        // Arrange
        var settings = Declared();
        settings.BodyCleanup.Model.Alias = "   ";

        // Act
        var plan = MailBodyCleanupPlanMapper.Map(settings);

        // Assert
        Assert.NotNull(plan);
        Assert.Equal("a-chat-model", plan.Plan.Endpoint.RoutedModelName);
    }

    /// <summary>A model named beside no declaration is refused by validation, and mapping it would build a plan with nowhere to send an outline.</summary>
    [Fact]
    public void Map_AModelWithoutAChatEndpoint_MapsNothing()
    {
        // Arrange
        var settings = new ChatModelOptions();
        settings.BodyCleanup.Model.Alias = "cheap";

        // Act
        var plan = MailBodyCleanupPlanMapper.Map(settings);

        // Assert
        Assert.Null(plan);
    }

    [Fact]
    public void Map_WithoutADeclaration_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => MailBodyCleanupPlanMapper.Map(null!));
    }

    private static ChatModelOptions Declared() => DeclaredChatModels.Section(
        DeclaredChatModels.Model("answering"),
        DeclaredChatModels.Model("cheap", model: "a-small-fast-model"));
}
