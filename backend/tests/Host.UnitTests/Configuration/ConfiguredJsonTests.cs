// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Host.Configuration;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration;

/// <summary>Covers the two things configuration cannot state about a JSON document it carries: whether a node is an array, and how deep it goes.</summary>
/// <remarks>
/// What each setting becomes on the way to a request is proved where it is declared, in
/// <see cref="Chat.ChatModelAdditionalPropertiesBindingTests" />. What is left here is the reading itself, whose edges
/// no declaration would reach on purpose — an object whose members are named like indices, a node carrying a value and
/// children at once, and a tree deeper than anything may descend.
/// </remarks>
public sealed class ConfiguredJsonTests
{
    [Fact]
    public void Read_ANodeWhoseChildrenAreTheIntegersBelowTheirCount_IsAnArrayInIndexOrder()
    {
        // Act
        var value = ConfiguredJson.Read(Node(("0", "first"), ("1", "second"), ("2", "third")));

        // Assert
        Assert.Equal(JsonValueKind.Array, value.ValueKind);
        Assert.Equal(["first", "second", "third"], value.EnumerateArray().Select(element => element.GetString()));
    }

    /// <summary>A gap says the keys are names rather than positions, which is the only thing that separates the two shapes.</summary>
    [Fact]
    public void Read_ANodeWhoseIntegerChildrenLeaveAGap_IsAnObject()
    {
        // Act
        var value = ConfiguredJson.Read(Node(("0", "first"), ("2", "third")));

        // Assert
        Assert.Equal(JsonValueKind.Object, value.ValueKind);
        Assert.Equal("third", value.GetProperty("2").GetString());
    }

    /// <summary>The structure wins over a value on the same node, because a provider that carried both said the node has members.</summary>
    [Fact]
    public void Read_ANodeCarryingBothAValueAndChildren_IsReadAsItsChildren()
    {
        // Arrange
        var node = Node(("provider", "ignored"), ("provider:order:0", "anthropic"))
            .GetSection("provider");

        // Act
        var value = ConfiguredJson.Read(node);

        // Assert
        Assert.Equal("anthropic", value.GetProperty("order")[0].GetString());
    }

    [Fact]
    public void DepthOf_ALeaf_IsOneLevel()
    {
        // Act, Assert
        Assert.Equal(1, ConfiguredJson.DepthOf(Node(("top_k", "40")).GetSection("top_k")));
    }

    /// <summary>A tree past the bound is reported as past it rather than walked to the bottom, which is the whole point of measuring before descending.</summary>
    [Fact]
    public void DepthOf_ATreeDeeperThanAnythingMayDescend_StopsOneLevelPastTheBound()
    {
        // Arrange
        var key = string.Join(':', Enumerable.Repeat("level", (ConfiguredJson.GreatestNestingDepth * 4) + 1));

        // Act, Assert
        Assert.Equal(ConfiguredJson.GreatestNestingDepth + 1, ConfiguredJson.DepthOf(Node((key, "1"))));
    }

    private static IConfigurationSection Node(params (string Key, string? Value)[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(entries.Select(entry =>
                new KeyValuePair<string, string?>($"node:{entry.Key}", entry.Value)))
            .Build()
            .GetSection("node");
}
