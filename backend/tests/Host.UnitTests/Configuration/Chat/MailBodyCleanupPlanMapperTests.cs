// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Chat;
using MailFathom.Infrastructure.Secrets.Discovery;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Chat;

/// <summary>
/// Covers the step between a bound body-cleanup declaration and the plan the pass runs on. What is particular to this
/// mapper is the routed model: it is the first block under <c>Chat</c> that may name a model of its own, and an unwritten
/// one has to resolve to the endpoint's rather than to an empty string the provider would refuse.
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

    /// <summary>The point of the block: a reader is waiting, so an operator may put this pass on a cheaper model without moving the one that answers questions.</summary>
    [Fact]
    public void Map_AnEnabledPassNamingItsOwnModel_RoutesToThatModelAndLeavesTheRestOfThePlanAlone()
    {
        // Arrange
        var settings = Declared();
        settings.BodyCleanup.Model = "a-small-fast-model";

        // Act
        var plan = MailBodyCleanupPlanMapper.Map(settings);
        var shared = ChatGenerationPlanMapper.Map(settings);

        // Assert
        Assert.NotNull(plan);
        Assert.NotNull(shared);
        Assert.Equal("a-small-fast-model", plan.Plan.Endpoint.RoutedModelName);
        Assert.Equal(shared.Endpoint.Alias, plan.Plan.Endpoint.Alias);
        Assert.Equal(shared.Endpoint.Address, plan.Plan.Endpoint.Address);
        Assert.Equal(shared.MaximumRequestCharacters, plan.Plan.MaximumRequestCharacters);
        Assert.Equal(shared.RequestTimeout, plan.Plan.RequestTimeout);
    }

    /// <summary>A name written with whitespace around it is the same name, because an operator's configuration file is read rather than parsed twice.</summary>
    [Fact]
    public void Map_AnEnabledPassNamingAModelWithSurroundingSpace_RoutesToTheTrimmedName()
    {
        // Arrange
        var settings = Declared();
        settings.BodyCleanup.Model = "  a-small-fast-model  ";

        // Act
        var plan = MailBodyCleanupPlanMapper.Map(settings);

        // Assert
        Assert.NotNull(plan);
        Assert.Equal("a-small-fast-model", plan.Plan.Endpoint.RoutedModelName);
    }

    /// <summary>Whitespace alone is a key somebody left empty, so it routes to the endpoint's own model rather than to nothing.</summary>
    [Fact]
    public void Map_AnEnabledPassNamingOnlyWhitespace_RoutesToTheModelEverythingElseRunsOn()
    {
        // Arrange
        var settings = Declared();
        settings.BodyCleanup.Model = "   ";

        // Act
        var plan = MailBodyCleanupPlanMapper.Map(settings);

        // Assert
        Assert.NotNull(plan);
        Assert.Equal("a-chat-model", plan.Plan.Endpoint.RoutedModelName);
    }

    /// <summary>A model named beside no endpoint is refused by validation, and mapping it would build a plan with nowhere to send an outline.</summary>
    [Fact]
    public void Map_AModelWithoutAChatEndpoint_MapsNothing()
    {
        // Arrange
        var settings = new ChatModelOptions();
        settings.BodyCleanup.Model = "a-small-fast-model";

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

    private static ChatModelOptions Declared() => new()
    {
        Alias = "answering",
        Model = "a-chat-model",
        ApiKey = new ConfiguredSecret { SecretReference = "env:CHAT_KEY" },
    };
}
