// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailFathom.Versioning;
using Xunit;

namespace MailFathom.Mcp.UnitTests;

/// <summary>
/// Covers what the protocol surface says about itself when a client initializes a session. It is one of the two places
/// a MailFathom build is observable at run time, and the only one a client can reach.
/// </summary>
public sealed class McpServerIdentityTests
{
    [Fact]
    public void ServerInfo_ComposedSurface_NamesTheProductRatherThanTheHostAssembly()
    {
        // Act
        var serverInfo = RegisteredMcpToolSurface.ServerInfo();

        // Assert
        Assert.NotNull(serverInfo);
        Assert.Equal("MailFathom", serverInfo.Name);
    }

    /// <summary>
    /// The expectation is read from the protocol assembly's own build-time metadata rather than restated as a literal,
    /// so a registration that regressed to a hardcoded version — one that would stay plausible while the declared
    /// version moved on — fails here instead of reporting a build that is not running.
    /// </summary>
    [Fact]
    public void ServerInfo_ComposedSurface_ReportsTheVersionStampedIntoTheProtocolAssembly()
    {
        // Arrange
        var stamped = StampedAssemblyVersion.ReadFrom(typeof(McpServiceCollectionExtensions).Assembly);

        // Act
        var serverInfo = RegisteredMcpToolSurface.ServerInfo();

        // Assert
        Assert.NotNull(serverInfo);
        Assert.Equal(stamped.Version, serverInfo.Version);
    }

    /// <summary>
    /// The revision belongs to build provenance rather than to the compatibility statement a client reads, so it stays
    /// out of what the handshake publishes even though the same metadata carries it.
    /// </summary>
    [Fact]
    public void ServerInfo_ComposedSurface_PublishesNoSourceControlBuildMetadata()
    {
        // Act
        var serverInfo = RegisteredMcpToolSurface.ServerInfo();

        // Assert
        Assert.NotNull(serverInfo);
        Assert.NotNull(serverInfo.Version);
        Assert.DoesNotContain("+", serverInfo.Version, StringComparison.Ordinal);
    }
}
