// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Infrastructure.Secrets;
using MailMcp.Infrastructure.Security;
using Xunit;

namespace MailMcp.Infrastructure.UnitTests;

public sealed class McpRateLimitPartitionsTests
{
    [Fact]
    public void KeyFor_WithAnAuthenticatedName_CountsTheClientUnderIt()
    {
        // Act
        var partitionKey = McpRateLimitPartitions.KeyFor("desktop-agent");

        // Assert
        Assert.Equal("desktop-agent", partitionKey);
    }

    [Fact]
    public void KeyFor_WithDifferentAuthenticatedNames_KeepsThemApart()
    {
        // Act
        var first = McpRateLimitPartitions.KeyFor("desktop-agent");
        var second = McpRateLimitPartitions.KeyFor("nightly-indexer");

        // Assert
        Assert.NotEqual(first, second);
    }

    /// <summary>
    /// The anonymous partition holds every caller the deployment cannot tell apart, so a configured client sharing it
    /// would both spend that stream's capacity and have its own spent by it. Nothing but the spelling keeps them apart,
    /// which is why the spelling is asserted against the grammar a configured name is actually accepted under rather
    /// than left as an assumption about what an operator is likely to write.
    /// </summary>
    [Fact]
    public void AnonymousKey_CannotBeSpelledAsAConfiguredName()
    {
        // Act
        var isAcceptedAsASecretName = SecretName.TryCreate(McpRateLimitPartitions.AnonymousKey, out _);

        // Assert
        Assert.False(isAcceptedAsASecretName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void KeyFor_WithoutAnAuthenticatedName_SharesOneAnonymousPartition(string? authenticatedClientName)
    {
        // Act
        var partitionKey = McpRateLimitPartitions.KeyFor(authenticatedClientName);

        // Assert
        Assert.Equal(McpRateLimitPartitions.AnonymousKey, partitionKey);
    }
}
