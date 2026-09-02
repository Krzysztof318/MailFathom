// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Domain.Mutations;
using Xunit;

namespace MailFathom.Domain.UnitTests.Mutations;

public sealed class MailboxMutationLifecycleTests
{
    /// <summary>Every stage a record can carry has to have a lifecycle, or a count would silently omit those records.</summary>
    [Fact]
    public void Of_EveryDeclaredStage_NamesALifecycle()
    {
        // Arrange
        var declaredStages = Enum.GetValues<MailboxMutationStage>();

        // Act
        var lifecycles = declaredStages.Select(MailboxMutationLifecycle.Of).ToArray();

        // Assert
        Assert.All(lifecycles, lifecycle => Assert.True(lifecycle.IsSpecified));
        Assert.All(lifecycles, lifecycle => Assert.Contains(lifecycle, MailboxMutationLifecycle.All));
    }

    /// <summary>The mapping is the whole contract, and the three converging stages collapsing into one is the point of it.</summary>
    [Theory]
    [InlineData(MailboxMutationStage.Recorded, "pending")]
    [InlineData(MailboxMutationStage.PlacementIssued, "converging")]
    [InlineData(MailboxMutationStage.PlacementConfirmed, "converging")]
    [InlineData(MailboxMutationStage.SourceFlaggedDeleted, "converging")]
    [InlineData(MailboxMutationStage.Completed, "completed")]
    [InlineData(MailboxMutationStage.Abandoned, "dead-lettered")]
    public void Of_AStage_NamesTheLifecycleAnOperatorReads(MailboxMutationStage stage, string expectedName)
    {
        // Act
        var lifecycle = MailboxMutationLifecycle.Of(stage);

        // Assert
        Assert.Equal(expectedName, lifecycle.Name);
    }

    /// <summary>A number no member carries is a stage this build does not have, and reporting it as one would be a lie.</summary>
    [Fact]
    public void Of_AStageThisBuildDoesNotDeclare_IsRefused()
    {
        // Act
        var refusal = Assert.Throws<ArgumentOutOfRangeException>(
            () => MailboxMutationLifecycle.Of((MailboxMutationStage)97));

        // Assert
        Assert.Equal("stage", refusal.ParamName);
    }

    /// <summary>The name is the published identity, so a name that round-trips is the whole of what the type guarantees.</summary>
    [Fact]
    public void TryParseName_EveryDeclaredName_ReturnsTheLifecycleThatCarriesIt()
    {
        // Act
        var reparsed = MailboxMutationLifecycle.All
            .Select(lifecycle => MailboxMutationLifecycle.TryParseName(lifecycle.Name, out var parsed)
                ? parsed
                : default)
            .ToArray();

        // Assert
        Assert.Equal(MailboxMutationLifecycle.All, reparsed);
    }

    /// <summary>A name that was never a lifecycle must be recognized as unknown rather than reconstructed.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("stuck")]
    [InlineData("DeadLettered")]
    public void TryParseName_AnUnknownName_IsRefused(string? name)
    {
        // Act
        var parsed = MailboxMutationLifecycle.TryParseName(name, out var lifecycle);

        // Assert
        Assert.False(parsed);
        Assert.False(lifecycle.IsSpecified);
    }

    /// <summary>The struct default is reachable and names nothing, which every member has to say rather than pretend otherwise.</summary>
    [Fact]
    public void Name_TheStructDefault_IsRefused()
    {
        // Arrange
        var unspecified = default(MailboxMutationLifecycle);

        // Act
        var refusal = Assert.Throws<InvalidOperationException>(() => unspecified.Name);

        // Assert
        Assert.False(unspecified.IsSpecified);
        Assert.Equal("(unspecified)", unspecified.ToString());
        Assert.Contains("names no mutation lifecycle", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>The serialized form is the name for the same reason the value object exists.</summary>
    [Fact]
    public void Serialization_ALifecycle_RoundTripsThroughItsName()
    {
        // Act
        var json = JsonSerializer.Serialize(MailboxMutationLifecycle.DeadLettered);
        var restored = JsonSerializer.Deserialize<MailboxMutationLifecycle>(json);

        // Assert
        Assert.Equal("\"dead-lettered\"", json);
        Assert.Equal(MailboxMutationLifecycle.DeadLettered, restored);
    }

    /// <summary>A document naming a lifecycle this build does not have is refused rather than read as the nearest one.</summary>
    [Theory]
    [InlineData("\"stuck\"")]
    [InlineData("7")]
    public void Deserialization_AValueThatNamesNoLifecycle_IsRefused(string json)
    {
        // Act, Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<MailboxMutationLifecycle>(json));
    }

    /// <summary>An unspecified value cannot be written, because nothing would name it on the way back.</summary>
    [Fact]
    public void Serialization_TheStructDefault_IsRefused()
    {
        // Act, Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(default(MailboxMutationLifecycle)));
    }
}
